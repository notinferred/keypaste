using System.Buffers;
using System.Globalization;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>What an agent's <c>run</c> call asked for, before anything is resolved.</summary>
/// <param name="Command">The program as the agent named it, then its arguments.</param>
/// <param name="Directory">The directory it runs in.</param>
/// <param name="Project">The env project whose set it wants, or null in reference mode.</param>
/// <param name="Profile">The project's profile, or null for the default.</param>
/// <param name="Keys">Only these keys of the set, or null for the whole set.</param>
/// <param name="References">Each variable name and the <c>kp://</c> reference it takes, or null in set mode.</param>
/// <param name="Reason">The agent's stated reason.</param>
/// <param name="TimeoutSeconds">How long the command may run once started.</param>
public sealed record RunArguments(
    IReadOnlyList<string> Command,
    string Directory,
    string? Project,
    string? Profile,
    IReadOnlyList<string>? Keys,
    IReadOnlyList<RunReference>? References,
    string Reason,
    int TimeoutSeconds);

/// <summary>What a <c>run</c> request must satisfy before anybody is asked about it.</summary>
/// <remarks>
/// <para>
/// The bridge applies it so it can name the argument to the agent, and the vault's owner applies it
/// again to every request it is sent, as <see cref="CredentialRequestRules"/> is applied.
/// </para>
/// <para>
/// A command item or directory holding a control character, or anything
/// <see cref="DisplayTextSanitizer"/> would change, is refused rather than scrubbed, so a person is
/// only ever shown the text that runs (THREATS.md T-35). Names are compared case-insensitively on
/// every platform, as <see cref="EnvNameRules"/> compares them.
/// </para>
/// </remarks>
public static class RunRequestRules
{
    /// <summary>The most variables one run injects.</summary>
    public const int MaximumVariables = 32;

    /// <summary>The most command items one run names.</summary>
    public const int MaximumArguments = 256;

    /// <summary>The longest single command item.</summary>
    public const int MaximumArgumentLength = 4096;

    /// <summary>How long a command runs when the agent does not say.</summary>
    public const int DefaultTimeoutSeconds = 120;

    /// <summary>The longest a command may run.</summary>
    public const int MaximumTimeoutSeconds = 600;

    /// <summary>The characters <c>cmd.exe</c> gives a meaning to, refused in a batch file's arguments.</summary>
    private static readonly SearchValues<char> _batchMetacharacters = SearchValues.Create("\"&|<>^%!()\r\n");

    /// <summary>Finds the first argument a run breaks the rules with.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The argument and its rule, or null when the run is well formed.</returns>
    public static CredentialRequestProblem? Check(RunArguments run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return CheckCommand(run.Command)
            ?? CheckDirectory(run.Directory)
            ?? CheckVariables(run)
            ?? (run.Reason.Length is 0 or > CredentialRequestRules.MaximumReasonLength
                ? new CredentialRequestProblem("reason", Length(CredentialRequestRules.MaximumReasonLength))
                : null)
            ?? (run.TimeoutSeconds is < 1 or > MaximumTimeoutSeconds
                ? new CredentialRequestProblem("timeout_seconds", string.Create(CultureInfo.InvariantCulture, $"must be between 1 and {MaximumTimeoutSeconds}"))
                : null);
    }

    /// <summary>Whether the program the bridge resolved may run with these arguments.</summary>
    /// <param name="program">The program's absolute path.</param>
    /// <param name="command">The command, whose first item named the program.</param>
    /// <returns>The problem, or null.</returns>
    /// <remarks>
    /// A batch file runs through <c>cmd.exe</c>, which parses its own command line: no quoting of an
    /// argument list survives it, so an argument holding one of its metacharacters is refused rather
    /// than passed to a shell the person never saw.
    /// </remarks>
    public static CredentialRequestProblem? CheckProgram(string program, IReadOnlyList<string> command)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(command);

        if (program.Length is 0 or > EnvReleasePrompt.MaximumDirectoryLength
            || !Path.IsPathFullyQualified(program)
            || !Shown(program))
        {
            return new("command", "must name a program keypaste can find and show whole");
        }

        if (!IsBatchFile(program))
        {
            return null;
        }

        if (program.AsSpan().ContainsAny(_batchMetacharacters))
        {
            return new("command", "names a batch file whose path cmd.exe would reinterpret");
        }

        return command.Skip(1).Any(argument => argument.AsSpan().ContainsAny(_batchMetacharacters) || argument.EndsWith('\\'))
            ? new("command", "runs a batch file through cmd.exe, so no argument may hold \" & | < > ^ % ! ( ) or end in \\")
            : null;
    }

    /// <summary>Whether a program runs through <c>cmd.exe</c>.</summary>
    /// <param name="program">The program's path.</param>
    /// <returns>True for <c>.bat</c> and <c>.cmd</c>, in any case, ignoring trailing dots and spaces as Windows does.</returns>
    public static bool IsBatchFile(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var trimmed = program.TrimEnd('.', ' ');
        return trimmed.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a vault value may never be injected under this name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>True for <c>PATH</c> and every <c>KEYPASTE_</c> name, in any case.</returns>
    /// <remarks>A value must not choose which program runs, nor speak as keypaste's own settings.</remarks>
    public static bool IsReservedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("KEYPASTE_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a variable changes how programs start, which a prompt warns about.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>True for loader, interpreter and proxy settings.</returns>
    public static bool ChangesHowProgramsStart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var upper = name.ToUpperInvariant();
        return upper.StartsWith("LD_", StringComparison.Ordinal)
            || upper.StartsWith("DYLD_", StringComparison.Ordinal)
            || upper.StartsWith("PYTHON", StringComparison.Ordinal)
            || upper.EndsWith("_PROXY", StringComparison.Ordinal)
            || upper is "NODE_OPTIONS" or "BASH_ENV" or "ENV" or "PERL5OPT" or "RUBYOPT" or "GIT_SSH_COMMAND";
    }

    /// <summary>Why a list of variable names may not be injected, or null.</summary>
    /// <param name="names">The names.</param>
    /// <returns>What the names must be, completing a sentence that begins with what named them; never a value.</returns>
    public static string? NameProblem(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count is 0 or > MaximumVariables)
        {
            return string.Create(CultureInfo.InvariantCulture, $"must name 1 to {MaximumVariables} variables");
        }

        if (names.Any(IsReservedName))
        {
            return "must not name PATH or a KEYPASTE_ variable";
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count
            ? null
            : "must not name two variables that differ only in case";
    }

    private static CredentialRequestProblem? CheckCommand(IReadOnlyList<string> command)
    {
        if (command.Count is 0 or > MaximumArguments || command[0].Length == 0)
        {
            return new("command", string.Create(CultureInfo.InvariantCulture, $"must name a program and at most {MaximumArguments - 1} arguments"));
        }

        if (command.Any(item => item.Length > MaximumArgumentLength))
        {
            return new("command", string.Create(CultureInfo.InvariantCulture, $"items must be at most {MaximumArgumentLength} characters"));
        }

        if (!command.All(Shown))
        {
            return new("command", "must hold no control character and nothing a prompt cannot show as it is");
        }

        return EnvReleasePrompt.CommandLine(command).Length > EnvReleasePrompt.MaximumCommandLength
            ? new("command", string.Create(CultureInfo.InvariantCulture, $"must be at most {EnvReleasePrompt.MaximumCommandLength} characters as one line"))
            : null;
    }

    private static CredentialRequestProblem? CheckDirectory(string directory)
    {
        if (directory.Length is 0 or > EnvReleasePrompt.MaximumDirectoryLength || !Shown(directory))
        {
            return new("directory", string.Create(CultureInfo.InvariantCulture, $"must be 1 to {EnvReleasePrompt.MaximumDirectoryLength} characters a prompt can show as they are"));
        }

        if (!Path.IsPathFullyQualified(directory)
            || !string.Equals(directory, Path.GetFullPath(directory), StringComparison.Ordinal)
            || (OperatingSystem.IsWindows() && (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal))))
        {
            return new("directory", "must be an absolute local path with no '..' or '.' segment");
        }

        return null;
    }

    private static CredentialRequestProblem? CheckVariables(RunArguments run)
    {
        if ((run.Project is null) == (run.References is null))
        {
            return new("project", "must be given, or env, but not both");
        }

        if (run.References is { } references)
        {
            if (run.Profile is not null || run.Keys is not null)
            {
                return new("env", "must not be given with profile or keys");
            }

            if (NameProblem([.. references.Select(reference => reference.Name)]) is { } names)
            {
                return new("env", names);
            }

            foreach (var reference in references)
            {
                if (!EnvConvention.IsValidKey(reference.Name, out _))
                {
                    return new("env", "names must be environment variable names");
                }

                if (!KpReferences.TryParse(reference.Reference, out _, out _))
                {
                    return new("env", "values must be kp:// references to an env value or an entry");
                }
            }

            return null;
        }

        if (!EnvConvention.IsValidProject(run.Project!, out _))
        {
            return new("project", "must be an env project name");
        }

        if (run.Profile is { } profile && !EnvProfileNames.IsValid(profile, out _))
        {
            return new("profile", "must be a profile name: lowercase letters, digits and '-'");
        }

        if (run.Keys is { } keys)
        {
            if (keys.Any(key => !EnvConvention.IsValidKey(key, out _)))
            {
                return new("keys", "must be environment variable names");
            }

            if (NameProblem(keys) is { } names)
            {
                return new("keys", names);
            }
        }

        return null;
    }

    /// <summary>Whether text is shown exactly as it is: no control character and nothing the display sanitizer would change.</summary>
    private static bool Shown(string text) =>
        !text.Any(char.IsControl)
        && !DisplayTextSanitizer.Sanitize(text, Math.Max(1, text.Length)).WasAltered;

    private static string Length(int maximum) =>
        string.Create(CultureInfo.InvariantCulture, $"must be 1 to {maximum} characters");
}
