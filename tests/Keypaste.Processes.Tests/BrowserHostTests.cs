using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// A browser pairs with the vault's owner only while the person armed pairing (8.1d), across real processes: tests/Keypaste.FakeBrowser
/// starts the built keypaste as each browser starts KeePassXC-Browser's native messaging host and speaks the extension's protocol to it,
/// and tests/Keypaste.AppDriver holds the vault, its pairing confirmation and notice drawn and clicked.
/// </summary>
/// <remarks>
/// Pairing's rules over a real pipe are Core.Tests' <c>SessionBrowsersTests</c>, and the relay's Browser.Tests' <c>RelayTests</c>.
/// Settings' Turn on and Turn off (8.1e) are driven the same way, and the fake browser's <c>find</c> finds the host where each
/// browser looks under a test's own folders and registry key, and starts it as that browser does.
/// </remarks>
public sealed class BrowserHostTests
{
    private const string _chrome = "fipabobollckgdobkjfkkchimlphcicg";
    private const string _edge = "iobkkhmaklkkeiadigbohfaplldmaogd";
    private const string _chromeBuild = "keypaste's extension from the Chrome Web Store (fipabobollckgdobkjfkkchimlphcicg)";
    private const string _notice = "A browser asked to pair. To pair it, press Pair a browser.";
    private const int _notFound = 20;
    private static readonly string[] _browsers = ["chrome", "chromium", "edge", "brave", "firefox"];

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
                Assert.Contains("identity=keypaste's extension from Firefox Add-ons (browser@keypaste.invalid)", app.WaitFor("^pair-prompt identity=", ++prompts), StringComparison.Ordinal);
                app.Send("deny");
                Assert.Equal("6", Assert.Single(Answers(denied)).GetProperty("errorCode").GetString());
            }

            AssertLast(workspace, "pair", "denied", "prompt", client: null);
            app.Send("pairings");
            app.WaitFor("^pairing identity=", 2);
            Assert.DoesNotContain(app.Driver.Output, line => line.StartsWith("pairing identity=keypaste's extension from Firefox", StringComparison.Ordinal));

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

    [Fact]
    public void Turn_on_has_each_browser_find_and_start_this_keypaste_and_Turn_off_removes_keypastes_own_and_leaves_another_programs()
    {
        using var workspace = new Workspace();
        using var roots = new HostRoots();
        using var app = HeldApp.Hold(workspace.Vault, roots.DriverEnvironment(workspace));
        var keypaste = Built.Keypaste;
        var statuses = 0;

        var status = Status(app, ref statuses);
        foreach (var browser in _browsers)
        {
            Assert.Equal($"on=false other=false answer=Not set up. Turn on so {Name(browser)} can start keypaste for its extension. extension={Name(browser)} isn't installed.", status[browser]);
        }

        Assert.Equal(OperatingSystem.IsWindows(), app.Driver.Output.Contains("browser-shared Chrome and Brave read one setting, so they turn on and off together."));

        // The extension is found in a profile of the browser's own folder; a profile without it is a browser it isn't installed in.
        Directory.CreateDirectory(Path.Combine(roots.ProfileFolder("chrome"), "Default", "Extensions", _chrome));
        Directory.CreateDirectory(Path.Combine(roots.ProfileFolder("edge"), "Default"));
        status = Status(app, ref statuses);
        Assert.EndsWith(" extension=keypaste's extension is installed in Chrome.", status["chrome"], StringComparison.Ordinal);
        Assert.EndsWith(" extension=keypaste's extension isn't installed in Edge. Get it from Microsoft Edge Add-ons.", status["edge"], StringComparison.Ordinal);

        // Each browser finds the manifest Turn on wrote, starts the built keypaste through it and is answered with the vault's hash.
        string? hash = null;
        var manifests = new Dictionary<string, string>();
        foreach (var browser in _browsers)
        {
            var sharing = !OperatingSystem.IsWindows() ? string.Empty
                : browser == "brave" ? " Chrome reads the same setting, so it is on too."
                : browser == "chrome" ? " Brave reads the same setting, so it is on too."
                : string.Empty;
            Assert.Equal($"browser-on {browser}: {Name(browser)} now starts keypaste at {keypaste}.{sharing}", app.Act($"browser-on {browser}"));
            Assert.StartsWith($"on=true other=false answer={Name(browser)} starts keypaste at {keypaste}. ", Status(app, ref statuses)[browser], StringComparison.Ordinal);

            using (var found = Find(workspace, roots, browser, "get-databasehash"))
            {
                var answered = Assert.Single(Answers(found)).GetProperty("hash").GetString()!;
                Assert.Matches("^[0-9a-f]{64}$", answered);
                Assert.Equal(hash ??= answered, answered);
                manifests[browser] = Manifest(found);
            }

            Assert.Equal($"browser-off {browser}: {Name(browser)} no longer starts keypaste.{sharing.Replace(" on ", " off ", StringComparison.Ordinal)}", app.Act($"browser-off {browser}"));
            Assert.StartsWith("on=false other=false answer=Not set up.", Status(app, ref statuses)[browser], StringComparison.Ordinal);
            using var gone = Find(workspace, roots, browser, "get-databasehash");
            Assert.Equal(_notFound, gone.WaitForExit());
            Assert.Empty(gone.Output);
        }

        // Another program's manifest, and on Windows its key, is the person's: Turn on asks first, and nothing is written until it is answered.
        var other = Path.Combine(roots.Directory, "other", OperatingSystem.IsWindows() ? "other-host.exe" : "other-host");
        var otherManifest = OperatingSystem.IsWindows() ? Path.Combine(roots.Directory, "other", "com.keypaste.browser.json") : manifests["chromium"];
        var otherBytes = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["name"] = "com.keypaste.browser",
            ["description"] = "another program",
            ["path"] = other,
            ["type"] = "stdio",
            ["allowed_origins"] = new JsonArray($"chrome-extension://{_chrome}/"),
        }.ToJsonString());
        Directory.CreateDirectory(Path.GetDirectoryName(otherManifest)!);
        File.WriteAllBytes(otherManifest, otherBytes);
        roots.SetChromiumKey(otherManifest);

        Assert.Equal($"on=false other=true answer=Chromium starts another program for keypaste's extension: {other}. extension=Chromium isn't installed.", Status(app, ref statuses)["chromium"]);
        Assert.StartsWith("browser-off chromium not offered:", app.Act("browser-off chromium"), StringComparison.Ordinal);
        Assert.Equal($"browser-on chromium confirm: Turning on replaces {other} for Chromium, and keypaste can't put it back.", app.Act("browser-on chromium"));
        Assert.Equal(otherBytes, File.ReadAllBytes(otherManifest));
        Assert.Equal(OperatingSystem.IsWindows() ? otherManifest : null, roots.ReadChromiumKey());

        // A manifest of keypaste's naming a keypaste that no longer exists reads as missing, and Turn on points it at this one.
        Assert.StartsWith("browser-on edge: ", app.Act("browser-on edge"), StringComparison.Ordinal);
        var missing = Path.Combine(roots.Directory, "gone", Path.GetFileName(keypaste));
        File.WriteAllText(manifests["edge"], File.ReadAllText(manifests["edge"]).Replace(JsonSerializer.Serialize(keypaste), JsonSerializer.Serialize(missing), StringComparison.Ordinal));
        Assert.StartsWith($"on=false other=false answer=Edge looks for keypaste at {missing}, which no longer exists. Turn on to point it at this keypaste. ", Status(app, ref statuses)["edge"], StringComparison.Ordinal);
        Assert.Equal($"browser-on edge: Edge now starts keypaste at {keypaste}.", app.Act("browser-on edge"));
        using (var found = Find(workspace, roots, "edge", "get-databasehash"))
        {
            Assert.Equal(hash, Assert.Single(Answers(found)).GetProperty("hash").GetString());
        }
    }

    private static string Name(string browser) => char.ToUpperInvariant(browser[0]) + browser[1..];

    // The driver prints the five browsers in order, Firefox last, after any line on a shared setting.
    private static Dictionary<string, string> Status(HeldApp app, ref int statuses)
    {
        app.Send("browser-status");
        app.WaitFor("^browser firefox ", ++statuses);
        return app.Driver.Output
            .Where(line => line.StartsWith("browser ", StringComparison.Ordinal))
            .TakeLast(_browsers.Length)
            .ToDictionary(line => line.Split(' ')[1], line => line[(line.IndexOf(' ', "browser ".Length) + 1)..]);
    }

    private static RunningProcess Find(Workspace workspace, HostRoots roots, string browser, params string[] actions)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_VAULT"] = workspace.Vault;
        var run = RunningProcess.Start(Built.FakeBrowser, environment, ["find", browser, .. roots.FindArguments, "--within", "60", .. actions]);
        run.CloseInput();
        return run;
    }

    // The fake browser names on standard error the manifest it found where the browser looks.
    private static string Manifest(RunningProcess run) => Assert.Single(run.Error, line => line.StartsWith("found ", StringComparison.Ordinal))["found ".Length..];

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

/// <summary>A user's folders and, on Windows, a key under <c>HKCU\Software\keypaste-tests</c>, for the browsers to be set up in, deleted with the test.</summary>
internal sealed class HostRoots : IDisposable
{
    private const string _tests = @"Software\keypaste-tests";

    internal HostRoots()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("keypaste-browsers-").FullName;
        RegistryRoot = $@"{_tests}\{Guid.NewGuid():N}";
    }

    internal string Directory { get; }

    internal string RegistryRoot { get; }

    private string Home => Path.Combine(Directory, "home");

    private string ConfigHome => Path.Combine(Directory, "config");

    private string AppData => Path.Combine(Directory, "appdata");

    private string LocalAppData => Path.Combine(Directory, "localappdata");

    internal string[] FindArguments =>
        ["--home", Home, "--config-home", ConfigHome, "--app-data", AppData, "--local-app-data", LocalAppData, "--registry-root", RegistryRoot];

    /// <summary>The held app's environment, its browsers set up for the built keypaste under these folders.</summary>
    internal Dictionary<string, string> DriverEnvironment(Workspace workspace)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_BROWSER_PROGRAM"] = Built.Keypaste;
        environment["KEYPASTE_DRIVER_BROWSER_HOME"] = Home;
        environment["KEYPASTE_DRIVER_BROWSER_USERPROFILE"] = Home;
        environment["KEYPASTE_DRIVER_BROWSER_XDG_CONFIG_HOME"] = ConfigHome;
        environment["KEYPASTE_DRIVER_BROWSER_APPDATA"] = AppData;
        environment["KEYPASTE_DRIVER_BROWSER_LOCALAPPDATA"] = LocalAppData;
        environment["KEYPASTE_DRIVER_BROWSER_REGISTRY_ROOT"] = RegistryRoot;
        return environment;
    }

    /// <summary>The folder a Chromium-family browser keeps its profiles in, written out here apart from keypaste's own list.</summary>
    internal string ProfileFolder(string browser) => (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), browser) switch
    {
        (true, _, "edge") => Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data"),
        (true, _, _) => Path.Combine(LocalAppData, "Google", "Chrome", "User Data"),
        (_, true, "edge") => Path.Combine(Home, "Library", "Application Support", "Microsoft Edge"),
        (_, true, _) => Path.Combine(Home, "Library", "Application Support", "Google", "Chrome"),
        (_, _, "edge") => Path.Combine(ConfigHome, "microsoft-edge"),
        _ => Path.Combine(ConfigHome, "google-chrome"),
    };

    /// <summary>On Windows, has the key Chromium reads name <paramref name="manifest"/>, as another program does.</summary>
    internal void SetChromiumKey(string manifest)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(ChromiumKey);
            key.SetValue(null, manifest);
        }
    }

    /// <summary>On Windows, the manifest the key Chromium reads names; null elsewhere.</summary>
    internal string? ReadChromiumKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(ChromiumKey);
        return key?.GetValue(null) as string;
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryRoot, throwOnMissingSubKey: false);
            try
            {
                Registry.CurrentUser.DeleteSubKey(_tests, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test's key is still under it.
            }
        }

        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A killed host's files are freed on Windows only once it has gone; the temporary directory is left then.
        }
    }

    private string ChromiumKey => $@"{RegistryRoot}\Chromium\NativeMessagingHosts\com.keypaste.browser";
}
