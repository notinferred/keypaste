using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// A browser pairs with the vault's owner only while the person armed pairing (8.1d), across real processes: tests/Keypaste.FakeBrowser
/// starts the built keypaste as each browser starts KeePassXC-Browser's native messaging host and speaks the extension's protocol to it,
/// and tests/Keypaste.AppDriver holds the vault, its pairing confirmation and notice drawn and clicked.
/// </summary>
/// <remarks>Pairing's rules over a real pipe are Core.Tests' <c>SessionBrowsersTests</c>, and the relay's Browser.Tests' <c>RelayTests</c>.</remarks>
public sealed class BrowserHostTests
{
    private const string _chrome = "oboonakemofpalcgghocfoadofidjkkk";
    private const string _edge = "pdffhmdngciaglkoonimfcmckehcpafo";
    private const string _chromeBuild = "KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)";
    private const string _notice = "A browser asked to pair. To pair it, press Pair a browser.";

    [Fact]
    public void A_browser_pairs_only_while_armed_its_pairing_proves_only_its_own_build_and_key_and_Remove_ends_it()
    {
        using var workspace = new Workspace();
        using var app = HeldApp.Hold(workspace);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pairs = 0;
        var prompts = 0;

        // Unarmed, a request to pair is refused with error 6 before anybody is asked, noticed and recorded.
        var unarmed = Browser(workspace, "chrome", key, "get-databasehash", "associate");
        var hash = unarmed[0].GetProperty("hash").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal("6", unarmed[1].GetProperty("errorCode").GetString());
        app.WaitFor($"^pair-notice identity={Regex.Escape(_chromeBuild)} text={Regex.Escape(_notice)}");
        Assert.DoesNotContain(app.Driver.Output, line => line.StartsWith("pair-prompt", StringComparison.Ordinal));
        AssertLast(workspace, "pair", "denied", "not-armed", _chrome, session: app.Session);
        app.Send("pairings");
        app.WaitFor("^pairings none");

        // Armed, the confirmation names the build the browser named, Pair saves the pairing, and its check passes.
        app.Send("pair");
        app.WaitFor("^pairing armed", ++pairs);
        using (var pairing = StartBrowser(workspace, "chrome", key, "associate", "test-associate"))
        {
            var prompt = app.WaitFor("^pair-prompt identity=", ++prompts);
            Assert.StartsWith($"pair-prompt identity={_chromeBuild} name=", prompt, StringComparison.Ordinal);
            var name = prompt[(prompt.IndexOf(" name=", StringComparison.Ordinal) + " name=".Length)..];
            app.Send("allow");
            var answers = Answers(pairing);
            app.WaitFor("^pair-prompt withdrawn");
            foreach (var answer in answers)
            {
                Assert.Equal(("true", name, hash), (answer.GetProperty("success").GetString(), answer.GetProperty("id").GetString(), answer.GetProperty("hash").GetString()));
            }

            AssertLast(workspace, "pair", "granted", "prompt", _chrome, label: name);
            app.Send("pairings");
            app.WaitFor($"^pairing identity={Regex.Escape(_chromeBuild)} name={Regex.Escape(name)}");

            // The arming answered one confirmation, so the next request to pair is refused unasked.
            Assert.Equal("6", Assert.Single(Browser(workspace, "chrome", key, "associate")).GetProperty("errorCode").GetString());
            Assert.Single(app.Driver.Output, line => line.StartsWith("pair-prompt identity=", StringComparison.Ordinal));
            AssertLast(workspace, "pair", "denied", "not-armed", client: null);

            // A check from another build, or with another key, is refused with error 8 and recorded; the pairing's own still passes.
            Assert.Equal("8", Assert.Single(Browser(workspace, "edge", key, $"test-associate={name}")).GetProperty("errorCode").GetString());
            AssertLast(workspace, "check", "denied", "not-paired", _edge, label: name);
            Assert.Equal("8", Assert.Single(Browser(workspace, "chrome", otherKey, $"test-associate={name}")).GetProperty("errorCode").GetString());
            Assert.Equal(("true", name), Pair(Assert.Single(Browser(workspace, "chrome", key, $"test-associate={name}"))));

            // Deny pairs nothing.
            app.Send("pair");
            app.WaitFor("^pairing armed", ++pairs);
            using (var denied = StartBrowser(workspace, "firefox", key, "associate"))
            {
                Assert.Contains("identity=KeePassXC-Browser from Firefox Add-ons (keepassxc-browser@keepassxc.org)", app.WaitFor("^pair-prompt identity=", ++prompts), StringComparison.Ordinal);
                app.Send("deny");
                Assert.Equal("6", Assert.Single(Answers(denied)).GetProperty("errorCode").GetString());
            }

            AssertLast(workspace, "pair", "denied", "prompt", client: null);
            app.Send("pairings");
            app.WaitFor("^pairing identity=", 2);
            Assert.DoesNotContain(app.Driver.Output, line => line.StartsWith("pairing identity=KeePassXC-Browser from Firefox", StringComparison.Ordinal));

            // Remove ends the pairing, and its next check is refused.
            app.Send($"unpair {name}");
            app.WaitFor($"^unpaired {Regex.Escape(name)}");
            AssertLast(workspace, "remove", decision: null, "removed", client: null, label: name);
            Assert.Equal("8", Assert.Single(Browser(workspace, "chrome", key, $"test-associate={name}")).GetProperty("errorCode").GetString());
        }

        foreach (var identification in new[] { key, otherKey })
        {
            Assert.DoesNotContain(identification, Workspace.Text(workspace.OwnerLog), StringComparison.Ordinal);
            Assert.DoesNotContain(app.Driver.Output, line => line.Contains(identification, StringComparison.Ordinal));
        }
    }

    private static (string?, string?) Pair(JsonElement answer) => (answer.GetProperty("success").GetString(), answer.GetProperty("id").GetString());

    private static RunningProcess StartBrowser(Workspace workspace, string browser, string key, params string[] actions)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_VAULT"] = workspace.Vault;
        var run = RunningProcess.Start(Built.FakeBrowser, environment, [Built.Keypaste, browser, "--id-key", key, "--within", "60", .. actions]);
        run.CloseInput();
        return run;
    }

    private static List<JsonElement> Browser(Workspace workspace, string browser, string key, params string[] actions)
    {
        using var run = StartBrowser(workspace, browser, key, actions);
        return Answers(run);
    }

    // The fake browser prints the host's answer to each action as one line of JSON, after its key exchange's.
    private static List<JsonElement> Answers(RunningProcess run)
    {
        Assert.True(run.WaitForExit() == 0, $"the browser's host exited with an error:\n{string.Join('\n', run.Output.Concat(run.Error))}");
        return [.. run.Output.Skip(1).Select(line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    private static void AssertLast(Workspace workspace, string field, string? decision, string method, string? client, string? label = null, string? session = null)
    {
        var line = Workspace.Lines(workspace.OwnerLog)[^1];
        Assert.Equal(("browser", field, method), (line.GetProperty("tool").GetString(), line.GetProperty("args").GetProperty("field").GetString(), line.GetProperty("method").GetString()));
        if (decision is not null)
        {
            Assert.Equal(decision, line.GetProperty("decision").GetString());
        }

        if (client is not null)
        {
            Assert.Equal(client, line.GetProperty("client").GetProperty("name").GetString());
        }

        if (label is not null)
        {
            Assert.Equal(label, line.GetProperty("client").GetProperty("label").GetString());
        }

        if (session is not null)
        {
            Assert.Equal(session, line.GetProperty("session").GetString());
        }
    }
}
