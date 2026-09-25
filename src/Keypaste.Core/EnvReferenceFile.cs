using System.Globalization;

namespace Keypaste.Core;

/// <summary>One line of a <c>.env.keypaste</c>: a variable named by a reference, or a literal passed through.</summary>
/// <param name="Name">The variable the child gets.</param>
/// <param name="Reference">Where its value lives, or null for a literal.</param>
/// <param name="Literal">The value as written, or null for a reference.</param>
/// <param name="Line">The line it was read from, or zero for one never read from a file.</param>
public sealed record ReferenceLine(string Name, KpReference? Reference, string? Literal, int Line);

/// <summary>A parsed <c>.env.keypaste</c>.</summary>
/// <param name="Lines">The variables, in the file's order.</param>
/// <param name="Problems">Everything wrong with it; a file with any problem resolves nothing.</param>
public sealed record EnvReferenceDocument(IReadOnlyList<ReferenceLine> Lines, IReadOnlyList<DotEnvProblem> Problems);

/// <summary>
/// The commit-safe <c>.env.keypaste</c>: dotenv syntax whose values are <c>kp://</c> references, or
/// literals that pass through as written.
/// </summary>
/// <remarks>
/// A cloned repository's file is written by whoever controls that repository (THREATS.md T-31), so
/// nothing here decides whether a file may be used; <see cref="IsDiscoverableFor"/> answers the one
/// question <c>run</c> asks before using a file it found rather than one it was given.
/// </remarks>
public static class EnvReferenceFile
{
    /// <summary>The file <c>run</c> looks for in the directory it starts in.</summary>
    public const string FileName = ".env.keypaste";

    /// <summary>The comment block a written file starts with.</summary>
    public const string Header =
        "# keypaste references: safe to commit, no value is stored here.\n" +
        "# `keypaste run --env-file .env.keypaste -- <command>` resolves them.\n";

    /// <summary>Writes a file referencing every key of one profile.</summary>
    /// <param name="project">The project.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="keys">The variable names, in the order written.</param>
    /// <returns>The file's text.</returns>
    public static string Format(string project, string profile, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(keys);

        var text = new StringBuilder(Header);

        foreach (var key in keys)
        {
            text.Append(key).Append('=').Append(Quoted(KpReferences.For(project, profile, key))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads a file.</summary>
    /// <param name="bytes">The file's contents, at most <see cref="DotEnv.MaximumBytes"/>.</param>
    /// <param name="document">What it holds, and every problem.</param>
    /// <returns>Whether it holds no problem.</returns>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out EnvReferenceDocument document)
    {
        if (!DotEnv.TryDecode(bytes, out var text, out var decodeError))
        {
            document = new EnvReferenceDocument([], [new DotEnvProblem(0, decodeError)]);
            return false;
        }

        _ = DotEnv.TryParse(text, out var parsed);

        List<ReferenceLine> lines = [];
        List<DotEnvProblem> problems = [.. parsed.Problems];

        foreach (var variable in parsed.Variables)
        {
            if (!variable.Value.StartsWith(KpReferences.Scheme, StringComparison.Ordinal))
            {
                lines.Add(new ReferenceLine(variable.Key, null, variable.Value, variable.Line));
            }
            else if (KpReferences.TryParse(variable.Value, out var reference, out var error))
            {
                lines.Add(new ReferenceLine(variable.Key, reference, null, variable.Line));
            }
            else
            {
                problems.Add(new DotEnvProblem(variable.Line, string.Create(CultureInfo.InvariantCulture, $"line {variable.Line}: '{variable.Key}' is not a usable reference: {error}")));
            }
        }

        problems.Sort(static (a, b) => a.Line.CompareTo(b.Line));
        document = new EnvReferenceDocument(lines, problems);
        return problems.Count == 0;
    }

    /// <summary>Whether a file found in a directory may be used for the project that directory is mapped to.</summary>
    /// <param name="document">The file.</param>
    /// <param name="inferredProject">The project <c>projects.json</c> maps the directory to.</param>
    /// <param name="reason">What the file names instead, when this returns false.</param>
    /// <returns>True only for env references to exactly <paramref name="inferredProject"/>, beside any literals.</returns>
    public static bool IsDiscoverableFor(EnvReferenceDocument document, string inferredProject, out string reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inferredProject);

        var projects = document.Lines
            .Select(line => line.Reference)
            .OfType<EnvReference>()
            .Select(reference => reference.Project)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        reason = document.Lines.Any(line => line.Reference is EntryReference) ? "vault entries"
            : projects.Count > 1 ? $"several projects ({string.Join(", ", projects)})"
            : projects.Count == 0 ? "no project"
            : !string.Equals(projects[0], inferredProject, StringComparison.Ordinal) ? $"project '{projects[0]}'"
            : string.Empty;

        return reason.Length == 0;
    }

    /// <summary>The file with every env reference moved to one profile; entry references and literals are unchanged.</summary>
    /// <param name="document">The file.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The rewritten file.</returns>
    public static EnvReferenceDocument WithProfile(EnvReferenceDocument document, string profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);

        return document with
        {
            Lines =
            [
                .. document.Lines.Select(line => line.Reference is EnvReference env
                    ? line with { Reference = env with { Profile = profile } }
                    : line),
            ],
        };
    }

    /// <summary>Writes a reference file as UTF-8, replacing one only when told to.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">What <see cref="Format"/> made.</param>
    /// <param name="replace">Whether a file already there may be replaced.</param>
    /// <param name="error">Why nothing was written, otherwise empty.</param>
    /// <returns>Whether the file was written.</returns>
    /// <remarks>The caller refuses a destination that is a vault: this writes references, never a vault's bytes.</remarks>
    public static bool TryWrite(string path, string text, bool replace, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            using var file = new FileStream(path, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            file.Write(Encoding.UTF8.GetBytes(text));
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = File.Exists(path) && !replace ? $"{path} already exists" : ex.Message;
            return false;
        }
    }

    private static string Quoted(string reference) =>
        reference.Any(c => c == '#' || char.IsWhiteSpace(c)) ? $"\"{reference}\"" : reference;
}
