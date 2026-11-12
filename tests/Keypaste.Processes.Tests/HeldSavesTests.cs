using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// A built keypaste verb that saves while the app or keypaste approve holds the vault (N.10, N.11): access and import are refused naming
/// the holder before any password, and every verb that edits items sends its edit to the holder, which asks and saves it (D-0464).
/// </summary>
/// <remarks>
/// The CLI's <c>HeldVaultRefusalTests</c> is the fast regression, against a claim the test process itself holds; this is the evidence
/// against the app and keypaste approve themselves.
/// </remarks>
public sealed class HeldSavesTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _released = "SENTINEL-HELD-SAVES-ONE-3f91";
    private const string _sourcePassword = "ci-held-saves-source-pw";

    private static readonly string[] _edits = ["add", "set", "set-field", "rotate", "field-rm", "env-set", "env-tag", "env-rm", "env-pull", "rm"];

    [Fact]
    public void Under_the_app_access_and_import_are_refused_and_each_edit_is_sent_to_it_asked_and_saved()
    {
        using var held = new Held();
        using var app = HeldApp.Hold(held.Workspace, "--approving-prompt");

        held.AssertRefusesAll(app.Process, "Make the change there, or run `keypaste lock` and try again.");
        var asked = 0;
        foreach (var verb in _edits)
        {
            held.AssertSendsNothing(verb, "app");
            var before = held.Digest();
            var (exit, error) = held.Run(verb, "app", Held.Values(verb, "app"));
            Assert.True(exit == 0, $"{verb} under the app exited {exit}: {error}");
            Assert.Contains("holds this vault; it will ask you to allow this change", error, StringComparison.Ordinal);
            app.WaitFor("^edit-asking command=keypaste ", ++asked);
            Assert.NotEqual(before, held.Digest());
        }

        held.AssertAnswersAnAgent("ci-held-app");
        Assert.Equal(0, held.Run(["lock"], []).Exit);
        app.WaitFor("^locked");
    }

    [Fact]
    public void Under_keypaste_approve_access_and_import_are_refused_and_each_edit_is_answered_at_its_terminal_the_denied_one_changing_nothing()
    {
        using var held = new Held();
        using var approve = RunningProcess.Start(Built.Keypaste, held.Workspace.Environment, "approve", "--vault", held.Workspace.Vault);
        approve.Send(Workspace.Master);
        approve.WaitForError("listening on");

        held.AssertRefusesAll(approve.Id, "Run `keypaste lock` and try again.");
        held.AssertOldStyleRefused("set", "agent");
        for (var i = 0; i < _edits.Length; i++)
        {
            var verb = _edits[i];
            var before = held.Digest();
            using var edit = held.Start(verb, "agent", Held.Values(verb, "agent"));
            approve.WaitForError("keypaste: a terminal asks to change this vault", i + 1);
            Assert.Contains(approve.Error, line => line.Contains("  command  keypaste ", StringComparison.Ordinal));
            var denied = i == _edits.Length - 1;
            approve.Send(denied ? "d" : "y");
            var exit = edit.WaitForExit();
            if (denied)
            {
                Assert.NotEqual(0, exit);
                Assert.Equal(before, held.Digest());
            }
            else
            {
                Assert.True(exit == 0, $"{verb} under keypaste approve exited {exit}: {string.Join('\n', edit.Error)}");
                Assert.NotEqual(before, held.Digest());
            }
        }

        using (var bridge = McpBridge.Start(held.Workspace, label: "ci-held-agent"))
        {
            var asked = bridge.Send("request_credential", Held.Request());
            approve.WaitForError("an agent is asking for a credential");
            approve.Send("o");
            held.AssertReleased(bridge.Reply(asked));
        }

        Assert.Equal(0, held.Run(["lock"], []).Exit);
        approve.WaitForError("keypaste approve has stopped");
    }

    [Fact]
    public void With_nothing_holding_the_vault_each_verb_saves_itself()
    {
        using var held = new Held();
        foreach (var verb in _edits.Append("import").Append("access"))
        {
            var before = held.Digest();
            var (exit, error) = held.Run(verb, "free", Held.Answers(verb, "free"));
            Assert.True(exit == 0, $"{verb} with nothing holding the vault exited {exit}: {error}");
            Assert.False(before.SequenceEqual(held.Digest()), $"{verb} with nothing holding the vault saved nothing");
        }
    }

    /// <summary>The vault the verbs edit, a second vault to import from, and how each verb is run.</summary>
    private sealed class Held : IDisposable
    {
        internal Held()
        {
            Workspace = new Workspace();
            Workspace.Add(_entry, _released);
            Source = Path.Combine(Path.GetDirectoryName(Workspace.Vault)!, "source.kdbx");
            Dotenv = Path.Combine(Path.GetDirectoryName(Workspace.Vault)!, "pull.env");
            Workspace.Keypaste("init", _sourcePassword, _sourcePassword, Source);
            Workspace.Keypaste("add", _sourcePassword, "old-held", "Imported/old", "--vault", Source);
        }

        internal Workspace Workspace { get; }

        private string Source { get; }

        private string Dotenv { get; }

        internal static string[] Values(string verb, string phase) => verb switch
        {
            "add" => [$"held-add-{phase}"],
            "set" => [$"gh-{phase}"],
            "set-field" => [$"field-{phase}"],
            _ => [],
        };

        internal static string[] Answers(string verb, string phase) => verb switch
        {
            "import" => [_sourcePassword, Workspace.Master],
            "access" => [Workspace.Master, $"{Workspace.Master}-next", $"{Workspace.Master}-next"],
            _ => [Workspace.Master, .. Values(verb, phase)],
        };

        internal static JsonObject Request() => new() { ["entry"] = _entry, ["field"] = "password", ["reason"] = "ci held-saves probe" };

        internal byte[] Digest() => SHA256.HashData(File.ReadAllBytes(Workspace.Vault));

        internal int Backups() =>
            Directory.Exists($"{Workspace.Vault}.backups") ? Directory.GetFiles($"{Workspace.Vault}.backups").Length : 0;

        /// <summary>Access and import, with standard input closed and then given every password they would need, are refused naming the holder and change nothing.</summary>
        internal void AssertRefusesAll(int holder, string next)
        {
            var before = Digest();
            var kept = Backups();
            foreach (var verb in new[] { "access", "import" })
            {
                var (closedExit, closed) = Run(verb, "refused", null);
                Assert.NotEqual(0, closedExit);
                Assert.Contains($"(process {holder})", closed, StringComparison.Ordinal);
                Assert.Contains(next, closed, StringComparison.Ordinal);
                Assert.DoesNotMatch("no master password given|no password given", closed);

                var (answeredExit, answered) = Run(verb, "refused", Answers(verb, "refused"));
                Assert.NotEqual(0, answeredExit);
                Assert.Contains($"(process {holder})", answered, StringComparison.Ordinal);
                Assert.Equal(before, Digest());
                Assert.Equal(kept, Backups());
            }
        }

        /// <summary>A verb given no value it needs, or a master password before its values, sends nothing.</summary>
        internal void AssertSendsNothing(string verb, string phase)
        {
            var before = Digest();
            if (Values(verb, phase).Length > 0)
            {
                Assert.NotEqual(0, Run(verb, phase, null).Exit);
                Assert.Equal(before, Digest());
            }

            AssertOldStyleRefused(verb, phase);
        }

        // A script written for a verb that opened the vault itself pipes the master password first.
        internal void AssertOldStyleRefused(string verb, string phase)
        {
            var before = Digest();
            var (exit, error) = Run(verb, phase, Answers(verb, phase));
            Assert.NotEqual(0, exit);
            Assert.Contains("stdin held more lines than this command reads", error, StringComparison.Ordinal);
            Assert.Equal(before, Digest());
        }

        internal void AssertAnswersAnAgent(string label)
        {
            using var bridge = McpBridge.Start(Workspace, label: label);
            AssertReleased(bridge.Call("request_credential", Request()));
        }

        internal void AssertReleased(System.Text.Json.JsonElement reply)
        {
            Assert.False(McpBridge.IsError(reply));
            Assert.Contains(_released, reply.ToString(), StringComparison.Ordinal);
            Assert.Equal("granted", Workspace.Lines(Workspace.OwnerLog)[^1].GetProperty("decision").GetString());
            Assert.DoesNotContain("vault-changed", Workspace.Text(Workspace.OwnerLog), StringComparison.Ordinal);
        }

        internal (int Exit, string Error) Run(string verb, string phase, string[]? input) => Run(Arguments(verb, phase), input);

        internal (int Exit, string Error) Run(string[] arguments, string[]? input)
        {
            using var run = RunningProcess.Start(Built.Keypaste, Workspace.Environment, [.. arguments, "--vault", Workspace.Vault]);
            foreach (var line in input ?? [])
            {
                run.Send(line);
            }

            run.CloseInput();
            return (run.WaitForExit(), string.Join('\n', run.Error));
        }

        internal RunningProcess Start(string verb, string phase, string[] input)
        {
            var run = RunningProcess.Start(Built.Keypaste, Workspace.Environment, [.. Arguments(verb, phase), "--vault", Workspace.Vault]);
            foreach (var line in input)
            {
                run.Send(line);
            }

            run.CloseInput();
            return run;
        }

        public void Dispose() => Workspace.Dispose();

        private string[] Arguments(string verb, string phase)
        {
            if (verb == "env-pull")
            {
                File.WriteAllText(Dotenv, $"PULLED_KEY={phase}\n");
            }

            return verb switch
            {
                "add" => ["add", $"ci/NEW_{phase}"],
                "set" => ["set", "Work/github"],
                "set-field" => ["set", "Work/github", "--field", "KEY"],
                "rotate" => ["rotate", "Work/github"],
                "field-rm" => ["field", "rm", "Work/github", "KEY"],
                "env-set" => ["env", "set", "ci", $"OTHER_KEY=held-{phase}"],
                "env-tag" => ["env", "tag", "ci", "Work/github", "--yes"],
                "env-rm" => ["env", "rm", "ci", "OTHER_KEY", "--yes"],
                "env-pull" => ["env", "pull", "ci", Dotenv, "--yes", "--keep"],
                "rm" => ["rm", "Work/github", "--yes"],
                "access" => ["access", "--password"],
                _ => ["import", Source],
            };
        }
    }
}
