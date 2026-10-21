using System.ComponentModel;
using System.Diagnostics;
using Keypaste.App.Session;

namespace Keypaste.App.Views;

/// <summary>Hands an address to the platform's opener as its one argument, with no shell between, and reports the opener's exit.</summary>
/// <remarks>Avalonia's launcher runs <c>/bin/sh -c "xdg-open ..."</c> on Linux, which adds backslashes and expands <c>$NAME</c>, and on macOS and Linux it reports success before the opener has said whether anything handles the address.</remarks>
internal sealed class OpenerProcess(string opener, TimeSpan patience) : IWebLauncher
{
    public static OpenerProcess? ForCurrentPlatform() =>
        OperatingSystem.IsLinux() ? new("xdg-open", TimeSpan.FromSeconds(10))
        : OperatingSystem.IsMacOS() ? new("open", TimeSpan.FromSeconds(10))
        : null;

    public async Task<bool> OpenAsync(Uri address)
    {
        var start = new ProcessStartInfo(opener) { UseShellExecute = false };
        start.ArgumentList.Add(address.AbsoluteUri);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            using var waiting = new CancellationTokenSource(patience);
            await process.WaitForExitAsync(waiting.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // An opener that falls back to running the handler in the foreground has started it by now.
            return true;
        }
    }
}
