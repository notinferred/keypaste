using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// Connecting an AI tool from the app (2.6a, G.2, N.3, G.3), across real processes: tests/Keypaste.AppDriver presses the rows of Agents' AI
/// tools in a home of the test's own, the command tool is tests/Keypaste.FakeMcpClient named <c>claude</c> first on PATH, the file tool is
/// Cursor, and the bridge each registers is the built keypaste, started as the tool starts it.
/// </summary>
/// <remarks>
/// Which tools a plan names and how a row reads are App.Tests' and Core.Tests' tests of the AI tools; here a registration is shown to
/// start a real bridge that reaches the app, and keypaste doctor and keypaste alone to read it from outside, changing nothing.
/// </remarks>
public sealed partial class ConnectClientTests
{
    private const string _secret = "SENTINEL-CONNECT-PASSWORD-7a41c2";
    private const string _otherSecret = "SENTINEL-CONNECT-OTHER-3d90b6";
    private const string _claude = "claude-code";

    [Fact]
    public void Claude_Code_is_registered_as_planned_its_bridge_reaches_the_apps_prompt_and_doctor_and_keypaste_alone_find_it_changing_nothing()
    {
        using var home = new ToolHome();
        using var app = home.Hold();
        var session = app.Session;

        Assert.True(ToolHome.SameFile(home.Keypaste("use").Trim(), home.Vault), "the first unlock did not choose the vault it unlocked");
        Assert.DoesNotContain("nothing holds", home.Keypaste("clients"), StringComparison.Ordinal);
        Assert.Equal("connect", ToolHome.State(app, _claude));

        var plan = ToolHome.Say(app, "^preview end", $"connect {_claude}").Preview();
        Assert.Equal("claude mcp remove --scope user keypaste", plan[0]);
        Assert.Equal(2, plan.Count);
        Assert.DoesNotContain(" --vault ", plan[1], StringComparison.Ordinal);
        Assert.StartsWith("claude mcp add --scope user --transport stdio keypaste -- ", plan[1], StringComparison.Ordinal);
        Assert.EndsWith(" --client-label claude-code --allow-run --expose **", plan[1], StringComparison.Ordinal);

        Assert.Equal("Cancelled. Nothing was changed.", ToolHome.Say(app, "^message ", "cancel").Message());
        Assert.False(File.Exists(home.ClaudeConfig), "a cancelled plan wrote the tool's configuration");
        Assert.Equal(plan, ToolHome.Say(app, "^preview end", $"connect {_claude}").Preview());
        Assert.StartsWith("Connected.", ToolHome.Say(app, "^message ", "confirm").Message(), StringComparison.Ordinal);
        Assert.Equal(plan[1][(plan[1].IndexOf("keypaste -- ", StringComparison.Ordinal) + "keypaste -- ".Length)..], home.Registered());
        ToolHome.AssertClean(home.ClaudeConfig, session);

        ToolHome.Say(app, "^check-entry n=4", "check");
        Assert.Contains(app.Driver.Output, line => line == "check-entry n=1 env/ci/DEPLOY_KEY");
        Assert.Contains($"client=keypaste-check label={_claude} entry=env/ci/DEPLOY_KEY field=password", ToolHome.Say(app, "^prompt client", "pick 1").Last("^prompt client"), StringComparison.Ordinal);
        Assert.StartsWith("Connected: you approved env/ci/DEPLOY_KEY", ToolHome.Say(app, "^check end", "once").Check(), StringComparison.Ordinal);
        home.AssertLastAudit("granted", "prompt", session, _claude, "keypaste-check");
        Assert.Equal("connected", ToolHome.State(app, _claude));

        var logged = Workspace.Count(home.Audit);
        var before = home.Tree();
        var doctor = home.Doctor();
        Assert.Equal(0, doctor.Exit);
        foreach (var subject in new[] { "versions", "vault", "owner", _claude })
        {
            Assert.True(Says(doctor.Json, subject, "ok"), $"keypaste doctor does not find {subject} working: {doctor.Json}");
        }

        Assert.True(Says(doctor.Json, "cursor", "note"), $"keypaste doctor does not note cursor as not connected: {doctor.Json}");
        Assert.Equal(logged, Workspace.Count(home.Audit));
        Assert.Equal(before, home.Tree());

        if (!OperatingSystem.IsWindows())
        {
            var status = home.Alone();
            Assert.Contains("exit=0", status);
            Assert.Contains($"keypaste {home.Keypaste("--version").Trim()}", status);
            Assert.Contains(status, line => Regex.IsMatch(line, @"^  vault     .*vault\.kdbx"));
            Assert.Contains(status, line => line.Contains("chosen in the app", StringComparison.Ordinal));
            Assert.Contains("  held by   the keypaste app, unlocked", status);
            Assert.Contains(status, line => line.StartsWith("  AI tools  Claude Code connected", StringComparison.Ordinal));
            Assert.Contains(status, line => Regex.IsMatch(line, "^(›|>) `keypaste --help` lists every command$"));
            Assert.Equal(logged, Workspace.Count(home.Audit));
            Assert.Equal(before, home.Tree());
        }

        // With another vault chosen, Connect again pins this one, and that bridge still reaches the app.
        var other = Path.Combine(home.Root, "other.kdbx");
        home.Keypaste("init", Workspace.Master, Workspace.Master, other);
        home.Keypaste("use", other);
        var pinned = ToolHome.Say(app, "^preview end", $"connect {_claude}").Preview();
        Assert.Matches(@" --vault .*vault\.kdbx --client-label claude-code --allow-run --expose \*\*$", pinned[1]);
        Assert.StartsWith("Connected.", ToolHome.Say(app, "^message ", "confirm").Message(), StringComparison.Ordinal);
        Assert.Equal(pinned[1][(pinned[1].IndexOf("keypaste -- ", StringComparison.Ordinal) + "keypaste -- ".Length)..], home.Registered());
        ToolHome.AssertClean(home.ClaudeConfig, session);
        ToolHome.Say(app, "^check-entry n=4", "check");
        Assert.Contains($"label={_claude} entry=env/ci/DEPLOY_KEY field=password", ToolHome.Say(app, "^prompt client", "pick 1").Last("^prompt client"), StringComparison.Ordinal);
        Assert.StartsWith("Connected: the request for env/ci/DEPLOY_KEY reached you and was refused.", ToolHome.Say(app, "^check end", "deny").Check(), StringComparison.Ordinal);
        home.AssertLastAudit("denied", "prompt", session, _claude, name: null);

        Assert.Equal(["claude mcp remove --scope user keypaste"], ToolHome.Say(app, "^preview end", $"connect-remove {_claude}").Preview());
        Assert.StartsWith("Disconnected.", ToolHome.Say(app, "^message ", "confirm").Message(), StringComparison.Ordinal);
        Assert.DoesNotContain("keypaste", home.Run(Built.FakeClaude, "mcp", "list"), StringComparison.Ordinal);
        ToolHome.AssertClean(home.ClaudeConfig, session);
        Assert.Equal("connect", ToolHome.State(app, _claude));
        ToolHome.AssertNoSecretIn(app);
    }

    [Fact]
    public void Cursors_file_changes_only_in_keypastes_entry_keeps_its_original_its_bridge_reaches_the_app_and_Disconnect_gives_its_bytes_back()
    {
        using var home = new ToolHome();
        using var app = home.Hold();
        File.WriteAllText(home.CursorConfig, """
            {
              // the person's own note, which no edit may lose
              "mcpServers": {
                "other": {
                  "command": "other-server",
                  "args": ["--port", "7"]
                }
              },
              "theme": "dark"
            }

            """);
        var seed = File.ReadAllBytes(home.CursorConfig);

        var edit = Assert.Single(ToolHome.Say(app, "^preview end", "connect cursor").Preview());
        var planned = EditRegex().Match(edit);
        Assert.True(planned.Success, $"the Cursor plan is not the edit of keypaste's entry naming its backup: {edit}");
        var entry = JsonDocument.Parse(planned.Groups[1].Value).RootElement;
        var args = entry.GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToList();
        Assert.Equal(["--client-label", "cursor", "--allow-run", "--expose", "**"], args[args.IndexOf("--client-label")..]);

        Assert.Equal("Cancelled. Nothing was changed.", ToolHome.Say(app, "^message ", "cancel").Message());
        Assert.Equal(seed, File.ReadAllBytes(home.CursorConfig));
        Assert.False(File.Exists($"{home.CursorConfig}.keypaste-backup"), "a cancelled plan kept a backup");

        Assert.Equal(edit, Assert.Single(ToolHome.Say(app, "^preview end", "connect cursor").Preview()));
        Assert.Matches("^Connected. Cursor starts keypaste mcp for this vault;.*Restart Cursor to load it.$", ToolHome.Say(app, "^message ", "confirm").Message());
        Assert.Equal(seed, File.ReadAllBytes($"{home.CursorConfig}.keypaste-backup"));
        Assert.Equal(System.Text.Encoding.UTF8.GetString(seed), WithoutKeypaste(File.ReadAllText(home.CursorConfig)));
        var written = JsonDocument.Parse(string.Join('\n', File.ReadAllLines(home.CursorConfig).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))));
        Assert.True(JsonElement.DeepEquals(entry, written.RootElement.GetProperty("mcpServers").GetProperty("keypaste")), "Cursor's file does not hold the entry the plan showed");
        ToolHome.AssertClean(home.CursorConfig, app.Session);
        Assert.Equal("connected", ToolHome.State(app, "cursor"));

        ToolHome.Say(app, "^check-entry n=4", "check");
        Assert.Contains(app.Driver.Output, line => line == "check-entry n=3 env/alpha/.env");
        Assert.Contains(app.Driver.Output, line => line == "check-entry n=4 env/beta/.env");
        Assert.DoesNotContain(app.Driver.Output, line => line.StartsWith("check-entry n=5", StringComparison.Ordinal));
        Assert.Contains("client=keypaste-check label=cursor entry=env/beta/.env field=BETA_KEY", ToolHome.Say(app, "^prompt client", "pick 4").Last("^prompt client"), StringComparison.Ordinal);
        Assert.StartsWith("Connected: you approved env/beta/.env", ToolHome.Say(app, "^check end", "once").Check(), StringComparison.Ordinal);
        home.AssertLastAudit("granted", "prompt", app.Session, "cursor", name: null);

        Assert.Matches("^edit .*mcp.json: remove mcpServers.keypaste$", Assert.Single(ToolHome.Say(app, "^preview end", "connect-remove cursor").Preview()));
        Assert.StartsWith("Disconnected.", ToolHome.Say(app, "^message ", "confirm").Message(), StringComparison.Ordinal);
        Assert.Equal(seed, File.ReadAllBytes(home.CursorConfig));
        Assert.Equal(seed, File.ReadAllBytes($"{home.CursorConfig}.keypaste-backup"));
        Assert.Equal("connect", ToolHome.State(app, "cursor"));
        ToolHome.AssertNoSecretIn(app);
    }

    [Fact]
    public void Once_the_app_has_shut_down_doctor_notes_that_nothing_holds_the_vault_and_still_exits_0()
    {
        using var home = new ToolHome();
        using (var app = home.Hold())
        {
            app.Quit();
            app.WaitFor("^shut down");
        }

        var doctor = home.Doctor("--vault", home.Vault);
        Assert.Equal(0, doctor.Exit);
        Assert.True(Says(doctor.Json, "owner", "note"), $"keypaste doctor does not note that nothing holds the vault: {doctor.Json}");
    }

    private static bool Says(string json, string subject, string status)
    {
        var rows = JsonDocument.Parse(json).RootElement.EnumerateArray().Where(row => row.GetProperty("subject").GetString() == subject).ToList();
        return rows.Count > 0 && rows.All(row => row.GetProperty("status").GetString() == status);
    }

    // Cursor's file without keypaste's entry, which a connection adds as one block of mcpServers.
    private static string WithoutKeypaste(string text) => KeypasteEntryRegex().Replace(text, "");

    [GeneratedRegex(@"^edit .*mcp\.json: set mcpServers\.keypaste (\{.*\}); keeps the original as .*mcp\.json\.keypaste-backup$")]
    private static partial Regex EditRegex();

    [GeneratedRegex(@"(?m)^    ""keypaste"": \{\r?\n(.*\r?\n)*?    \},\r?\n")]
    private static partial Regex KeypasteEntryRegex();

    /// <summary>A person's home of the test's own, so no real tool's configuration is ever read or written, with the stand-in claude first on PATH.</summary>
    private sealed class ToolHome : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-connect-").FullName;

        internal ToolHome()
        {
            User = Path.Combine(_directory, "user");
            Directory.CreateDirectory(Path.Combine(User, ".keypaste"));
            Directory.CreateDirectory(Path.Combine(User, ".cursor"));
            Vault = Path.Combine(_directory, "vault.kdbx");
            Keypaste("init", Workspace.Master, Workspace.Master, Vault);
            Keypaste("add", Workspace.Master, _secret, "env/ci/DEPLOY_KEY", "--vault", Vault);
            Keypaste("add", Workspace.Master, _otherSecret, "env/other/TOKEN", "--vault", Vault);
            foreach (var project in new[] { "alpha", "beta" })
            {
                using var set = RunningProcess.Start(Built.Keypaste, Environment(), "env", "set", project, $"{project.ToUpperInvariant()}_KEY={_otherSecret}", "--vault", Vault);
                set.Send(Workspace.Master);
                set.CloseInput();
                Assert.Equal(0, set.WaitForExit());
            }
        }

        internal string Root => _directory;

        internal string User { get; }

        internal string Vault { get; }

        internal string Audit => Path.Combine(User, ".keypaste", "audit.jsonl");

        internal string ClaudeConfig => Path.Combine(User, ".claude.json");

        internal string CursorConfig => Path.Combine(User, ".cursor", "mcp.json");

        internal Dictionary<string, string> Environment() => new()
        {
            ["HOME"] = User,
            ["USERPROFILE"] = User,
            ["KEYPASTE_HOME"] = Path.Combine(User, ".keypaste"),
            ["KEYPASTE_FAKE_CLIENT_CONFIG"] = ClaudeConfig,
            ["KEYPASTE_VAULT"] = "",
            ["PATH"] = string.Join(Path.PathSeparator, Path.GetDirectoryName(Built.FakeClaude), Path.GetDirectoryName(Built.Keypaste), System.Environment.GetEnvironmentVariable("PATH")),
        };

        internal HeldApp Hold() => HeldApp.Hold(Vault, Environment());

        internal string Keypaste(params string[] arguments) => Run(Built.Keypaste, arguments);

        internal void Keypaste(string verb, string first, string second, params string[] arguments)
        {
            using var run = RunningProcess.Start(Built.Keypaste, Environment(), [verb, .. arguments]);
            run.Send(first);
            run.Send(second);
            run.CloseInput();
            Assert.True(run.WaitForExit() == 0, $"keypaste {verb} failed: {string.Join('\n', run.Error)}");
        }

        internal string Run(string program, params string[] arguments)
        {
            using var run = RunningProcess.Start(program, Environment(), arguments);
            run.CloseInput();
            Assert.True(run.WaitForExit() == 0, $"{program} {string.Join(' ', arguments)} failed: {string.Join('\n', run.Error)}");
            return string.Join('\n', run.Output.Concat(run.Error)).Trim('﻿');
        }

        internal (int Exit, string Json) Doctor(params string[] options)
        {
            using var run = RunningProcess.Start(Built.Keypaste, Environment(), ["doctor", "--json", .. options]);
            run.CloseInput();
            var exit = run.WaitForExit();
            return (exit, string.Join('\n', run.Output));
        }

        /// <summary>keypaste with no arguments on a pseudo-terminal from <c>script</c>, with no colour, and how it exited.</summary>
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        internal IReadOnlyList<string> Alone()
        {
            var bare = Path.Combine(_directory, "bare.sh");
            File.WriteAllText(bare, "#!/bin/sh\n\"$1\"\necho \"exit=$?\"\n");
            File.SetUnixFileMode(bare, (UnixFileMode)0b111_101_101);
            var environment = Environment();
            environment["NO_COLOR"] = "1";
            environment["TERM"] = "dumb";
            using var run = OperatingSystem.IsLinux()
                ? RunningProcess.Start("script", environment, "-qec", $"{bare} {Built.Keypaste}", "/dev/null")
                : RunningProcess.Start("script", environment, "-q", "/dev/null", bare, Built.Keypaste);
            run.CloseInput();
            Assert.Equal(0, run.WaitForExit());
            return [.. run.Output.Select(line => line.Replace("\b", "", StringComparison.Ordinal).TrimStart('^', 'D'))];
        }

        internal string Registered()
        {
            var server = JsonDocument.Parse(File.ReadAllText(ClaudeConfig)).RootElement.GetProperty("mcpServers").GetProperty("keypaste");
            return string.Join(' ', [server.GetProperty("command").GetString()!, .. server.GetProperty("args").EnumerateArray().Select(a => a.GetString()!)]);
        }

        internal static string State(HeldApp app, string tool)
        {
            var before = app.Driver.Output.Count(line => line.StartsWith("tools end", StringComparison.Ordinal));
            app.Send("tools");
            app.WaitFor("^tools end", before + 1);
            var block = Block(app, "tools end", before + 1);
            var row = block.Last(line => line.StartsWith($"tool id={tool} ", StringComparison.Ordinal));
            return Regex.Match(row, "state=([^ ]*)").Groups[1].Value;
        }

        internal static Said Say(HeldApp app, string ending, string command)
        {
            var before = app.Driver.Output.Count(line => Regex.IsMatch(line, ending));
            app.Send(command);
            app.WaitFor(ending, before + 1);
            return new Said(app);
        }

        internal static void AssertClean(string file, string session)
        {
            if (!File.Exists(file))
            {
                return;
            }

            var text = File.ReadAllText(file);
            foreach (var secret in new[] { Workspace.Master, _secret, _otherSecret, session })
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("keyfile", text, StringComparison.OrdinalIgnoreCase);
        }

        internal void AssertLastAudit(string decision, string method, string session, string label, string? name)
        {
            var line = Workspace.Lines(Audit)[^1];
            Assert.Equal(
                ("request_credential", decision, method, session, label),
                (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString(),
                 line.GetProperty("session").GetString(), line.GetProperty("client").GetProperty("label").GetString()));
            if (name is not null)
            {
                Assert.Equal(name, line.GetProperty("client").GetProperty("name").GetString());
            }
        }

        internal static void AssertNoSecretIn(HeldApp app) =>
            Assert.DoesNotContain(app.Driver.Output, line => line.Contains(_secret, StringComparison.Ordinal) || line.Contains(_otherSecret, StringComparison.Ordinal));

        internal static bool SameFile(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || (File.Exists(a) && File.Exists(b) && new FileInfo(a).FullName == new FileInfo(b).FullName)
            || (OperatingSystem.IsMacOS() && Path.GetFullPath(a).Replace("/private", "", StringComparison.Ordinal) == Path.GetFullPath(b).Replace("/private", "", StringComparison.Ordinal));

        /// <summary>Every path under the home, each file with its bytes, to show a read changed nothing.</summary>
        internal string Tree() =>
            string.Join('\n', Directory.EnumerateFileSystemEntries(User, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path => File.Exists(path) ? $"{path} {ReadShared(path)}" : path));

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A killed process's files are freed on Windows only once it has gone; the temporary directory is left then.
            }
        }

        // A file another process holds locked, such as a session's lock, is named as held rather than read.
        private static string ReadShared(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return Convert.ToHexString(copy.ToArray());
            }
            catch (IOException)
            {
                return "held";
            }
        }

        private static List<string> Block(HeldApp app, string end, int number)
        {
            var output = app.Driver.Output;
            var ends = output.Select((line, index) => (line, index)).Where(pair => pair.line.StartsWith(end, StringComparison.Ordinal)).Select(pair => pair.index).ToList();
            var start = number > 1 ? ends[number - 2] + 1 : 0;
            return [.. output.Skip(start).Take(ends[number - 1] - start)];
        }
    }

    /// <summary>What the app printed by the time a command's answer came.</summary>
    private sealed class Said(HeldApp app)
    {
        internal List<string> Preview()
        {
            var output = app.Driver.Output;
            var end = output.Select((line, index) => (line, index)).Last(pair => pair.line.StartsWith("preview end", StringComparison.Ordinal)).index;
            var start = end;
            while (start > 0 && output[start - 1].StartsWith("preview ", StringComparison.Ordinal) && !output[start - 1].StartsWith("preview end", StringComparison.Ordinal))
            {
                start--;
            }

            return [.. output.Skip(start).Take(end - start).Select(line => line["preview ".Length..])];
        }

        internal string Message() => app.Driver.Output.Last(line => line.StartsWith("message ", StringComparison.Ordinal))["message ".Length..];

        internal string Check() => app.Driver.Output.Last(line => line.StartsWith("check ", StringComparison.Ordinal) && !line.StartsWith("check end", StringComparison.Ordinal) && !line.StartsWith("check-entry", StringComparison.Ordinal))["check ".Length..];

        internal string Last(string pattern) => app.Driver.Output.Last(line => Regex.IsMatch(line, pattern));
    }
}
