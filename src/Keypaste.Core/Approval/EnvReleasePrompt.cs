using Keypaste.Core.Projects;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown before a project's env set is released to <c>keypaste run --session</c>:
/// the project, the variable names, the command and the directory, never a value.
/// </summary>
/// <remarks>
/// <para>
/// The command and directory are the runner's claim (THREATS.md T-30). They go through
/// <see cref="DisplayTextSanitizer"/>, which keeps the characters a command is made of and removes
/// everything that could draw something other than what was sent, and are never truncated: a
/// request whose command will not be shown whole is refused before anybody is asked, so no tail can
/// be hidden past the edge of the prompt.
/// </para>
/// <para>
/// Like <see cref="ApprovalPrompt"/> it has nowhere to express a default button, a deadline or a
/// layout; the gate owns the deadline.
/// </para>
/// </remarks>
public sealed record EnvReleasePrompt
{
    /// <summary>The longest command shown, and so the longest accepted, as <c>projects.json</c> allows.</summary>
    public const int MaximumCommandLength = ProjectMappings.MaximumCommandLength;

    /// <summary>The longest directory shown, and so the longest accepted.</summary>
    public const int MaximumDirectoryLength = 1024;

    /// <summary>The project, sanitized.</summary>
    public required string Project { get; init; }

    /// <summary>The variable names, which are already exportable names.</summary>
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>The command, one line, arguments with spaces quoted.</summary>
    public required string Command { get; init; }

    /// <summary>Whether <see cref="Command"/> had anything scrubbed out of it.</summary>
    public required bool CommandWasAltered { get; init; }

    /// <summary>The directory the command starts in.</summary>
    public required string Directory { get; init; }

    /// <summary>Whether <see cref="Directory"/> had anything scrubbed out of it.</summary>
    public required bool DirectoryWasAltered { get; init; }

    /// <summary>Why a request cannot be asked about, or null when it can.</summary>
    /// <param name="project">The project named.</param>
    /// <param name="command">The command, one argument per item.</param>
    /// <param name="directory">The directory.</param>
    /// <returns>keypaste's own words, or null.</returns>
    public static string? Problem(string project, IReadOnlyList<string> command, string directory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(directory);

        if (!EnvConvention.IsValidProject(project, out var invalid))
        {
            return invalid;
        }

        if (command.Count == 0 || command[0].Length == 0)
        {
            return "the request names no command";
        }

        if (Joined(command).Length > MaximumCommandLength)
        {
            return $"the command is longer than the {MaximumCommandLength} characters a prompt shows whole";
        }

        return directory.Length is 0 or > MaximumDirectoryLength
            ? $"the directory must be given, in at most {MaximumDirectoryLength} characters"
            : null;
    }

    /// <summary>Builds the prompt for one request that <see cref="Problem"/> accepted.</summary>
    /// <param name="preview">The set's names.</param>
    /// <param name="command">The command, one argument per item.</param>
    /// <param name="directory">The directory.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static EnvReleasePrompt For(EnvPreview preview, IReadOnlyList<string> command, string directory)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(directory);

        var shownCommand = OneLine(Joined(command), MaximumCommandLength);
        var shownDirectory = OneLine(directory, MaximumDirectoryLength);

        return new EnvReleasePrompt
        {
            Project = EntryNameSanitizer.Sanitize(preview.Project).Text,
            Keys = preview.Keys,
            Command = shownCommand.Text,
            CommandWasAltered = shownCommand.WasAltered,
            Directory = shownDirectory.Text,
            DirectoryWasAltered = shownDirectory.WasAltered,
        };
    }

    /// <summary>Display text on one line: a line break or tab in a command could draw a line of its own under the prompt's.</summary>
    private static SanitizedName OneLine(string raw, int maximumLength)
    {
        var text = DisplayTextSanitizer.Sanitize(raw.Replace('\n', '\0').Replace('\r', '\0').Replace('\t', '\0'), maximumLength).Text;

        return new SanitizedName(text, !string.Equals(text, raw, StringComparison.Ordinal));
    }

    /// <summary>The command as one line, quoting an argument that is empty or holds a space or a quote.</summary>
    private static string Joined(IReadOnlyList<string> command)
    {
        var line = new StringBuilder();

        foreach (var argument in command)
        {
            if (line.Length > 0)
            {
                line.Append(' ');
            }

            if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                line.Append(argument);
            }
            else
            {
                line.Append('"').Append(argument.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
        }

        return line.ToString();
    }
}
