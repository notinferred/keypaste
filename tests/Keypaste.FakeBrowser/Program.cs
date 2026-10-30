using System.Diagnostics;
using Keypaste.Core.Browser;

namespace Keypaste.FakeBrowser;

/// <summary>
/// <c>Keypaste.FakeBrowser &lt;host&gt; chrome|edge|firefox [action]...</c>: starts a native messaging host as that browser
/// starts KeePassXC-Browser's, exchanges keys, sends each action sealed with nothing else in it, and prints each answer as one
/// line of JSON, opened where the host sealed it. It exits with the host's exit code once it has closed the host's input.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is not [var host, var browser, .. var actions] || BrowserLaunch.Arguments(browser, OperatingSystem.IsWindows()) is not { } arguments)
        {
            await Console.Error.WriteLineAsync("usage: Keypaste.FakeBrowser <host> chrome|edge|firefox [action]...").ConfigureAwait(false);
            return 2;
        }

        var start = new ProcessStartInfo(host) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{host} did not start");
        using var client = new ExtensionClient(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);

        await Console.Out.WriteLineAsync((await client.ChangePublicKeysAsync(CancellationToken.None).ConfigureAwait(false)).ToJsonString()).ConfigureAwait(false);

        foreach (var action in actions)
        {
            var answer = await client.RequestAsync(action, null, CancellationToken.None).ConfigureAwait(false);
            await Console.Out.WriteLineAsync((answer.Message ?? answer.Reply).ToJsonString()).ConfigureAwait(false);
        }

        process.StandardInput.Close();
        await process.WaitForExitAsync().ConfigureAwait(false);

        return process.ExitCode;
    }
}

/// <summary>The arguments each browser starts KeePassXC-Browser's native messaging host with.</summary>
public static class BrowserLaunch
{
    /// <summary>The arguments <paramref name="browser"/> passes, or null for a browser this fake does not stand in for.</summary>
    /// <param name="browser"><c>chrome</c>, <c>edge</c> or <c>firefox</c>.</param>
    /// <param name="windows">Whether to pass them as on Windows, where Chrome adds the window it was called from.</param>
    /// <returns>The arguments, without the program.</returns>
    public static string[]? Arguments(string browser, bool windows) => browser switch
    {
        "chrome" => Chromium(BrowserIdentity.ChromeWebStore, windows),
        "edge" => Chromium(BrowserIdentity.EdgeAddOns, windows),
        "firefox" => [Path.Combine(AppContext.BaseDirectory, "org.keepassxc.keepassxc_browser.json"), BrowserIdentity.FirefoxAddOns.Caller],
        _ => null,
    };

    // A service worker calls from no window, which Chrome passes as 0.
    private static string[] Chromium(BrowserIdentity identity, bool windows) => windows ? [identity.Caller, "--parent-window=0"] : [identity.Caller];
}
