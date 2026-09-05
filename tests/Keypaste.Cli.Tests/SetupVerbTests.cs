using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What <c>keypaste setup</c> asks each client to do, and what it refuses to do.
/// </summary>
/// <remarks>
/// Every case here runs against <see cref="FakeProcessRunner"/>, so nothing in this file can
/// reach a real client's configuration. The assertions are on the argv keypaste constructs,
/// because that argv <em>is</em> the feature: the command's whole job is turning one vault path
/// into the right invocation for each client that happens to be installed.
/// </remarks>
public sealed class SetupVerbTests
{
    private const string _master = "correct horse battery staple";

    private static CliHarness Wired(params string[] installed)
    {
        var harness = new CliHarness();

        // setup looks for keypaste-mcp beside the running binary, which in a test is the test host.
        // Passing --server-path is how the tests avoid depending on where that happens to be.
        File.WriteAllText(ServerPath(harness), "not a real binary");

        foreach (var executable in installed)
        {
            harness.ProcessRunner.Installed.Add(executable);
        }

        return harness;
    }

    private static bool Adds(string call) => call.Contains("mcp add", StringComparison.Ordinal);

    private static string ServerPath(CliHarness harness) =>
        Path.Combine(harness.Directory, "keypaste-mcp");

    private static int RunSetup(CliHarness harness, params string[] extra)
    {
        string[] baseArgs =
        [
            "setup",
            "--vault", harness.VaultPath,
            "--server-path", ServerPath(harness),
        ];

        return harness.Run([.. baseArgs, .. extra]);
    }

    [Fact]
    public void Claude_code_is_told_the_scope_and_the_transport()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        var call = Assert.Single(harness.ProcessRunner.RealCalls, Adds);
        Assert.Contains("mcp add --scope user --transport stdio keypaste --", call, StringComparison.Ordinal);
        Assert.Contains("--client-label claude-code", call, StringComparison.Ordinal);
        Assert.Contains(harness.VaultPath, call, StringComparison.Ordinal);
    }

    /// <summary>
    /// Codex rejects <c>--scope</c> and <c>--transport</c>; they are Claude Code's alone.
    /// </summary>
    [Fact]
    public void Codex_is_not_told_about_a_scope_it_has_no_concept_of()
    {
        using var harness = Wired("codex");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "codex"));

        var call = Assert.Single(harness.ProcessRunner.RealCalls, Adds);
        Assert.StartsWith("codex mcp add keypaste --", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--scope", call, StringComparison.Ordinal);
        Assert.DoesNotContain("--transport", call, StringComparison.Ordinal);
    }

    /// <summary>
    /// The audit log exists to tell clients apart, so one run wiring two clients must not label
    /// them both the same.
    /// </summary>
    [Fact]
    public void Each_client_is_labelled_as_itself()
    {
        using var harness = Wired("claude", "codex");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness));

        var calls = harness.ProcessRunner.RealCalls.Where(Adds).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c.Contains("--client-label claude-code", StringComparison.Ordinal));
        Assert.Contains(calls, c => c.Contains("--client-label codex", StringComparison.Ordinal));
    }

    [Fact]
    public void An_absent_client_is_reported_and_not_invoked()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness));

        Assert.Contains("codex", harness.Out, StringComparison.Ordinal);
        Assert.Contains("not installed", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.ProcessRunner.RealCalls, c => c.StartsWith("codex", StringComparison.Ordinal));
    }

    [Fact]
    public void Dry_run_prints_the_command_and_runs_nothing()
    {
        using var harness = Wired("claude", "codex");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--dry-run"));

        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains("would run:", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_asks_each_client_to_remove_only_keypaste()
    {
        using var harness = Wired("claude", "codex");

        Assert.Equal(CliApp.ExitSuccess, harness.Run("setup", "--remove"));

        var calls = harness.ProcessRunner.RealCalls;
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c == "claude mcp remove --scope user keypaste");
        Assert.Contains(calls, c => c == "codex mcp remove keypaste");
    }

    /// <summary>
    /// A client with no command of its own is printed, never written. Reporting success for a file
    /// keypaste has not verified any client reads is the failure that costs the most to diagnose.
    /// </summary>
    [Fact]
    public void A_client_without_its_own_command_is_printed_rather_than_written()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code,cursor"));

        Assert.Contains("has no command of its own", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.ProcessRunner.Calls, c => c.StartsWith("cursor", StringComparison.Ordinal));
    }

    [Fact]
    public void No_client_at_all_changes_nothing_and_says_so()
    {
        using var harness = Wired();

        Assert.Equal(CliApp.ExitNotFound, RunSetup(harness));

        Assert.Empty(harness.ProcessRunner.RealCalls);
        Assert.Contains("Nothing was changed", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_client_is_a_usage_error_that_lists_the_known_ones()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitUsageError, RunSetup(harness, "--client", "emacs"));

        Assert.Contains("no client called 'emacs'", harness.Err, StringComparison.Ordinal);
        Assert.Contains("claude-code", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.ProcessRunner.RealCalls);
    }

    /// <summary>
    /// Omitted rather than restated: <c>keypaste-mcp</c> owns the default, so there is one place a
    /// reader can learn what it is.
    /// </summary>
    [Fact]
    public void No_expose_flag_is_passed_unless_one_was_asked_for()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));
        Assert.DoesNotContain(
            "--expose",
            Assert.Single(harness.ProcessRunner.RealCalls, Adds),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_expose_glob_becomes_its_own_flag()
    {
        using var harness = Wired("claude");

        Assert.Equal(
            CliApp.ExitSuccess,
            RunSetup(harness, "--client", "claude-code", "--expose", "env/**,deploy/**"));

        var call = Assert.Single(harness.ProcessRunner.RealCalls, Adds);
        Assert.Contains("--expose env/** --expose deploy/**", call, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wiring before the vault exists is the ordinary case — a reader follows the install page
    /// top to bottom — so it warns and proceeds rather than refusing.
    /// </summary>
    [Fact]
    public void A_vault_that_does_not_exist_yet_warns_but_still_wires()
    {
        using var harness = Wired("claude");

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        Assert.Contains("there is no vault at", harness.Err, StringComparison.Ordinal);
        Assert.Single(harness.ProcessRunner.RealCalls, Adds);
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

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        var calls = harness.ProcessRunner.RealCalls;
        Assert.Equal(2, calls.Count);
        Assert.Equal("claude mcp remove --scope user keypaste", calls[0]);
        Assert.Contains("mcp add", calls[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_that_refuses_is_reported_in_its_own_words()
    {
        using var harness = Wired("claude");
        harness.ProcessRunner.Refuses["claude"] = "that scope is not writable here";

        Assert.Equal(CliApp.ExitSuccess, RunSetup(harness, "--client", "claude-code"));

        Assert.Contains("refused", harness.Out, StringComparison.Ordinal);
        Assert.Contains("that scope is not writable here", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_path_is_made_absolute_because_a_clients_working_directory_is_not_ours()
    {
        using var harness = Wired("claude");
        harness.SeedVault(_master, ("env/demo/KEY", "value"));

        Assert.Equal(
            CliApp.ExitSuccess,
            harness.Run("setup", "--vault", harness.VaultPath, "--server-path", ServerPath(harness)));

        var call = Assert.Single(harness.ProcessRunner.RealCalls, Adds);
        Assert.Contains(Path.GetFullPath(harness.VaultPath), call, StringComparison.Ordinal);
    }
}
