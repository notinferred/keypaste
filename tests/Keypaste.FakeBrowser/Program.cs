using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Keypaste.Browser;
using Keypaste.Core.Browser;

namespace Keypaste.FakeBrowser;

/// <summary>
/// <c>Keypaste.FakeBrowser &lt;host&gt; chrome|edge|firefox [--id-key &lt;base64&gt;] [--within &lt;seconds&gt;] [action]...</c>: starts a
/// native messaging host as that browser starts KeePassXC-Browser's, exchanges keys, sends each action sealed, and prints each
/// answer as one line of JSON, opened where the host sealed it. It exits with the host's exit code once it has closed the host's
/// input, or with 124, ending the host, when the host has not answered everything within <c>--within</c>'s seconds.
/// </summary>
/// <remarks>
/// <c>associate</c> sends the session key and the identification key, as the extension pairs; <c>test-associate=&lt;id&gt;</c>
/// presents the pairing <c>id</c> with the identification key, as it checks one, and a bare <c>test-associate</c> the id the last
/// <c>associate</c> was answered with; any other action is sent with nothing else in it. The identification key is
/// <c>--id-key</c>'s, so two runs can pair and then check, or a fresh one. A host may be any program that speaks the protocol,
/// <c>keepassxc-proxy</c> included, which drops what it cannot hand KeePassXC, hence <c>--within</c>.
/// </remarks>
internal static class Program
{
    private const string _usage =
        "usage: Keypaste.FakeBrowser <host> chrome|edge|firefox [--id-key <base64>] [--within <seconds>] [action | test-associate=<id>]...";

    private static async Task<int> Main(string[] args)
    {
        if (args is not [var host, var browser, .. var rest] || BrowserLaunch.Arguments(browser, OperatingSystem.IsWindows()) is not { } arguments)
        {
            await Console.Error.WriteLineAsync(_usage).ConfigureAwait(false);
            return 2;
        }

        using var fresh = NaclBox.NewKeyPair();
        var identificationKey = Convert.ToBase64String(fresh.PublicKey);
        var within = Timeout.InfiniteTimeSpan;

        while (rest is [var option, var value, .. var after] && option.StartsWith("--", StringComparison.Ordinal))
        {
            switch (option)
            {
                case "--id-key":
                    identificationKey = value;
                    break;
                case "--within" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds):
                    within = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    await Console.Error.WriteLineAsync(_usage).ConfigureAwait(false);
                    return 2;
            }

            rest = after;
        }

        var start = new ProcessStartInfo(host) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{host} did not start");
        using var client = new ExtensionClient(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
        var conversation = ConverseAsync(client, rest, identificationKey, CancellationToken.None);

        // A read of a pipe need not see a cancellation, so the host is ended instead, which ends the read.
        if (await Task.WhenAny(conversation, Task.Delay(within)).ConfigureAwait(false) != conversation)
        {
            await Console.Error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{host} did not answer within {within.TotalSeconds:0} seconds")).ConfigureAwait(false);
            process.Kill(entireProcessTree: true);
            return 124;
        }

        await conversation.ConfigureAwait(false);

        process.StandardInput.Close();
        await process.WaitForExitAsync().ConfigureAwait(false);

        return process.ExitCode;
    }

    private static async Task ConverseAsync(ExtensionClient client, string[] requests, string identificationKey, CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync((await client.ChangePublicKeysAsync(cancellationToken).ConfigureAwait(false)).ToJsonString()).ConfigureAwait(false);

        string? paired = null;

        foreach (var request in requests)
        {
            var (action, fields) = request switch
            {
                "associate" => ("associate", new JsonObject { ["key"] = client.PublicKey, ["idKey"] = identificationKey }),
                "test-associate" => ("test-associate", new JsonObject { ["id"] = paired ?? string.Empty, ["key"] = identificationKey }),
                _ when request.StartsWith("test-associate=", StringComparison.Ordinal) =>
                    ("test-associate", new JsonObject { ["id"] = request["test-associate=".Length..], ["key"] = identificationKey }),
                _ => (request, (JsonObject?)null),
            };

            var answer = await client.RequestAsync(action, fields, ExtensionClient.NewNonce(), triggerUnlock: false, cancellationToken).ConfigureAwait(false);

            // The extension checks a pairing it has just made under the id the host answered, as this does for a bare test-associate.
            if (action == "associate" && (string?)answer.Message?["id"] is { } id)
            {
                paired = id;
            }

            await Console.Out.WriteLineAsync((answer.Message ?? answer.Reply).ToJsonString()).ConfigureAwait(false);
        }
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
