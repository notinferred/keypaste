using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Internal;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste doctor</c> end to end: what it prints and exits for each finding, that only a registered keypaste and
/// PATH's are started, and only with <c>--version</c> or <c>mcp --help</c>, and that it changes nothing and asks nothing
/// (D-0467).
/// </summary>
/// <remarks>
/// Each case builds its machine under the harness's directory: this keypaste on a PATH of its own, a vault that is only
/// the twelve bytes doctor reads of one, and the AI tools' files under the harness's home. <see cref="FakeProcessRunner"/>
/// answers every program, and the owners are real listeners on pipes of their own.
/// </remarks>
public sealed class DoctorVerbTests : IDisposable
{
    private const string _signIn = "SENTINEL-CLAUDE-SIGN-IN-3f1e";

    private const string _restart = "quit the keypaste app or stop `keypaste approve`, and start it again";

    private static readonly TimeSpan _ownerWait = TimeSpan.FromSeconds(30);

    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private string Bin => Path.Combine(_harness.Directory, "bin");

    private string This => _harness.ProcessPath!;

    [Fact]
    public void Help_says_it_only_reads_and_that_7_means_something_needs_fixing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("doctor", "--help"));

        Assert.StartsWith("usage: keypaste doctor [--vault <path>] [--json]", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("exits 0 when nothing needs fixing and 7 when something does.", _harness.Out, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessRunner.Calls);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("--keyfile", "k.key")]
    [InlineData("--approver", "pinned")]
    public void An_operand_or_an_option_doctor_does_not_take_is_a_usage_error(params string[] extra)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(["doctor", .. extra]));

        Assert.Empty(_harness.Out);
        Assert.StartsWith("keypaste doctor: ", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Each_fix_is_a_row_with_what_to_do_beneath_it_and_any_fix_exits_7()
    {
        _harness.AssertExit(CliApp.ExitNeedsFixing, _harness.Run("doctor"));

        Assert.Equal(
            Lines(
            [
                $"  ✓ versions         {CoreInfo.Version} here",
                .. Argon2Rows(),
                "  ✗ vault            no vault is chosen",
                "                     › open your vault in the keypaste app, or run `keypaste use <path>`",
                "  · AI tools         no AI tool keypaste connects is installed here",
                "  · owner            no vault to ask about",
                "  ✓ settings         no settings files yet",
                "  ✗ PATH             a terminal finds no keypaste on PATH",
                "                     › put the folder holding keypaste on PATH",
                "",
                "  2 to fix",
            ]),
            _harness.Out);
        Assert.Empty(_harness.Err);
        Assert.Empty(_harness.ProcessRunner.Calls);
    }

    [Fact]
    public void A_working_setup_shows_each_path_beneath_its_row_and_a_note_alone_exits_0()
    {
        Healthy();
        Register(McpConfigLocation.Cursor, This, "mcp");

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("doctor"));

        Assert.Equal(
            Lines(
            [
                $"  ✓ versions         {CoreInfo.Version} here and in each AI tool",
                .. Argon2Rows(),
                "  ✓ vault            the chosen vault is a KeePass database",
                $"                     {_harness.VaultPath}",
                $"  ✓ cursor           starts keypaste {CoreInfo.Version} from a place upgrades keep",
                $"                     {This}",
                "  · owner            nothing holds the vault unlocked; agents are refused",
                "                     › unlock it in the keypaste app, or run `keypaste approve`",
                "  ✓ settings         app.toml parses",
                "  ✓ PATH             a terminal runs this keypaste",
                $"                     {This}",
                "",
                "  nothing to fix",
            ]),
            _harness.Out);
        Assert.Equal([$"{This} mcp --help"], _harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Json_is_one_line_of_one_object_per_finding_with_the_same_six_keys_and_nulls_kept()
    {
        Healthy();

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Single(_harness.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using (var json = JsonDocument.Parse(_harness.Out))
        {
            Assert.All(
                json.RootElement.EnumerateArray(),
                item => Assert.Equal(["area", "subject", "status", "says", "where", "fix"], item.EnumerateObject().Select(property => property.Name)));
        }

        Assert.Equal(["versions", "argon2", "vault", "tool", "owner", "settings", "path"], found.Select(finding => finding.Area));
        Assert.Equal(new Finding("vault", "vault", "ok", "the chosen vault is a KeePass database", _harness.VaultPath, Fix: null), found[2]);
        Assert.Equal(new Finding("tool", "AI tools", "note", "no AI tool keypaste connects is installed here", Where: null, Fix: null), found[3]);
        Assert.Equal(
            new Finding("owner", "owner", "note", "nothing holds the vault unlocked; agents are refused", Where: null, "unlock it in the keypaste app, or run `keypaste approve`"),
            found[4]);
    }

    [Fact]
    public async Task An_owner_holding_the_vault_unlocked_answers_and_counts_no_agent()
    {
        Healthy();
        await using var owner = SessionOwner.Start(_harness);

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(new Finding("owner", "owner", "ok", "a keypaste process holds the vault and answers", Where: null, Fix: null), Owner(found));
        Assert.Equal(
            new Finding("versions", "versions", "ok", $"{CoreInfo.Version} here and in the keypaste process holding the vault", Where: null, Fix: null),
            Assert.Single(found, finding => finding.Area == "versions"));
        Assert.Empty(owner.Authority.Clients);
    }

    [Fact]
    public async Task An_owner_holding_the_vault_locked_is_a_note()
    {
        Healthy();
        await using var owner = SessionOwner.Start(_harness);
        owner.Lifetime.End();

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(
            new Finding("owner", "owner", "note", "a keypaste process holds the vault locked; agents are refused", Where: null, "unlock it in the keypaste app"),
            Owner(found));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_owner_older_than_this_keypaste_is_a_fix_to_start_it_again(bool givesVersion)
    {
        Healthy();
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        done.CancelAfter(_ownerWait);
        var owner = AnswerOnceAsync(Pipe(), AttachReply.To("doctor-session") with { OwnerVersion = givesVersion ? "0.0.1" : null }, done.Token);

        var (exit, found) = Doctor();
        await owner;

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        var says = givesVersion
            ? $"the keypaste process holding the vault is 0.0.1, this is {CoreInfo.Version}"
            : "the keypaste process holding the vault is older than this keypaste";
        Assert.Equal(new Finding("versions", "versions", "fix", says, Where: null, _restart), Assert.Single(found, finding => finding.Area == "versions"));
        Assert.Equal("ok", Owner(found).Status);
    }

    [Fact]
    public async Task An_owner_holding_another_vault_is_a_fix()
    {
        Healthy();
        var other = PlantVault(Path.Combine(_harness.Directory, "other.kdbx"));
        await using var owner = SessionOwner.Start(_harness);

        var (exit, found) = Doctor("--vault", other);

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal(
            new Finding(
                "owner",
                "owner",
                "fix",
                "the keypaste process that answered holds a different vault",
                Where: null,
                $"unset KEYPASTE_APPROVER, or point it at the process holding {other}"),
            Owner(found));
    }

    [Fact]
    public async Task An_owner_that_does_not_answer_is_a_fix_to_start_it_again()
    {
        Healthy();
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        done.CancelAfter(_ownerWait);
        var owner = AnswerOnceAsync(Pipe(), reply: null, done.Token);

        var (exit, found) = Doctor();
        await owner;

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal(
            new Finding("owner", "owner", "fix", "the keypaste process holding the vault did not answer; it may be older than this keypaste", Where: null, _restart),
            Owner(found));
    }

    [Fact]
    public void A_pipe_name_keypaste_does_not_accept_is_a_fix()
    {
        Healthy();
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = "not/a-pipe";

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        var owner = Owner(found);
        Assert.Equal("fix", owner.Status);
        Assert.StartsWith("KEYPASTE_APPROVER is not a pipe name keypaste accepts: ", owner.Says, StringComparison.Ordinal);
        Assert.Equal($"unset KEYPASTE_APPROVER, or point it at the process holding {_harness.VaultPath}", owner.Fix);
    }

    [Fact]
    public void A_pipe_name_a_locked_app_listens_on_is_a_fix_and_is_never_connected_to()
    {
        Healthy();
        var pipe = LockedAppEndpoint.NameFor(VaultIdentity.Of(Home, _harness.VaultPath));
        var asked = 0;
        using var locked = LockedAppEndpoint.Listen(pipe, () => Interlocked.Increment(ref asked), _ => { });
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal(
            new Finding(
                "owner",
                "owner",
                "fix",
                "KEYPASTE_APPROVER is not a pipe name keypaste accepts: an approver name cannot start with keypaste-locked-, which names a locked keypaste app's pipe",
                Where: null,
                $"unset KEYPASTE_APPROVER, or point it at the process holding {_harness.VaultPath}"),
            Owner(found));
        Assert.Equal(0, Volatile.Read(ref asked));
    }

    [Fact]
    public void Naming_another_vault_notes_that_ai_tools_use_the_chosen_one()
    {
        Healthy();
        var other = PlantVault(Path.Combine(_harness.Directory, "other.kdbx"));

        var (exit, found) = Doctor("--vault", other);

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(
            [
                new Finding("vault", "vault", "ok", "the vault named with --vault is a KeePass database", other, Fix: null),
                new Finding("vault", "vault", "note", "AI tools use the chosen vault unless their registration names one", _harness.VaultPath, Fix: null),
            ],
            found.Where(finding => finding.Area == "vault"));
    }

    [Fact]
    public void A_vault_that_is_not_there_is_a_fix_saying_how_to_choose_or_create_it()
    {
        Healthy();
        var missing = Path.Combine(_harness.Directory, "missing.kdbx");

        var (exit, found) = Doctor("--vault", missing);

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal(
            new Finding(
                "vault",
                "vault",
                "fix",
                "the vault named with --vault is missing",
                missing,
                "run `keypaste use <path>` to choose your vault, or `keypaste init <path>` to create it"),
            Assert.Single(found, finding => finding.Area == "vault"));
    }

    [Fact]
    public void A_vault_named_by_the_environment_that_is_not_keepass_is_a_fix()
    {
        Healthy();
        var notes = Path.Combine(_harness.Directory, "notes.txt");
        File.WriteAllText(notes, "plain text, and longer than a KeePass header");
        _harness.Environment[VaultLocation.EnvironmentVariable] = notes;

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal(
            new Finding("vault", "vault", "fix", "the vault named by KEYPASTE_VAULT is not a KeePass database", notes, "run `keypaste use <path>` with your vault"),
            Assert.Single(found, finding => finding.Area == "vault"));
    }

    [Fact]
    public void An_app_toml_that_does_not_parse_is_a_fix_naming_its_line_for_the_vault_and_the_file()
    {
        Healthy();
        var settings = KeypasteHome.SettingsPath(Home);
        File.WriteAllText(settings, "this is not toml\n");

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        var vault = Assert.Single(found, finding => finding.Area == "vault");
        Assert.StartsWith("no vault is chosen: app.toml could not be read (line 1: ", vault.Says, StringComparison.Ordinal);
        Assert.Equal((settings, $"fix or delete {settings}, then run `keypaste use <path>`"), (vault.Where, vault.Fix));
        var app = Assert.Single(found, finding => finding.Subject == "app.toml");
        Assert.Equal(("settings", "fix", settings), (app.Area, app.Status, app.Where));
    }

    [Fact]
    public void A_registered_keypaste_of_another_version_is_asked_only_its_version_and_its_help()
    {
        Healthy();
        var old = Another("old", "0.0.1");
        Register(McpConfigLocation.Cursor, old, "mcp", "--client-label", "cursor");

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal([$"{old} --version", $"{old} mcp --help --client-label cursor"], _harness.ProcessRunner.Calls);
        Assert.Equal(
            new Finding(
                "versions",
                "versions",
                "fix",
                $"Cursor starts keypaste 0.0.1, this is {CoreInfo.Version}",
                Where: null,
                "run `keypaste setup --client cursor` from the keypaste you keep"),
            Assert.Single(found, finding => finding.Area == "versions"));
        Assert.Equal(new Finding("tool", "cursor", "ok", "starts keypaste 0.0.1 from a place upgrades keep", old, Fix: null), Tool(found));
    }

    [Fact]
    public void A_registered_keypaste_that_cannot_be_started_is_a_fix_quoting_its_first_line()
    {
        Healthy();
        var old = Another("old", "0.0.1");
        _harness.ProcessRunner.Refuses[old] = "cannot execute binary file\nsecond line";
        Register(McpConfigLocation.Cursor, old, "mcp");

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal([$"{old} --version"], _harness.ProcessRunner.Calls);
        Assert.Equal(
            new Finding("tool", "cursor", "fix", "could not be started: cannot execute binary file", old, "run `keypaste setup --client cursor`"),
            Tool(found));
    }

    [Fact]
    public void A_tool_installed_and_not_connected_is_a_note_saying_how_to_connect_it()
    {
        Healthy();
        Directory.CreateDirectory(McpConfigLocation.Cursor.Folders(Machine())[0]);

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(new Finding("tool", "cursor", "note", "installed but not connected", Where: null, "run `keypaste setup --client cursor`"), Tool(found));
        Assert.Empty(_harness.ProcessRunner.Calls);
    }

    [Fact]
    public void A_tools_file_keypaste_cannot_read_is_a_note_and_starts_nothing()
    {
        Healthy();
        var path = McpConfigLocation.Cursor.PathOn(Machine())!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"mcpServers\": ");

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        var cursor = Tool(found);
        Assert.Equal(("cursor", "note", path), (cursor.Subject, cursor.Status, cursor.Where));
        Assert.StartsWith("keypaste cannot tell whether it is connected: ", cursor.Says, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Another_keypaste_first_on_path_is_asked_only_its_version()
    {
        Healthy();
        var other = Another("other", "0.0.1");
        _harness.Environment["PATH"] = Path.GetDirectoryName(other) + Path.PathSeparator + Bin;

        var (exit, found) = Doctor();

        _harness.AssertExit(CliApp.ExitNeedsFixing, exit);
        Assert.Equal([$"{other} --version"], _harness.ProcessRunner.Calls);
        Assert.Equal(new Finding("path", "PATH", "ok", "a terminal runs another keypaste", other, Fix: null), Assert.Single(found, finding => finding.Area == "path"));
        Assert.Equal(
            new Finding("versions", "versions", "fix", $"a terminal runs keypaste 0.0.1, this is {CoreInfo.Version}", other, "a terminal runs that one: update or remove it"),
            Assert.Single(found, finding => finding.Area == "versions"));
    }

    [Fact]
    public void What_another_program_wrote_reaches_the_terminal_as_text()
    {
        Healthy();
        var old = Another("old", "0.0.1");
        _harness.ProcessRunner.Refuses[old] = "\u001b]0;owned\u0007\u202Erefused";
        Register(McpConfigLocation.Cursor, old, "mcp");

        _harness.AssertExit(CliApp.ExitNeedsFixing, _harness.Run("doctor"));

        Assert.DoesNotContain("\u001b", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("]0;owned refused", _harness.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// The founder's rule for doctor: it starts no AI tool and asks for nothing, and every byte under the harness, the
    /// settings files, the AI tools' files and Claude Code's sign-in among them, is as it was.
    /// </summary>
    [Fact]
    public async Task Doctor_changes_no_byte_under_home_starts_no_ai_tool_and_asks_nothing()
    {
        Healthy();
        await using var owner = SessionOwner.Start(_harness);
        var old = Another("old", CoreInfo.Version);
        string[] tools = [Plant(Bin, "claude"), Plant(Bin, "codex")];

        File.WriteAllText(KeypasteHome.ClientsPath(Home), "this is not toml\n");
        File.WriteAllText(KeypasteHome.RecentPath(Home), "[[vault]]\n");
        File.WriteAllText(KeypasteHome.ProjectsPath(Home), "{}");
        File.WriteAllText(KeypasteHome.RecommendationsPath(Home), "[]");
        File.WriteAllText(KeypasteHome.PolicyPath(Home), "[[agent]]\n");

        var claude = new JsonObject
        {
            ["oauthAccount"] = new JsonObject { ["accountUuid"] = _signIn },
            ["mcpServers"] = new JsonObject
            {
                ["other"] = new JsonObject { ["command"] = "other-server" },
                ["keypaste"] = new JsonObject { ["command"] = This, ["args"] = Arguments("mcp", "--client-label", "claude-code") },
            },
        };
        File.WriteAllText(McpConfigLocation.ClaudeCode.PathOn(Machine())!, claude.ToJsonString());

        var codex = McpConfigLocation.Codex.PathOn(Machine())!;
        Directory.CreateDirectory(Path.GetDirectoryName(codex)!);
        File.WriteAllText(codex, $"model = \"o4\"\n\n[mcp_servers.keypaste]\ncommand = '{old}'\nargs = [\"mcp\", \"--client-label\", \"codex\"]\n");
        Register(McpConfigLocation.Cursor, This, "mcp");

        _harness.Prompt.Interactive = true;
        var before = Snapshot(_harness.Directory);

        _harness.AssertExit(CliApp.ExitNeedsFixing, _harness.Run("doctor"));

        Assert.Equal(before, Snapshot(_harness.Directory));
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains($"{old} --version", _harness.ProcessRunner.Calls);
        Assert.All(_harness.ProcessRunner.Calls, call => Assert.True(OnlyAsks(call, This, old), call));
        Assert.DoesNotContain(_harness.ProcessRunner.Calls, call => tools.Any(tool => call.StartsWith(tool, StringComparison.Ordinal)));
        Assert.Empty(owner.Authority.Clients);
        Assert.False(File.Exists(KeypasteHome.AuditPath(Home)));
        Assert.False(Directory.Exists(Path.Combine(Home, VaultClaim.DirectoryName)));
        Assert.DoesNotContain(_signIn, _harness.Out, StringComparison.Ordinal);
    }

    /// <summary>This keypaste first on PATH, and a chosen vault that is a KeePass database.</summary>
    private void Healthy()
    {
        _harness.ProcessPath = Plant(Bin, McpServerLocator.FileName);
        _harness.ProcessRunner.Installed.Add(This);
        _harness.Environment["PATH"] = Bin;
        PlantVault(_harness.VaultPath);
        Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(Home, _harness.VaultPath, onlyIfNone: false));
    }

    /// <summary>Another keypaste, in a folder of its own, whose <c>--version</c> prints <paramref name="version"/>.</summary>
    private string Another(string folder, string version)
    {
        var program = Plant(Path.Combine(_harness.Directory, folder), McpServerLocator.FileName);
        _harness.ProcessRunner.Installed.Add(program);
        _harness.ProcessRunner.Says[program] = version + "\n";
        return program;
    }

    private (int Exit, List<Finding> Found) Doctor(params string[] extra)
    {
        var exit = _harness.Run(["doctor", "--json", .. extra]);
        using var json = JsonDocument.Parse(_harness.Out);
        List<Finding> found = [.. json.RootElement.EnumerateArray().Select(Parsed)];

        return (exit, found);
    }

    private static Finding Parsed(JsonElement item) => new(
        item.GetProperty("area").GetString()!,
        item.GetProperty("subject").GetString()!,
        item.GetProperty("status").GetString()!,
        item.GetProperty("says").GetString()!,
        item.GetProperty("where").GetString(),
        item.GetProperty("fix").GetString());

    private McpClientMachine Machine() =>
        McpClientMachine.From(name => _harness.Environment.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);

    /// <summary>Writes keypaste's entry into a tool's own JSON file.</summary>
    private void Register(McpConfigLocation location, string command, params string[] arguments)
    {
        var path = location.PathOn(Machine())!;
        var servers = new JsonObject { ["keypaste"] = new JsonObject { ["command"] = command, ["args"] = Arguments(arguments) } };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonObject { ["mcpServers"] = servers }.ToJsonString());
    }

    /// <summary>Listens on <paramref name="pipe"/> for one attach naming no AI tool, and answers it with <paramref name="reply"/>, or hangs up when it is null.</summary>
    private static async Task AnswerOnceAsync(string pipe, AttachReply? reply, CancellationToken cancellationToken)
    {
        await using var server = new NamedPipeServerStream(
            pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(cancellationToken);
        using var framer = new MessageFramer(server, ownsStream: false);

        var frame = await framer.ReadAsync(cancellationToken);
        Assert.True(ApproverProtocol.TryDecode(frame, out AttachRequest? attach));
        Assert.Null(attach!.Client);

        if (reply is not null)
        {
            await framer.WriteAsync(ApproverProtocol.Encode(reply), cancellationToken);
        }
    }

    private string Pipe()
    {
        var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;
        return pipe;
    }

    private static Finding Owner(List<Finding> found) => Assert.Single(found, finding => finding.Area == "owner");

    private static Finding Tool(List<Finding> found) => Assert.Single(found, finding => finding.Area == "tool");

    private static JsonArray Arguments(params string[] arguments) => new(arguments.Select(argument => (JsonNode?)argument).ToArray());

    private static string Plant(string folder, string name)
    {
        var path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    /// <summary>A file holding only the twelve bytes a KeePass database starts with, which is all doctor reads of one.</summary>
    private static string PlantVault(string path)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, KdbxFormat.FileSignature1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), KdbxFormat.FileSignature2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), 4);
        File.WriteAllBytes(path, header);
        return path;
    }

    /// <summary>Whether a call starts one of these keypaste files with <c>--version</c>, or with <c>mcp --help</c> and its registration's flags.</summary>
    private static bool OnlyAsks(string call, params string[] keypastes) =>
        keypastes.Any(program => call == $"{program} --version" || call.StartsWith($"{program} mcp --help", StringComparison.Ordinal));

    /// <summary>Every file and folder under <paramref name="root"/>, each file with its SHA-256.</summary>
    private static List<string> Snapshot(string root) =>
        [.. Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(entry => File.Exists(entry) ? $"{entry} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}" : entry)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The argon2 row for the library this process derived with: the file a test build carries beside it on Windows and macOS
    /// (D-0523), or on Linux the system's, or the note where there is none.
    /// </summary>
    private static string[] Argon2Rows() => (NativeArgon2.BundledFileName, CoreInfo.NativeArgon2) switch
    {
        ({ } file, _) => [$"  ✓ argon2           this keypaste derives vault keys with {file}", $"                     {Path.Combine(AppContext.BaseDirectory, file)}"],
        (null, { } system) => [$"  ✓ argon2           this keypaste derives vault keys with the system's {system}"],
        _ => ["  · argon2           no libargon2 is installed, so unlocking and saving take longer", "                     › install your distribution's libargon2 package"],
    };

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;

    /// <summary>One object of <c>doctor --json</c>.</summary>
    private sealed record Finding(string Area, string Subject, string Status, string Says, string? Where, string? Fix);
}
