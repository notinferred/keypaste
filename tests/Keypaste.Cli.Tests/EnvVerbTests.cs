using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The <c>env</c> verb group end to end, through the real dispatch and the real vault.
/// </summary>
/// <remarks>
/// Each vault-touching test pays for Argon2 at 64 MiB twice over — once to create the vault and
/// once per command that opens it — so assertions are grouped by behaviour rather than split one
/// per fact. If this class gets slow, cut the number of vault-touching tests, never the KDF.
/// </remarks>
public sealed class EnvVerbTests
{
    internal const string Master = "correct horse battery staple";

    private static readonly EntryName _home = new("env/billing", ".env");

    [Fact]
    public void Set_ThenLs_ShowsTheHomeEntryAndTheKey_ButNeverTheValue()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "postgres://user:pw@localhost/db");
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "billing", "DATABASE_URL", "--vault", harness.VaultPath));
        Assert.Contains("Set DATABASE_URL on env/billing/.env, created and tagged env:billing", harness.Err, StringComparison.Ordinal);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "--vault", harness.VaultPath));
        Assert.Equal(["billing", "  dev", "    env/billing/.env"], Lines(harness.Out));

        Assert.Equal(["  dev", "    env/billing/.env", "      DATABASE_URL"], Ls(harness, "billing"));
        Assert.DoesNotContain("postgres", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// A new key is a protected field of the environment's home entry, tagged into it, which
    /// KeePassXC shows and <c>keypaste get --field</c> reads (D-0413); no entry is made for the key.
    /// </summary>
    [Fact]
    public void Set_StoresANewKeyOnTheHomeEntry_ProtectedAndReadableByGet()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "s3cret-value");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("get", "env/billing/.env", "--field", "TOKEN", "--show", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Equal("s3cret-value", harness.Out.ReplaceLineEndings("\n").Trim(), StringComparer.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(["env:billing"], vault.Tags(_home));
        Assert.True(Assert.Single(vault.Fields(_home)!).IsProtected);
        Assert.Null(vault.Find(new EntryName("env/billing", "TOKEN")));
    }

    [Fact]
    public void Set_WithAnInlineValue_TakesItFromTheArgument()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        // One stdin line only: the master password. The value came from argv.
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "set", "billing", "TOKEN=inline-value", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("inline-value", vault.ReadField(_home, "TOKEN"), StringComparer.Ordinal);
    }

    /// <summary>
    /// The inline form leaks the value into shell history and the process list, and says so — but
    /// only when it actually happened. Warning on the prompted form too would train people to
    /// ignore the line that matters.
    /// </summary>
    [Fact]
    public void Set_WarnsAboutTheCommandLine_OnlyForTheInlineForm()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "INLINE=v", "--vault", harness.VaultPath);
        Assert.Contains("command line", harness.Err, StringComparison.Ordinal);

        // The warning must not carry the thing it is warning about.
        Assert.DoesNotContain("=v", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Out);

        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master, "prompted-value");
        harness.Run("env", "set", "billing", "PROMPTED", "--vault", harness.VaultPath);
        Assert.DoesNotContain("command line", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>A connection string is mostly equals signs; only the first one separates.</summary>
    [Fact]
    public void Set_WithAnInlineValue_SplitsOnTheFirstEqualsOnly()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "CONN=Server=db;Pwd=a=b", "--vault", harness.VaultPath);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("Server=db;Pwd=a=b", vault.ReadField(_home, "CONN"), StringComparer.Ordinal);
    }

    [Fact]
    public void Set_WithAnEmptyInlineValue_StoresAnEmptyValue()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "billing", "OPTIONAL=", "--vault", harness.VaultPath));

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(string.Empty, vault.ReadField(_home, "OPTIONAL"), StringComparer.Ordinal);
    }

    [Fact]
    public void Set_OverAnExistingKey_UpdatesItWhereItLives_AndSaysItKeptTheOldValueInHistory()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master, "first");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);

        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master, "second");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);

        // The retention is stated where it happens, not only in SECURITY.md — a user rotating a
        // leaked credential needs to know the old one is still in the file (DECISIONS.md D-0014).
        Assert.Contains("Updated TOKEN on env/billing/.env (previous value kept in entry history)", harness.Err, StringComparison.Ordinal);

        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master, "second");
        harness.Run("env", "set", "billing", "TOKEN", "--vault", harness.VaultPath);
        Assert.Contains("env/billing/.env already holds that value for TOKEN; nothing was written", harness.Err, StringComparison.Ordinal);

        using var opened = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("second", opened.ReadField(_home, "TOKEN"), StringComparer.Ordinal);
        Assert.Single(opened.ReadHistory(_home)!);
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("api_key")]
    [InlineData("KPXC_X")]
    [InlineData("URL")]
    public void Set_RefusesANewKeyNoProjectReleases(string key)
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "set", "billing", key + "=v", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains(key, harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Empty(vault.ReadEntries());
    }

    [Fact]
    public void Set_WithEntry_WritesANewKeyOnTheTaggedEntryNamed_AndRefusesAnotherOrAMissingOne()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        var stripe = new EntryName("services", "Stripe");
        using (var vault = Vault.Open(harness.VaultPath, Master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "login" });
            vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Other", Password = "login" });
            Assert.True(vault.AddTag(stripe, "env:billing"));
            vault.Save();
        }

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "billing", "WEBHOOK=whsec", "--entry", "services/Stripe", "--vault", harness.VaultPath));
        Assert.Contains("Set WEBHOOK on services/Stripe", harness.Err, StringComparison.Ordinal);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitUsageError, harness.Run("env", "set", "billing", "OTHER_KEY=v", "--entry", "services/Other", "--vault", harness.VaultPath));
        Assert.Contains("services/Other is not in 'billing/dev'; tag it into the environment first", harness.Err, StringComparison.Ordinal);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "set", "billing", "OTHER_KEY=v", "--entry", "services/Absent", "--vault", harness.VaultPath));
        Assert.Contains("no entry 'services/Absent'", harness.Err, StringComparison.Ordinal);

        using var opened = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("whsec", opened.ReadField(stripe, "WEBHOOK"), StringComparer.Ordinal);
        Assert.Null(opened.ReadField(new EntryName("services", "Other"), "OTHER_KEY"));
        Assert.Null(opened.Find(_home));
    }

    [Fact]
    public void Set_AKeyTwoEntriesHold_IsRefusedNamingBoth()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        TagTwoHolders(harness, "STRIPE_KEY");

        var before = Digest(harness.VaultPath);
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitUsageError, harness.Run("env", "set", "billing", "STRIPE_KEY=new", "--vault", harness.VaultPath));

        Assert.Contains("STRIPE_KEY is on more than one entry (services/Stripe, services/Twin)", harness.Err, StringComparison.Ordinal);
        Assert.Equal(before, Digest(harness.VaultPath), StringComparer.Ordinal);
    }

    [Fact]
    public void Rm_RemovesTheField_LeavesTheRest_AndHistoryKeepsIt()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "B=2", "--vault", harness.VaultPath);

        harness.Stderr.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "A", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("Removed A from env/billing/.env (its value stays in the entry's history)", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal(["B"], vault.Fields(_home)!.Select(field => field.Name));
        Assert.Empty(vault.ReadRecycled());
        Assert.True(vault.RestoreRevision(_home, 0));
        Assert.Equal("1", vault.ReadField(_home, "A"), StringComparer.Ordinal);
    }

    /// <summary>An untagged entry under <c>env/</c> is no variable (D-0416), so <c>env rm</c> cannot reach it.</summary>
    [Fact]
    public void Rm_LeavesAnUntaggedEntryUnderEnvAlone()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        Author(harness, ("env/billing", "A", "1"));

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "A", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("no env set for 'billing'", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("1", vault.Find("env/billing/A")?.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// Two entries in one project holding one key. Removing either would be a guess, so the
    /// command refuses, exits nonzero and leaves the file exactly as it found it — which is why
    /// this asserts on the bytes: <see cref="Vault.Save"/> re-randomises salt and nonces, so an
    /// unchanged digest proves no save happened rather than that the contents matched.
    /// </summary>
    [Fact]
    public void Rm_AKeyTwoEntriesHold_IsRefused_AndTheVaultIsNotWritten()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        TagTwoHolders(harness, "TOKEN");

        var before = Digest(harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "TOKEN", "--yes", "--vault", harness.VaultPath);

        Assert.NotEqual(CliApp.ExitSuccess, exit);
        Assert.Contains("TOKEN is on more than one entry (services/Stripe, services/Twin)", harness.Err, StringComparison.Ordinal);
        Assert.Equal(before, Digest(harness.VaultPath), StringComparer.Ordinal);
    }

    [Fact]
    public void Rm_WithoutYes_AndRedirectedStdin_IsAUsageError()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);

        harness.Prompt.Interactive = false;
        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "rm", "billing", "A", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("--yes", harness.Err, StringComparison.Ordinal);

        using var vault = Vault.Open(harness.VaultPath, Master);
        Assert.Equal("1", vault.ReadField(_home, "A"), StringComparer.Ordinal);
    }

    [Fact]
    public void MissingProjectOrKey_ExitsNotFound()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "ls", "nope", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "rm", "nope", "A", "--yes", "--vault", harness.VaultPath));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "rm", "billing", "NOPE", "--yes", "--vault", harness.VaultPath));
    }

    /// <summary>
    /// A project with no variables left is not the same as a project that never existed, and
    /// <c>env ls</c> has to keep telling them apart or a script cannot branch on it.
    /// </summary>
    [Fact]
    public void Ls_OnAProjectWithNoVariablesLeft_SucceedsAndListsNone()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        harness.Run("env", "set", "billing", "A=1", "--vault", harness.VaultPath);
        harness.Prompt.Enqueue(Master);
        harness.Run("env", "rm", "billing", "A", "--yes", "--vault", harness.VaultPath);

        Assert.Equal(["  dev", "    env/billing/.env"], Ls(harness, "billing"));
    }

    [Fact]
    public void Ls_OnAVaultWithNoEnvGroup_SucceedsWithNoOutput()
    {
        using var harness = new CliHarness();
        SeedVault(harness);

        harness.Prompt.Enqueue(Master);
        var exit = harness.Run("env", "ls", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.Out);
    }

    [Theory]
    [InlineData("env")]
    [InlineData("env", "bogus")]
    [InlineData("env", "set", "billing")]
    [InlineData("env", "set", "billing", "A", "B")]
    [InlineData("env", "set", "billing", "=novalue")]
    [InlineData("env", "rm", "billing")]
    [InlineData("env", "ls", "a", "b")]
    [InlineData("env", "tag", "billing")]
    [InlineData("env", "tag", "bill:ing", "api/Stripe")]
    [InlineData("env", "tag", "billing", "api/Stripe", "-p", "Prod")]
    [InlineData("env", "untag", "billing", "api/Stripe", "extra")]
    [InlineData("env", "pull")]
    [InlineData("env", "pull", "a", "b", "c")]
    [InlineData("env", "pull", "a", "--keep", "--delete-source")]
    [InlineData("env", "export")]
    [InlineData("env", "export", "a")]
    [InlineData("env", "export", "a", "b", "c", "--dotenv")]
    [InlineData("env", "export", "a", "out.env", "--dotenv", "--stdout")]
    [InlineData("env", "export", "a", "--dotenv", "--stdout", "--force")]
    public void MalformedInvocations_AreUsageErrors_OnStderr(params string[] args)
    {
        using var harness = new CliHarness();

        var exit = harness.Run(args);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.NotEmpty(harness.Err);
        Assert.Empty(harness.Out);
    }

    [Theory]
    [InlineData("env", "--help")]
    [InlineData("env", "-h")]
    [InlineData("env", "help")]
    [InlineData("env", "ls", "--help")]
    [InlineData("env", "set", "--help")]
    [InlineData("env", "rm", "--help")]
    [InlineData("env", "pull", "--help")]
    [InlineData("env", "export", "--help")]
    [InlineData("env", "diff", "--help")]
    [InlineData("env", "tag", "--help")]
    [InlineData("env", "untag", "--help")]
    public void Help_GoesToStdout_AndExitsZero(params string[] args)
    {
        using var harness = new CliHarness();

        var exit = harness.Run(args);

        Assert.Equal(CliApp.ExitSuccess, exit);
        Assert.Contains("usage: keypaste env", harness.Out, StringComparison.Ordinal);
        Assert.Empty(harness.Err);
    }

    /// <summary>
    /// The group listing has to stay usable in a terminal that is not UTF-8, exactly as
    /// <c>keypaste ls</c> does — this is what stops someone reaching for box-drawing characters.
    /// </summary>
    [Fact]
    public void EnvUsage_IsAscii()
    {
        using var harness = new CliHarness();
        harness.Run("env", "--help");

        Assert.All(harness.Out, c => Assert.True(c < 128, $"non-ASCII character '{c}' in env usage"));
    }

    [Fact]
    public void Profiles_SetLsRmAndPull_WorkOnTheNamedProfileOnly()
    {
        using var harness = new CliHarness();
        SeedVault(harness);
        var dev = new EntryName("env/acme-api", ".env");
        var staging = new EntryName("env/acme-api", ".env.staging");

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "acme-api", "DATABASE_URL=dev-db", "--vault", harness.VaultPath));
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "set", "acme-api", "DATABASE_URL=staging-db", "-p", "staging", "--vault", harness.VaultPath));
        Assert.Contains("Set DATABASE_URL on env/acme-api/.env.staging, created and tagged env:acme-api:staging", harness.Err, StringComparison.Ordinal);

        var file = Path.Combine(harness.Directory, "staging.env");
        File.WriteAllText(file, "PULLED=pulled-value\n");
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "pull", "acme-api", file, "-p", "staging", "--yes", "--keep", "--vault", harness.VaultPath));
        Assert.Contains("PULLED on env/acme-api/.env.staging", harness.Err, StringComparison.Ordinal);
        Assert.Contains("into acme-api/staging", harness.Err, StringComparison.Ordinal);

        Assert.Equal(["  staging", "    env/acme-api/.env.staging", "      DATABASE_URL", "      PULLED"], Ls(harness, "acme-api", "-p", "staging"));
        Assert.Equal(["  dev", "    env/acme-api/.env", "      DATABASE_URL"], Ls(harness, "acme-api", "-p", "dev"));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "rm", "acme-api", "DATABASE_URL", "-p", "staging", "--yes", "--vault", harness.VaultPath));

        using (var vault = Vault.Open(harness.VaultPath, Master))
        {
            Assert.Equal("dev-db", vault.ReadField(dev, "DATABASE_URL"));
            Assert.Null(vault.ReadField(staging, "DATABASE_URL"));
            Assert.Equal("pulled-value", vault.ReadField(staging, "PULLED"));
            Assert.Equal(["env:acme-api:staging"], vault.Tags(staging));
            Assert.DoesNotContain("env/acme-api/staging", vault.ReadGroupPaths());
        }

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "rm", "acme-api", "PULLED", "-p", "qa", "--yes", "--vault", harness.VaultPath));
        Assert.Contains("'acme-api' has no 'qa' profile", harness.Err, StringComparison.Ordinal);

        Assert.Equal(CliApp.ExitUsageError, harness.Run("env", "set", "acme-api", "X=y", "-p", "Staging", "--vault", harness.VaultPath));
        Assert.Contains("is not a profile name", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvLs_Json()
    {
        using var harness = new CliHarness();
        SeedProfiles(harness);
        Author(harness, ("env/acme-api/staging", "OLD_KEY", "x"));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "--json", "--vault", harness.VaultPath));
        Assert.Equal(
            """[{"project":"acme-api","environments":[{"name":"dev","protected":false,"members":[{"path":"env/acme-api/.env","group":"env/acme-api","title":".env"}]},{"name":"staging","protected":false,"members":[{"path":"env/acme-api/.env.staging","group":"env/acme-api","title":".env.staging"}]},{"name":"prod","protected":true,"members":[{"path":"env/acme-api/.env.prod","group":"env/acme-api","title":".env.prod"}]}]}]""",
            harness.Out.Trim());

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "acme-api", "-p", "staging", "--json", "--vault", harness.VaultPath));
        Assert.Equal(
            """[{"project":"acme-api","environments":[{"name":"staging","protected":false,"members":[{"path":"env/acme-api/.env.staging","group":"env/acme-api","title":".env.staging","keys":["DATABASE_URL"]}]}]}]""",
            harness.Out.Trim());
        Assert.DoesNotContain("staging-db", harness.Out + harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvLs_Profiles()
    {
        using var harness = new CliHarness();
        SeedProfiles(harness);
        Author(harness, ("env/acme-api/qa", "LOST", "x"));

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "ls", "acme-api", "--profiles", "--vault", harness.VaultPath));
        Assert.Equal(["dev", "staging", "prod"], Lines(harness.Out));
        Assert.Empty(harness.Err);

        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitNotFound, harness.Run("env", "ls", "acme-api", "-p", "qa", "--vault", harness.VaultPath));
        Assert.Equal("keypaste env ls: 'acme-api' has no 'qa' environment", harness.Err.Trim());
    }

    [Fact]
    public void EnvDiff_Output()
    {
        using var harness = new CliHarness();
        SeedProfiles(harness);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "diff", "acme-api", "dev", "prod", "--vault", harness.VaultPath));
        Assert.Equal(
            [
                "  - DATABASE_URL      missing in prod",
                "  - JWT_SIGNING_KEY   missing in dev",
                "  - SENTRY_DSN        missing in dev",
                "  = STRIPE_KEY        same value in dev and prod",
            ],
            Lines(harness.Out));

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "diff", "acme-api", "--vault", harness.VaultPath));
        Assert.Equal(
            [
                "  dev · staging",
                "  - STRIPE_KEY        missing in staging",
                "  dev · prod",
                "  - DATABASE_URL      missing in prod",
                "  - JWT_SIGNING_KEY   missing in dev",
                "  - SENTRY_DSN        missing in dev",
                "  = STRIPE_KEY        same value in dev and prod",
            ],
            Lines(harness.Out));

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "diff", "acme-api", "staging", "staging", "--vault", harness.VaultPath));
        Assert.Equal(["  ✓ staging and staging have the same keys"], Lines(harness.Out));
        Assert.DoesNotContain("-db", harness.Out + harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvDiff_InfersTheProject()
    {
        using var harness = new CliHarness();
        SeedProfiles(harness);
        var project = MapProject(harness, "acme-api");
        harness.WorkingDirectory = Path.Combine(project, "src");
        Directory.CreateDirectory(harness.WorkingDirectory);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run("env", "diff", "staging", "prod", "--vault", harness.VaultPath));
        Assert.Contains("  - SENTRY_DSN        missing in staging", Lines(harness.Out));

        harness.WorkingDirectory = harness.Directory;
        Assert.Equal(CliApp.ExitUsageError, harness.Run("env", "diff", "--vault", harness.VaultPath));
        Assert.Contains("this directory is not mapped to a project; name a project", harness.Err, StringComparison.Ordinal);
    }

    /// <summary>Maps a fresh directory to a project in <c>projects.json</c> under a home inside the harness.</summary>
    /// <returns>The mapped directory.</returns>
    internal static string MapProject(CliHarness harness, string project)
    {
        var home = Path.Combine(harness.Directory, "home");
        var directory = Path.Combine(harness.Directory, "work", project);
        Directory.CreateDirectory(directory);

        harness.Environment[Core.Audit.KeypasteHome.EnvironmentVariable] = home;
        Assert.True(Core.Projects.ProjectMappings.Save(
            Core.Audit.KeypasteHome.ProjectsPath(home),
            [new Core.Projects.ProjectMapping(Path.GetFullPath(harness.VaultPath), project, directory, "npm start")]));

        return directory;
    }

    /// <summary>
    /// acme-api's dev holds DATABASE_URL and STRIPE_KEY, staging DATABASE_URL, prod JWT_SIGNING_KEY,
    /// SENTRY_DSN and the same STRIPE_KEY as dev.
    /// </summary>
    private static void SeedProfiles(CliHarness harness)
    {
        SeedVault(harness);

        using var vault = Vault.Open(harness.VaultPath, Master);
        ProjectVariables.Set(vault, "acme-api", "DATABASE_URL", "dev-db");
        ProjectVariables.Set(vault, "acme-api", "STRIPE_KEY", "shared-stripe");
        ProjectVariables.Set(vault, "acme-api", "staging", "DATABASE_URL", "staging-db");
        ProjectVariables.Set(vault, "acme-api", "prod", "JWT_SIGNING_KEY", "prod-jwt");
        ProjectVariables.Set(vault, "acme-api", "prod", "SENTRY_DSN", "prod-sentry");
        ProjectVariables.Set(vault, "acme-api", "prod", "STRIPE_KEY", "shared-stripe");
        vault.Save();
    }

    private static List<string> Ls(CliHarness harness, params string[] args)
    {
        harness.Stdout.GetStringBuilder().Clear();
        harness.Prompt.Enqueue(Master);
        Assert.Equal(CliApp.ExitSuccess, harness.Run([.. new[] { "env", "ls" }, .. args, "--vault", harness.VaultPath]));
        return Lines(harness.Out);
    }

    private static List<string> Lines(string text) =>
        [.. text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Writes untagged entries through the core directly, the way KeePassXC would.</summary>
    private static void Author(CliHarness harness, params (string GroupPath, string Title, string Password)[] entries)
    {
        using var vault = Vault.Open(harness.VaultPath, Master);

        foreach (var entry in entries)
        {
            vault.AddEntry(new VaultEntry
            {
                Title = entry.Title,
                Password = entry.Password,
                GroupPath = entry.GroupPath,
            });
        }

        vault.Save();
    }

    /// <summary>Tags <c>services/Stripe</c> and <c>services/Twin</c> into billing, each holding <paramref name="key"/>.</summary>
    private static void TagTwoHolders(CliHarness harness, string key)
    {
        using var vault = Vault.Open(harness.VaultPath, Master);

        foreach (var title in new[] { "Stripe", "Twin" })
        {
            var name = new EntryName("services", title);
            vault.AddEntry(new VaultEntry { GroupPath = "services", Title = title, Password = "login" });
            Assert.True(vault.SetFields(name, [new FieldWrite(key, "old-" + title)]));
            Assert.True(vault.AddTag(name, "env:billing"));
        }

        vault.Save();
    }

    private static string Digest(string path)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void SeedVault(CliHarness harness)
    {
        harness.Prompt.Interactive = true;
        harness.Prompt.Enqueue(Master, Master);
        harness.Run("init", harness.VaultPath);

        harness.Stdout.GetStringBuilder().Clear();
        harness.Stderr.GetStringBuilder().Clear();
    }
}
