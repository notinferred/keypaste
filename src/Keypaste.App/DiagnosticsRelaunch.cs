#pragma warning disable RS0030 // The app's one start of itself: the same program, given its own arguments.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Keypaste.App;

/// <summary>Starts the Windows app again with .NET's diagnostics port off, which would let any process of the same user read the unlocked vault.</summary>
/// <remarks>
/// The runtime reads <c>DOTNET_EnableDiagnostics</c> only from its environment, which the macOS bundle and the AppImage set
/// and the MSI cannot: its shortcut, the Run key and the file type's command carry no environment.
/// </remarks>
internal static class DiagnosticsRelaunch
{
    internal const string Variable = "DOTNET_EnableDiagnostics";

    private const string _app = "keypaste-app.exe";

    /// <summary>Whether a start must relaunch: the variable is unset, which a relaunch never leaves it, and the process is keypaste-app itself rather than a host running it.</summary>
    /// <param name="setting">The variable's value, null when unset; any value, <c>1</c> included, is kept as the person's own.</param>
    /// <param name="processPath">The running executable.</param>
    internal static bool IsNeeded(string? setting, string? processPath) =>
        setting is null && string.Equals(Path.GetFileName(processPath), _app, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs <paramref name="program"/> with <paramref name="args"/> and the port off, in this working directory, passing it the right to the foreground, and waits for it.</summary>
    /// <returns>Its exit code, or null when it could not be started, so this start runs as before.</returns>
    [SupportedOSPlatform("windows")]
    internal static int? Run(string program, string[] args)
    {
        // Redirecting input alone makes .NET hand the child this process's own output and error, which --version and --selftest write to.
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardInput = true };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[Variable] = "0";

        try
        {
            using var child = Process.Start(start);
            if (child is null)
            {
                return null;
            }

            WindowsForeground.LetProcessTakeIt(child.Id);
            child.StandardInput.Close();
            child.WaitForExit();
            return child.ExitCode;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
