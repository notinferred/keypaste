using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What <c>keypaste setup</c> asks each client to do, what it asks the person, and what it refuses to do.
/// </summary>
/// <remarks>
/// Every case plants its clients as files on a <c>PATH</c> that holds only the harness's own folder and runs them
/// against <see cref="FakeProcessRunner"/>, and every client's file is under the harness's home, so nothing in this file
/// can reach a real client's configuration. The assertions are on the argv keypaste constructs and the files it edits,
/// because those <em>are</em> the feature.
/// </remarks>
public sealed class SetupVerbTests
{
    private const string _master = "correct horse battery staple";

    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const string _holdingKeypaste = """{"mcpServers": {"keypaste": {"command": "k"}}}""";

    private static CliHarness Wired(params string[] commands)
    {
        var harness = new CliHarness();
        Directory.CreateDirectory(Bin(harness));
        harness.Environment["PATH"] = Bin(harness);

        foreach (var command in commands)
        {
            harness.ProcessRunner.Installed.Add(Plant(Bin(harness), command));
        }

        return harness;
    }

    private static string Bin(CliHarness harness) => Path.Combine(harness.Directory, "bin");

    private static string Program(CliHarness harness, string command) => Path.Combine(Bin(harness), Exe(command));

    private static string Exe(string command) => OperatingSystem.IsWindows() ? command + ".exe" : command;

    private static string Plant(string folder, string command)
    {
        var path = Path.Combine(folder, Exe(command));
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, _executable);
        }

        return path;
    }

    /// <summary>The calls that changed something, each named by its bare command as a person would type it.</summary>
    private static List<string> Ran(CliHarness harness) =>
        [.. harness.ProcessRunner.RealCalls.Select(call => call.StartsWith(Bin(harness) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? Bare(call[(Bin(harness).Length + 1)..])
            : call)];

    private static string Bare(string call)
    {
        var space = call.IndexOf(' ', StringComparison.Ordinal);
        return Path.GetFileNameWithoutExtension(call[..space]) + call[space..];
    }

    private static bool Adds(string call) => call.Contains("mcp add", StringComparison.Ordinal);

    // On macOS the bridge is registered with every link in its path resolved, /var's among them.
    private static string ServerPath(CliHarness harness) => OperatingSystem.IsMacOS()
        ? PathIdentity.Canonical(Path.Combine(harness.Directory, McpServerLocator.ExecutableName))
        : Path.Combine(harness.Directory, McpServerLocator.ExecutableName);

    private static McpClientMachine Machine(CliHarness harness) =>
        McpClientMachine.From(name => harness.Environment.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);

    private static string Seed(CliHarness harness, string id, string text)
    {
        var path = McpClientCatalog.Find(id)!.Configuration!.PathOn(Machine(harness))!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static int RunSetup(CliHarness harness, params string[] extra) =>
        harness.Run(["setup", "--yes", "--vault", harness.VaultPath, .. extra]);

    [Fact]
    public void Claude_code_is_told_the_scope_and_the_transport()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.StartsWith("claude mcp add --scope user --transport stdio keypaste --", call, StringComparison.Ordinal);
        Assert.Contains("--client-label claude-code", call, StringComparison.Ordinal);
        Assert.Contains(harness.VaultPath, call, StringComparison.Ordinal);
    }

    /// <summary>
    /// The keypaste that runs setup is the bridge, so a client is told to start it as <c>keypaste mcp</c>,
    /// with <c>mcp</c> before the bridge's own flags.
    /// </summary>
    [Fact]
    public void The_running_keypaste_is_started_as_the_bridge()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.Contains($"-- {ServerPath(harness)} mcp --vault ", call, StringComparison.Ordinal);
    }

    /// <summary>
    /// Run through <c>dotnet</c>, the running program is not keypaste, and registering it would start the
    /// wrong thing.
    /// </summary>
    [Fact]
    public void Setup_run_through_dotnet_registers_nothing()
    {
        using var harness = Wired("claude");
        harness.ProcessPath = Path.Combine(harness.Directory, "dotnet");

        Assert.NotEqual(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        Assert.Empty(harness.ProcessRunner.Calls);
        Assert.Contains("rather than through dotnet", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Codex rejects <c>--scope</c> and <c>--transport</c>; they are Claude Code's alone.
    /// </summary>
    [Fact]
    public void Codex_is_not_told_about_a_scope_it_has_no_concept_of()
    {
        using var harness = Wired("codex");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "codex"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.StartsWith("codex mcp add keypaste --", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--scope", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--transport", call, StringComparison.Ordinal);
    }

    [Fact]
    public void Gemini_cli_is_given_the_program_before_the_separator_and_the_person_is_told_about_trusted_folders()
    {
        using var harness = Wired("gemini");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "gemini-cli"));

        Assert.StartsWith($"gemini mcp add --scope user keypaste {ServerPath(harness)} -- mcp --vault ", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
        Assert.Contains("  gemini-cli       configured; Gemini CLI starts keypaste only in folders you have trusted", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Devin_is_told_its_user_scope()
    {
        using var harness = Wired("devin");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "devin"));

        Assert.StartsWith("devin mcp add -s user keypaste -- ", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
    }

    /// <summary>
    /// The audit log exists to tell clients apart, so one run wiring two clients must not label
    /// them both the same.
    /// </summary>
    [Fact]
    public void Each_client_is_labelled_as_itself()
    {
        using var harness = Wired("claude", "codex");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness));

        var calls = Ran(harness).Where(Adds).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c.Contains("--client-label claude-code", StringComparison.Ordinal));
        Assert.Contains(calls, c => c.Contains("--client-label codex", StringComparison.Ordinal));
    }

    [Fact]
    public void An_absent_client_is_reported_and_not_invoked()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness));

        Assert.Contains("  codex            not installed on this machine", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.ProcessRunner.Calls, c => c.Contains("codex", StringComparison.Ordinal));
    }

    /// <summary>A client is found only on PATH's absolute entries, never in the directory setup runs in (D-0436).</summary>
    [Fact]
    public void A_client_in_the_working_directory_and_not_on_path_is_not_installed_and_never_run()
    {
        using var harness = Wired();
        var work = Directory.CreateDirectory(Path.Combine(harness.Directory, "work")).FullName;
        harness.ProcessRunner.Installed.Add(Plant(work, "claude"));
        harness.ProcessRunner.Installed.Add("claude");
        harness.WorkingDirectory = work;

        harness.AssertExit(CliApp.ExitNotFound, RunSetup(harness, "--client", "claude-code"));

        Assert.Empty(harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Dry_run_prints_every_step_and_the_program_it_would_start_and_changes_nothing()
    {
        using var harness = Wired("claude", "codex");
        var cursor = Seed(harness, "cursor", "{}");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--dry-run", "--vault", harness.VaultPath));

        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains("  claude-code      claude mcp remove --scope user keypaste", harness.Out, StringComparison.Ordinal);
        Assert.Contains($"  claude-code      starts: {Program(harness, "claude")} mcp remove --scope user keypaste", harness.Out, StringComparison.Ordinal);
        Assert.Contains($"  cursor           edit {cursor}: set mcpServers.keypaste ", harness.Out, StringComparison.Ordinal);
        Assert.Equal("{}", File.ReadAllText(cursor));
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void One_answer_covers_every_client_and_anything_but_yes_changes_nothing()
    {
        using var harness = Wired("claude", "codex");
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue("n");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("setup", "--vault", harness.VaultPath));

        Assert.Equal(["Connect keypaste to these 2 tools? [y/N] "], harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains("Cancelled. Nothing was changed.", harness.Err, StringComparison.Ordinal);

        harness.Prompt.PromptsSeen.Clear();
        harness.Prompt.Enqueue("y");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--vault", harness.VaultPath));

        Assert.Single(harness.Prompt.PromptsSeen);
        Assert.Equal(2, Ran(harness).Count(Adds));
    }

    [Fact]
    public void Without_a_terminal_and_without_yes_nothing_is_started_or_written()
    {
        using var harness = Wired("claude");
        var cursor = Seed(harness, "cursor", "{}");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("setup", "--vault", harness.VaultPath));

        Assert.Empty(harness.ProcessRunner.Calls);
        Assert.Equal("{}", File.ReadAllText(cursor));
        Assert.Contains("keypaste setup: --yes is required when stdin is not a terminal; nothing was changed", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Yes_asks_nothing()
    {
        using var harness = Wired("claude");
        harness.Prompt.Interactive = true;

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Single(Ran(harness), Adds);
    }

    [Fact]
    public void No_client_at_all_changes_nothing_and_says_so()
    {
        using var harness = Wired();

        harness.AssertExit(CliApp.ExitNotFound, RunSetup(harness));

        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains("keypaste: no AI tool keypaste connects is installed here. Nothing was changed.", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_client_found_refusing_is_its_own_exit_and_one_connecting_is_success()
    {
        using var harness = Wired("claude", "codex");
        harness.ProcessRunner.Refuses[Program(harness, "claude")] = "that scope is not writable here";

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness));

        harness.ProcessRunner.Refuses[Program(harness, "codex")] = "no";

        harness.AssertExit(CliApp.ExitClientsRefused, RunSetup(harness));
    }

    [Fact]
    public void An_unknown_client_is_a_usage_error_that_lists_the_known_ones()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitUsageError, RunSetup(harness, "--client", "emacs"));

        Assert.Contains("no client called 'emacs'", harness.Err, StringComparison.Ordinal);
        Assert.Contains("claude-code, codex, gemini-cli, devin, vscode, cursor", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Claude_desktop_is_not_offered_on_linux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Claude Desktop has no Linux app");

        using var harness = Wired();

        harness.AssertExit(CliApp.ExitUsageError, RunSetup(harness, "--client", "claude-desktop"));
    }

    /// <summary>
    /// Omitted rather than restated: the bridge owns the default, so there is one place a
    /// reader can learn what it is.
    /// </summary>
    [Fact]
    public void No_expose_flag_is_passed_unless_one_was_asked_for()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));
        Assert.DoesNotContain("--expose", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_expose_glob_becomes_its_own_flag()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code", "--expose", "env/**,deploy/**"));

        Assert.Contains("--expose env/** --expose deploy/**", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
    }

    /// <summary>
    /// Wiring before the vault exists is the ordinary case — a reader follows the install page
    /// top to bottom — so it warns and proceeds rather than refusing.
    /// </summary>
    [Fact]
    public void A_vault_that_does_not_exist_yet_warns_but_still_wires()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        Assert.Contains("there is no vault at", harness.Err, StringComparison.Ordinal);
        Assert.Single(Ran(harness), Adds);
    }

    /// <summary>
    /// Running it twice has to work, because re-running is how a moved vault or a moved binary is
    /// fixed. The clients disagree: Codex overwrites an existing entry, Claude Code refuses it —
    /// so keypaste clears first and the two behave alike.
    /// </summary>
    [Fact]
    public void Wiring_a_client_that_already_has_keypaste_clears_it_first()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        var calls = Ran(harness);
        Assert.Equal(2, calls.Count);
        Assert.Equal("claude mcp remove --scope user keypaste", calls[0]);
        Assert.Contains("mcp add", calls[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_that_refuses_is_reported_in_its_own_words()
    {
        using var harness = Wired("claude");
        harness.ProcessRunner.Refuses[Program(harness, "claude")] = "that scope is not writable here";

        harness.AssertExit(CliApp.ExitClientsRefused, RunSetup(harness, "--client", "claude-code"));

        Assert.Contains("  claude-code      Claude Code refused", harness.Out, StringComparison.Ordinal);
        Assert.Contains("keypaste: Claude Code said: that scope is not writable here", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_configured_by_file_has_only_keypastes_entry_written_and_its_original_kept()
    {
        using var harness = Wired();
        const string Original = """{"mcpServers": {"other": {"command": "other"}}}""";
        var cursor = Seed(harness, "cursor", Original);
        var backup = McpConfigFile.BackupPath(cursor);

        harness.AssertExit(CliApp.ExitSuccess, RunSetup(harness, "--client", "cursor"));

        Assert.Contains($"  cursor           edit {cursor}: set mcpServers.keypaste ", harness.Out, StringComparison.Ordinal);
        Assert.Contains($"  cursor           configured; the original is kept as {backup}; restart Cursor to load it", harness.Out, StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllText(backup));
        using var written = JsonDocument.Parse(File.ReadAllText(cursor));
        var servers = written.RootElement.GetProperty("mcpServers");
        Assert.Equal(ServerPath(harness), servers.GetProperty("keypaste").GetProperty("command").GetString());
        Assert.Equal("other", servers.GetProperty("other").GetProperty("command").GetString());
        Assert.Empty(harness.ProcessRunner.Calls);
    }

    [Fact]
    public void Without_a_vault_named_the_client_is_given_none_and_its_bridge_uses_the_chosen_one()
    {
        using var harness = Wired("claude");
        harness.SeedVault(_master, ("env/demo/KEY", "value"));
        ChosenVault.Choose(harness.Environment[KeypasteHome.EnvironmentVariable], harness.VaultPath, onlyIfNone: false);

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.DoesNotContain("--vault", call, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.VaultPath, call, StringComparison.Ordinal);
        Assert.Contains($"vault          {harness.VaultPath} (the chosen vault; the client's entry names none)", harness.Out, StringComparison.Ordinal);
        Assert.EndsWith("  keypaste agent", harness.Out.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_vault_chosen_it_still_wires_and_says_how_to_choose_one()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.DoesNotContain("--vault", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
        Assert.Contains("vault          none chosen yet", harness.Out, StringComparison.Ordinal);
        Assert.Contains("keypaste use <path>", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_vault_still_pins_the_client_to_it()
    {
        using var harness = Wired("claude");
        harness.Environment[VaultLocator.EnvironmentVariable] = harness.VaultPath;

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.Contains($"--vault {harness.VaultPath}", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
        Assert.EndsWith($"  keypaste agent --vault {harness.VaultPath}", harness.Out.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Naming_the_chosen_vault_does_not_pin_the_client_to_it()
    {
        using var harness = Wired("claude");
        ChosenVault.Choose(harness.Environment[KeypasteHome.EnvironmentVariable], harness.VaultPath, onlyIfNone: false);
        harness.Environment[VaultLocator.EnvironmentVariable] = harness.VaultPath;

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.DoesNotContain("--vault", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
        Assert.EndsWith("  keypaste agent", harness.Out.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_path_is_made_absolute_because_a_clients_working_directory_is_not_ours()
    {
        using var harness = Wired("claude");
        harness.SeedVault(_master, ("env/demo/KEY", "value"));

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--vault", harness.VaultPath));

        Assert.Contains(Path.GetFullPath(harness.VaultPath), Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_install_pins_no_audit_log_and_no_approver()
    {
        using var harness = Wired("claude");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.DoesNotContain("--audit-log", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--approver", call, StringComparison.Ordinal);
        Assert.DoesNotContain("audit log", harness.Out, StringComparison.Ordinal);
    }

    /// <summary>A client may start the bridge without KEYPASTE_HOME, and Gemini CLI always does, so another home is written into it (D-0389).</summary>
    [Fact]
    public void Another_home_pins_its_chosen_vault_its_log_and_its_owner()
    {
        using var harness = Wired("claude");
        var home = Path.Combine(harness.Directory, "elsewhere");
        harness.Environment[KeypasteHome.EnvironmentVariable] = home;
        ChosenVault.Choose(home, harness.VaultPath, onlyIfNone: false);
        var owner = ApproverEndpoint.Resolve(null, null, VaultIdentity.Of(KeypasteHome.Resolve(home), harness.VaultPath));
        var audit = KeypasteHome.AuditPath(home);

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.Contains($"-- {ServerPath(harness)} mcp --vault {harness.VaultPath} --audit-log {audit} --approver {owner} --client-label claude-code", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
        Assert.Contains($"audit log      {audit}", harness.Out, StringComparison.Ordinal);
        Assert.Contains($"approver       {owner}", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_home_with_no_vault_chosen_changes_nothing_and_says_to_choose_one()
    {
        using var harness = Wired("claude");
        var home = Path.Combine(harness.Directory, "elsewhere");
        harness.Environment[KeypasteHome.EnvironmentVariable] = home;
        var cursor = Seed(harness, "cursor", "{}");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("setup", "--yes"));

        Assert.Empty(harness.ProcessRunner.Calls);
        Assert.Equal("{}", File.ReadAllText(cursor));
        Assert.Contains(
            $"keypaste: KEYPASTE_HOME is {home}, which a tool started without it does not see; choose a vault with keypaste use <path>, or name one with --vault. Nothing was changed.",
            harness.Err,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_approver_under_the_default_home_is_pinned_alone()
    {
        using var harness = Wired("claude");
        harness.Environment[ApproverEndpoint.EnvironmentVariable] = "keypaste-test-pipe";

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        var call = Assert.Single(Ran(harness), Adds);
        Assert.Contains($"-- {ServerPath(harness)} mcp --approver keypaste-test-pipe --client-label claude-code", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--audit-log", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--vault", call, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_takes_keypaste_out_only_where_a_clients_own_file_holds_it()
    {
        using var harness = Wired("claude", "codex");
        Seed(harness, "claude-code", _holdingKeypaste);
        Seed(harness, "codex", "[mcp_servers.other]\ncommand = \"other\"\n");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--remove", "--yes"));

        Assert.Equal(["claude mcp remove --scope user keypaste"], Ran(harness));
        Assert.Contains("  claude-code      removed", harness.Out, StringComparison.Ordinal);
        Assert.Contains("  codex            not connected; nothing to remove", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_with_nothing_connected_asks_nothing_and_succeeds()
    {
        using var harness = Wired("claude", "codex");
        harness.Prompt.Interactive = true;

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--remove"));

        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessRunner.RealCalls);
    }

    [Fact]
    public void Remove_asks_once_for_every_client_holding_keypaste()
    {
        using var harness = Wired("claude", "codex");
        Seed(harness, "claude-code", _holdingKeypaste);
        Seed(harness, "codex", "[mcp_servers.keypaste]\ncommand = \"k\"\n");
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue("y");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--remove"));

        Assert.Equal(["Remove keypaste from these 2 tools? [y/N] "], harness.Prompt.PromptsSeen);
        Assert.Equal(["claude mcp remove --scope user keypaste", "codex mcp remove keypaste"], Ran(harness));
    }

    [Fact]
    public void A_failed_removal_is_shown_in_the_clients_words_and_every_one_failing_is_its_own_exit()
    {
        using var harness = Wired("claude");
        Seed(harness, "claude-code", _holdingKeypaste);
        harness.ProcessRunner.Refuses[Program(harness, "claude")] = "No MCP server found with name: keypaste";

        harness.AssertExit(CliApp.ExitClientsRefused, harness.Run("setup", "--remove", "--yes"));

        Assert.Contains("keypaste: Claude Code said: No MCP server found with name: keypaste", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_from_a_client_configured_by_file_edits_only_its_entry()
    {
        using var harness = Wired();
        var cursor = Seed(harness, "cursor", """{"mcpServers": {"keypaste": {"command": "k"}}}""");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--remove", "--yes"));

        Assert.Equal("""{"mcpServers": {}}""", File.ReadAllText(cursor));
        Assert.Contains($"  cursor           removed; the original is kept as {McpConfigFile.BackupPath(cursor)}", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Inside_an_appimage_the_link_is_created_first_and_registered()
    {
        using var harness = Wired("claude");
        var link = new ScriptedLink(harness.Directory, CliLinkState.Absent);
        harness.AppImage = new McpAppImage("/opt/keypaste.AppImage", link);

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.Equal(1, link.Links);
        Assert.Contains($"  keypaste         create {link.LinkPath}, a script that starts /opt/keypaste.AppImage cli", harness.Out, StringComparison.Ordinal);
        Assert.True(
            harness.Out.IndexOf("  keypaste         create", StringComparison.Ordinal) < harness.Out.IndexOf("  claude-code      claude mcp", StringComparison.Ordinal),
            "the link step is not printed first");
        Assert.Contains($"-- {link.LinkPath} mcp ", Assert.Single(Ran(harness), Adds), StringComparison.Ordinal);
    }

    [Fact]
    public void Inside_an_appimage_a_file_keypaste_did_not_write_is_replaced_only_after_the_replace_question()
    {
        using var harness = Wired("claude");
        var link = new ScriptedLink(harness.Directory, CliLinkState.Foreign);
        harness.AppImage = new McpAppImage("/opt/keypaste.AppImage", link);
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue("y");

        harness.AssertExit(CliApp.ExitSuccess, harness.Run("setup", "--client", "claude-code"));

        Assert.Equal([$"Replace {link.LinkPath} and connect keypaste to this tool? [y/N] "], harness.Prompt.PromptsSeen);
        Assert.Contains($"  keypaste         replace {link.LinkPath}, which is another program, with a script", harness.Out, StringComparison.Ordinal);
        Assert.Equal((0, 1), (link.Links, link.Replaces));
    }

    [Fact]
    public void Inside_an_appimage_a_file_keypaste_did_not_write_is_refused_before_asking_while_its_backup_exists()
    {
        using var harness = Wired("claude");
        var link = new ScriptedLink(harness.Directory, CliLinkState.Foreign) { BackupExists = true };
        harness.AppImage = new McpAppImage("/opt/keypaste.AppImage", link);
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue("y");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("setup", "--client", "claude-code"));

        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Equal((0, 0), (link.Links, link.Replaces));
        Assert.Contains(
            $"keypaste: {link.LinkPath} is another program, and {link.BackupPath} already exists, which keypaste never overwrites. "
            + $"Move or delete it to replace {link.LinkPath}. Nothing was changed.",
            harness.Err,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inside_an_appimage_a_link_that_is_not_made_runs_no_client_step()
    {
        using var harness = Wired("claude");
        var link = new ScriptedLink(harness.Directory, CliLinkState.Absent) { Answer = new CliLinkResult(CliLinkOutcome.Failed, Reason: "Permission denied") };
        harness.AppImage = new McpAppImage("/opt/keypaste.AppImage", link);

        harness.AssertExit(CliApp.ExitInternalError, harness.Run("setup", "--yes", "--client", "claude-code"));

        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains($"keypaste: could not create {link.LinkPath}: Permission denied. Nothing else was changed.", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>A link that answers as the test decided, so nothing is linked on the machine running the tests.</summary>
    private sealed class ScriptedLink(string home, CliLinkState state) : ICliLink
    {
        private CliLinkState _state = state;

        public string LinkPath { get; } = Path.Combine(home, ".local", "bin", "keypaste");

        public string BackupPath => LinkPath + ".keypaste-backup";

        internal CliLinkResult? Answer { get; init; }

        internal bool BackupExists { get; init; }

        internal int Links { get; private set; }

        internal int Replaces { get; private set; }

        public CliLinkStatus Read() =>
            new(_state, LinkPath, LinkedTo: null, BackupPath, BackupExists, DirectoryOnPath: true, Translocated: false);

        public CliLinkResult Link()
        {
            Links++;
            return Change(CliLinkOutcome.Linked);
        }

        public CliLinkResult Replace()
        {
            Replaces++;
            return Change(CliLinkOutcome.Replaced);
        }

        public bool KeepCurrent() => false;

        private CliLinkResult Change(CliLinkOutcome outcome)
        {
            if (Answer is { } answer)
            {
                return answer;
            }

            _state = CliLinkState.ThisCopy;
            return new CliLinkResult(outcome);
        }
    }
}
