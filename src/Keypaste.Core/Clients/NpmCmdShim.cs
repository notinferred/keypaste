namespace Keypaste.Core.Clients;

/// <summary>Reads the <c>.cmd</c> launcher npm's cmd-shim writes for a package's command on Windows.</summary>
/// <remarks>
/// <para>
/// A batch file can only be started through <c>cmd.exe</c>, which re-reads every argument. keypaste instead starts what
/// the launcher would: <c>node.exe</c> with the script it names, or the <c>.exe</c> it names, with no shell between
/// (D-0436). Only the exact text npm writes is recognised, so a launcher that does anything else is never
/// reinterpreted as one that does not.
/// </para>
/// <para>
/// Two script layouts are npm's: cmd-shim up to 9.0.1 clears <c>.JS</c> from <c>PATHEXT</c> inside the <c>ELSE</c>
/// block, and 9.0.2 on the line that starts the program. A script with shebang arguments or variables gets a different
/// text and is not recognised.
/// </para>
/// </remarks>
internal static class NpmCmdShim
{
    private const string _head = "@ECHO off\nGOTO start\n:find_dp0\nSET dp0=%~dp0\nEXIT /b\n:start\nSETLOCAL\nCALL :find_dp0\n";

    private const string _chooseNode = "\nIF EXIST \"%dp0%\\node.exe\" (\n  SET \"_prog=%dp0%\\node.exe\"\n) ELSE (\n  SET \"_prog=node\"\n";

    private const string _start = "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & ";

    private const string _clearJs = "PATHEXT=%PATHEXT:;.JS;=;%";

    private static readonly (string Before, string After, bool ThroughNode)[] _forms =
    [
        (_head + _chooseNode + "  SET " + _clearJs + "\n)\n\n" + _start + "\"%_prog%\"  \"%dp0%\\", "\" %*\n", true),
        (_head + _chooseNode + ")\n\n" + _start + "set " + _clearJs + " & \"%_prog%\"  \"%dp0%\\", "\" %*\n", true),
        (_head + "\"%dp0%\\", "\"   %*\n", false),
    ];

    /// <summary>Reads a launcher's text.</summary>
    /// <param name="text">The whole <c>.cmd</c> file.</param>
    /// <param name="relativeTarget">The script or <c>.exe</c> it starts, relative to the launcher's folder.</param>
    /// <param name="throughNode">Whether that target is a script started by <c>node</c>, rather than an <c>.exe</c> started directly.</param>
    /// <returns>Whether the text is one of npm's forms naming a target keypaste can start without <c>cmd.exe</c>.</returns>
    public static bool TryRead(string text, out string relativeTarget, out bool throughNode)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var (before, after, node) in _forms)
        {
            if (normalised.Length > before.Length + after.Length
                && normalised.StartsWith(before, StringComparison.Ordinal)
                && normalised.EndsWith(after, StringComparison.Ordinal)
                && normalised[before.Length..^after.Length] is var target
                && IsPlainRelative(target)
                && (node || target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                relativeTarget = target;
                throughNode = node;
                return true;
            }
        }

        relativeTarget = string.Empty;
        throughNode = false;
        return false;
    }

    // cmd.exe would expand % and, with delayed expansion, ! and ^, so the path it starts could differ from the text.
    private static bool IsPlainRelative(string target) =>
        !target.Any(c => char.IsControl(c) || c is '%' or '"' or '!' or '^' or ':')
        && !target.StartsWith('\\')
        && !target.StartsWith('/');
}
