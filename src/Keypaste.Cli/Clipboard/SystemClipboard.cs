using System.Security.Cryptography;
using System.Text;
using Keypaste.Cli.Prompting;

namespace Keypaste.Cli.Clipboard;

/// <summary>
/// The system clipboard, driven through the tool each platform already ships.
/// </summary>
/// <remarks>
/// <para>
/// Shelling out rather than P/Invoke: there is no clipboard in the BCL, three native APIs would
/// mean three <c>DllImport</c> surfaces for the trim and AOT analyzers to argue with
/// (DECISIONS.md D-0005), and a user debugging a problem can reproduce a subprocess by hand.
/// </para>
/// <para>
/// <b>The secret is written to the tool's stdin, never passed as an argument.</b>
/// <c>/proc/&lt;pid&gt;/cmdline</c> is world-readable on Linux, <c>Win32_Process.CommandLine</c> is
/// readable over WMI on Windows, and Sysmon ships full command lines to a SIEM by default.
/// </para>
/// <para>
/// Windows and macOS tools are invoked by <b>absolute path</b>, because we are piping a plaintext
/// password into whatever this resolves to and a <c>clip.exe</c> planted earlier on <c>PATH</c>
/// would receive it. Linux has no fixed location for <c>wl-copy</c>, <c>xclip</c> or <c>xsel</c>,
/// so those go through <c>PATH</c>; that asymmetry is a real, accepted residual risk.
/// </para>
/// </remarks>
internal sealed class SystemClipboard : IClipboard
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;
    private readonly IEnvironmentProbe _environment;

    internal SystemClipboard(IProcessRunner runner, IEnvironmentProbe environment)
    {
        _runner = runner;
        _environment = environment;
    }

    /// <inheritdoc/>
    public ClipboardStatus TrySet(string text, out string error)
    {
        return Invoke(ClipboardAction.Set, text, out _, out error);
    }

    /// <inheritdoc/>
    public ClipboardStatus TryReadHash(out byte[] sha256, out string error)
    {
        sha256 = [];

        var status = Invoke(ClipboardAction.Read, stdin: null, out var output, out error);
        if (status != ClipboardStatus.Ok)
        {
            return status;
        }

        sha256 = SHA256.HashData(Encoding.UTF8.GetBytes(output));
        return ClipboardStatus.Ok;
    }

    /// <inheritdoc/>
    public ClipboardStatus TryClear(out string error)
    {
        return Invoke(ClipboardAction.Clear, stdin: string.Empty, out _, out error);
    }

    private ClipboardStatus Invoke(
        ClipboardAction action,
        string? stdin,
        out string output,
        out string error)
    {
        output = string.Empty;
        error = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            return RunWindows(action, stdin, ref output, ref error);
        }

        if (OperatingSystem.IsMacOS())
        {
            return RunMacOs(action, stdin, ref output, ref error);
        }

        return RunLinux(action, stdin, ref output, ref error);
    }

    private ClipboardStatus RunWindows(
        ClipboardAction action,
        string? stdin,
        ref string output,
        ref string error)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);

        if (action == ClipboardAction.Read)
        {
            var powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            var read = _runner.Run(
                powershell,
                ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.Write((Get-Clipboard -Raw))"],
                stdin: null,
                new UTF8Encoding(false),
                _timeout);

            return Complete(read, ref output, ref error, "powershell.exe");
        }

        // clip.exe reads its stdin as UTF-16LE and mojibakes anything else. The BOM is
        // suppressed because clip.exe would otherwise place a leading U+FEFF on the clipboard
        // for the user to paste.
        var result = _runner.Run(
            Path.Combine(system, "clip.exe"),
            [],
            stdin ?? string.Empty,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            _timeout);

        return Complete(result, ref output, ref error, "clip.exe");
    }

    private ClipboardStatus RunMacOs(
        ClipboardAction action,
        string? stdin,
        ref string output,
        ref string error)
    {
        if (action == ClipboardAction.Read)
        {
            var read = _runner.Run("/usr/bin/pbpaste", [], null, Utf8, _timeout);
            return Complete(read, ref output, ref error, "pbpaste");
        }

        var result = _runner.Run("/usr/bin/pbcopy", [], stdin ?? string.Empty, Utf8, _timeout);
        return Complete(result, ref output, ref error, "pbcopy");
    }

    private ClipboardStatus RunLinux(
        ClipboardAction action,
        string? stdin,
        ref string output,
        ref string error)
    {
        var wayland = !string.IsNullOrEmpty(_environment.Get("WAYLAND_DISPLAY"));
        var x11 = !string.IsNullOrEmpty(_environment.Get("DISPLAY"));

        if (!wayland && !x11)
        {
            // Nothing is spawned. On a headless server "install xclip" is actively wrong
            // advice, so the caller is told there is no display at all.
            error = "no graphical session (neither WAYLAND_DISPLAY nor DISPLAY is set)";
            return ClipboardStatus.NoDisplay;
        }

        // wl-copy first when Wayland is present: XWayland usually leaves DISPLAY set too, and
        // going through xclip on a Wayland session copies into a compatibility selection that
        // native applications do not read.
        List<Candidate> candidates = [];
        if (wayland)
        {
            candidates.Add(action == ClipboardAction.Read
                ? new Candidate("wl-paste", ["--no-newline"])
                : action == ClipboardAction.Clear
                    ? new Candidate("wl-copy", ["--clear"])
                    : new Candidate("wl-copy", []));
        }

        if (x11)
        {
            candidates.Add(action == ClipboardAction.Read
                ? new Candidate("xclip", ["-selection", "clipboard", "-out"])
                : new Candidate("xclip", ["-selection", "clipboard", "-in"]));
            candidates.Add(action == ClipboardAction.Read
                ? new Candidate("xsel", ["--clipboard", "--output"])
                : new Candidate("xsel", ["--clipboard", "--input"]));
        }

        foreach (var candidate in candidates)
        {
            var payload = candidate.Arguments.Contains("--clear") ? null : stdin;
            var result = _runner.Run(candidate.Tool, candidate.Arguments, payload, Utf8, _timeout);

            // Only a missing executable moves on to the next candidate. A tool that exists and
            // failed is a hard error: falling through would hide a real misconfiguration.
            if (!result.ToolFound)
            {
                continue;
            }

            return Complete(result, ref output, ref error, candidate.Tool);
        }

        error = "no clipboard tool found (looked for wl-copy, xclip, xsel)";
        return ClipboardStatus.NoTool;
    }

    private static Encoding Utf8 => new UTF8Encoding(false);

    private static ClipboardStatus Complete(
        ProcessResult result,
        ref string output,
        ref string error,
        string tool)
    {
        if (!result.ToolFound)
        {
            error = $"{tool} not found";
            return ClipboardStatus.NoTool;
        }

        if (result.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"{tool} exited {result.ExitCode}"
                : $"{tool}: {result.StandardError.Trim()}";
            return ClipboardStatus.Failed;
        }

        output = result.StandardOutput;
        return ClipboardStatus.Ok;
    }

    private enum ClipboardAction
    {
        Set,
        Read,
        Clear,
    }

    private readonly record struct Candidate(string Tool, IReadOnlyList<string> Arguments);
}
