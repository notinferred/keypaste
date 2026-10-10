using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Linking;

/// <summary>The Linux link: a <c>sh</c> script that starts one keypaste and names the version that wrote it.</summary>
/// <remarks>Only a file that is exactly what <see cref="Document"/> writes is keypaste's; anything else at the path is the person's.</remarks>
internal static class CliLinkScript
{
    /// <summary>The longest file read as a link.</summary>
    internal const int MaximumLength = 4096;

    private const string _shebang = "#!/bin/sh";
    private const string _wrotePrefix = "# keypaste ";
    private const string _wroteSuffix = " wrote this; a newer keypaste re-points it when it starts.";
    private const string _execPrefix = "exec '";
    private const string _throughAppImage = "' cli \"$@\"";
    private const string _direct = "' \"$@\"";
    private const string _escapedQuote = @"'\''";

    /// <summary>The script that starts <paramref name="target"/> with every argument unchanged.</summary>
    /// <param name="target">The program.</param>
    /// <param name="version">The version writing it.</param>
    /// <returns>The script, or null when the program is not an absolute path or holds a line break or a NUL.</returns>
    internal static string? Document(CliTarget target, SemanticVersion version)
    {
        if (!target.Program.StartsWith('/') || target.Program.AsSpan().IndexOfAny('\n', '\r', '\0') >= 0)
        {
            return null;
        }

        return string.Join(
            '\n',
            _shebang,
            _wrotePrefix + version.ToString() + _wroteSuffix,
            _execPrefix + target.Program.Replace("'", _escapedQuote, StringComparison.Ordinal) + (target.ThroughAppImage ? _throughAppImage : _direct),
            string.Empty);
    }

    /// <summary>Reads a script <see cref="Document"/> wrote.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="target">The program it starts, when this returns true.</param>
    /// <param name="version">The version that wrote it, when this returns true.</param>
    /// <returns>Whether <paramref name="text"/> is exactly a script keypaste writes.</returns>
    internal static bool TryRead(string text, [NotNullWhen(true)] out CliTarget? target, out SemanticVersion version)
    {
        target = null;
        version = default;

        if (text.Length > MaximumLength || !text.EndsWith('\n'))
        {
            return false;
        }

        var lines = text[..^1].Split('\n');

        if (lines.Length != 3
            || !TryBetween(lines[1], _wrotePrefix, _wroteSuffix, out var written)
            || !SemanticVersion.TryParse(written, out version))
        {
            return false;
        }

        var throughAppImage = lines[2].EndsWith(_throughAppImage, StringComparison.Ordinal);

        if (!TryBetween(lines[2], _execPrefix, throughAppImage ? _throughAppImage : _direct, out var quoted))
        {
            return false;
        }

        // Writing what was read again must give the same text, which also refuses any quote the writer would have escaped.
        var read = new CliTarget(quoted.Replace(_escapedQuote, "'", StringComparison.Ordinal), throughAppImage);

        if (!string.Equals(Document(read, version), text, StringComparison.Ordinal))
        {
            return false;
        }

        target = read;
        return true;
    }

    private static bool TryBetween(string line, string prefix, string suffix, out string between)
    {
        var fits = line.Length >= prefix.Length + suffix.Length
            && line.StartsWith(prefix, StringComparison.Ordinal)
            && line.EndsWith(suffix, StringComparison.Ordinal);

        between = fits ? line[prefix.Length..^suffix.Length] : string.Empty;
        return fits;
    }
}
