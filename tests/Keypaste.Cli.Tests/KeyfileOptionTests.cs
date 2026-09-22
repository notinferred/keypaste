using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>--keyfile</c> and <c>KEYPASTE_KEYFILE</c>: which verbs take them, what a bad one costs, and
/// where the warning about a fragile one is written.
/// </summary>
/// <remarks>
/// <para>
/// The point of the reach tests is not that eleven verbs each parse a flag. It is that adding a
/// keyfile to a vault must not silently take that vault away from <c>keypaste run</c> and
/// <c>keypaste agent</c> — the env and MCP journeys — while leaving it open in the app. A verb that
/// quietly lost the option would fail nothing else in this suite.
/// </para>
/// <para>
/// <c>keypaste setup</c> deliberately has no keyfile, and
/// <see cref="Setup_takes_no_keyfile_because_the_bridge_it_configures_opens_no_vault"/> holds it
/// that way: setup writes a path into a client's configuration file for <c>keypaste-mcp</c>, which
/// holds no vault at all, so a keyfile there would record where the second factor lives and buy
/// nothing.
/// </para>
/// </remarks>
public sealed class KeyfileOptionTests
{
    private const string _master = "correct horse battery staple";

    /// <summary>Stand-ins the theory replaces with the harness's real paths.</summary>
    private const string _vault = "{--vault}";
    private const string _keyfile = "{--keyfile}";

    /// <summary>
    /// Every verb that opens a vault. The list is the assertion: a verb dropped from it is a verb
    /// somebody's keyfile vault stops working in.
    /// </summary>
    public static TheoryData<string[]> OpeningVerbs
    {
        get
        {
            // Whole command lines, with the two paths marked, because where the options may go is
            // part of what is being asserted: `run` splits at `--` and anything after it belongs to
            // the child, so a keyfile written there would never reach keypaste at all.
            TheoryData<string[]> verbs = [];
            verbs.Add(["ls", _vault, _keyfile]);
            verbs.Add(["get", "env/demo/TOKEN", _vault, _keyfile]);
            verbs.Add(["add", "env/demo/NEW", _vault, _keyfile]);
            verbs.Add(["rm", "env/demo/TOKEN", "--yes", _vault, _keyfile]);
            verbs.Add(["env", "ls", _vault, _keyfile]);
            verbs.Add(["env", "set", "demo", "KEY=value", _vault, _keyfile]);
            verbs.Add(["env", "rm", "demo", "TOKEN", "--yes", _vault, _keyfile]);
            verbs.Add(["env", "export", "demo", "--dotenv", "--stdout", "--yes", _vault, _keyfile]);
            verbs.Add(["run", "demo", _vault, _keyfile, "--", "true"]);
            verbs.Add(["agent", _vault, _keyfile]);
            return verbs;
        }
    }

    [Theory]
    [MemberData(nameof(OpeningVerbs))]
    public void Every_verb_that_opens_a_vault_accepts_a_keyfile(string[] verb)
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master, ("env/demo/TOKEN", "value"));

        var keyfile = Path.Combine(harness.Directory, "absent.keyx");
        var args = verb
            .SelectMany(arg => arg switch
            {
                _vault => (IEnumerable<string>)["--vault", harness.VaultPath],
                _keyfile => ["--keyfile", keyfile],
                _ => [arg],
            })
            .ToArray();

        harness.Prompt.Enqueue(_master, _master, _master);
        var exit = harness.Run(args);

        // The keyfile is missing, so every one of these refuses for that reason rather than for
        // "unknown option" — which is what a verb without the OptionSpec would say, with
        // ExitUsageError. Asserting the refusal rather than success keeps this test independent of
        // what each verb does once it is open.
        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("no keyfile at", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown option", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_takes_no_keyfile_because_the_bridge_it_configures_opens_no_vault()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);

        var exit = harness.Run("setup", "--vault", harness.VaultPath, "--keyfile", "anything.keyx");

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("unknown option '--keyfile'", harness.Err, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // Where the path comes from
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_environment_supplies_a_keyfile_when_the_flag_does_not()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);
        harness.Environment[VaultLocation.KeyfileEnvironmentVariable] =
            Path.Combine(harness.Directory, "from-the-environment.keyx");

        harness.Prompt.Enqueue(_master);
        var exit = harness.Run("ls", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("from-the-environment.keyx", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_wins_over_the_environment()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);
        harness.Environment[VaultLocation.KeyfileEnvironmentVariable] =
            Path.Combine(harness.Directory, "from-the-environment.keyx");

        harness.Prompt.Enqueue(_master);
        var exit = harness.Run(
            "ls", "--vault", harness.VaultPath, "--keyfile", Path.Combine(harness.Directory, "from-the-flag.keyx"));

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("from-the-flag.keyx", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("from-the-environment.keyx", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty variable is unset, so a shell that exports it blank still opens a vault that has no
    /// keyfile — rather than refusing a keyfile called "".
    /// </summary>
    [Fact]
    public void An_empty_environment_variable_is_no_keyfile_at_all()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master, ("env/demo/TOKEN", "value"));
        harness.Environment[VaultLocation.KeyfileEnvironmentVariable] = string.Empty;

        harness.Prompt.Enqueue(_master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("ls", "--vault", harness.VaultPath));
    }

    // ---------------------------------------------------------------------------------------
    // Refusals, before the password is asked for
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_missing_keyfile_is_refused_before_anybody_types_a_password()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);

        // SeedVault typed a password of its own, so the baseline is what it left behind.
        var asked = harness.Prompt.SecretPrompts.Count;

        var exit = harness.Run(
            "ls", "--vault", harness.VaultPath, "--keyfile", Path.Combine(harness.Directory, "absent.keyx"));

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("no keyfile at", harness.Err, StringComparison.Ordinal);

        // Nothing further was asked for. Being made to type a master password and only then told
        // the keyfile was never there wastes the one thing the person had to supply.
        Assert.Equal(asked, harness.Prompt.SecretPrompts.Count);
    }

    [Fact]
    public void An_empty_file_is_refused_as_a_keyfile()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);
        var keyfile = Path.Combine(harness.Directory, "empty.keyx");
        File.WriteAllBytes(keyfile, []);

        Assert.Equal(CliApp.ExitNotFound, harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", keyfile));
        Assert.Contains("is empty", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_vault_is_refused_as_its_own_keyfile()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);

        var exit = harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("is a KeePass vault, not a keyfile", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A vault with no keyfile, opened with one, is a wrong unlock secret — and the message has to
    /// name both factors, or somebody with a good password retypes the one thing that was right.
    /// </summary>
    [Fact]
    public void A_refusal_names_the_keyfile_when_one_was_offered()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);
        var keyfile = Path.Combine(harness.Directory, "unrelated.keyx");
        File.WriteAllBytes(keyfile, new byte[32]);

        harness.Prompt.Enqueue(_master);
        var exit = harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", keyfile);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.Contains("wrong master password or keyfile", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_names_only_the_password_when_no_keyfile_was_offered()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master);

        harness.Prompt.Enqueue("not the password");
        var exit = harness.Run("ls", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.Contains("wrong master password", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("keyfile", harness.Err, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // The warning about the form one edit destroys
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// On stderr, and never on stdout. <c>keypaste get</c> gets piped into other programs and
    /// <c>keypaste run</c> hands stdout to a child, so a sentence about key material on stdout is
    /// somebody's script breaking — the rule the backup-directory notice already follows.
    /// </summary>
    [Fact]
    public void An_arbitrary_file_used_as_a_keyfile_is_reported_on_stderr_alone()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master, ("env/demo/TOKEN", "value"));

        var keyfile = Path.Combine(harness.Directory, "notes.txt");
        File.WriteAllText(keyfile, "an ordinary file somebody picked, which is exactly the problem");

        harness.Prompt.Enqueue(_master);
        harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", keyfile);

        Assert.Contains("locks the vault for good", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("locks the vault for good", harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_keyfile_somebody_actually_made_draws_no_warning()
    {
        using var harness = new CliHarness();
        harness.SeedVault(_master, ("env/demo/TOKEN", "value"));

        var keyfile = Path.Combine(harness.Directory, "real.keyx");
        File.WriteAllBytes(keyfile, new byte[32]);

        harness.Prompt.Enqueue(_master);
        harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", keyfile);

        Assert.DoesNotContain("locks the vault for good", harness.Err, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // What the help says
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_usage_text_names_the_keyfile_option_and_its_variable()
    {
        using var harness = new CliHarness();

        harness.Run("--help");

        Assert.Contains("--keyfile <path>", harness.Out, StringComparison.Ordinal);
        Assert.Contains(VaultLocation.KeyfileEnvironmentVariable, harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void The_agent_usage_names_the_keyfile_option()
    {
        using var harness = new CliHarness();

        harness.Run("agent", "--help");

        Assert.Contains("--keyfile <path>", harness.Out, StringComparison.Ordinal);
    }
}
