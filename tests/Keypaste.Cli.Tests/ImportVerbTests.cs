using System.Security.Cryptography;
using System.Text;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Internal;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary><c>keypaste import</c>: copying another KDBX file in, or keeping it where it is.</summary>
/// <remarks>
/// Every test runs under its own <c>KEYPASTE_HOME</c>, because a copy takes the target's claim and
/// keeping a file in place writes the recent-vaults list.
/// </remarks>
public sealed class ImportVerbTests : IDisposable
{
    private const string _master = "import-master";
    private const string _sourcePassword = "import-source";

    private readonly CliHarness _cli = new();

    public ImportVerbTests()
    {
        _cli.Environment[KeypasteHome.EnvironmentVariable] = Home;
    }

    private string Home => Path.Combine(_cli.Directory, "home");

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Import_CopiesAndReports()
    {
        Seed();
        var source = Source("acme.kdbx");

        _cli.Prompt.Enqueue(_sourcePassword, _master);
        var exit = _cli.Run("import", source, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Password for acme.kdbx: ", "Master password: "], _cli.Prompt.PromptsSeen);
        Assert.Contains("  acme.kdbx · KDBX 4.0 · Argon2d · AES-256", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  ✓ 2 entries · 1 project · copied into acme", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_cli.Out);

        using var vault = Vault.Open(_cli.VaultPath, _master);
        Assert.Equal("bank-pw", vault.Find(new EntryName("acme/Banking", "Bank"))!.Password);
        Assert.Equal("api-pw", vault.Find(new EntryName("env/acme-api", "API_KEY"))!.Password);
    }

    [Fact]
    public void Import_DryRun_WritesNothing()
    {
        Seed();
        var source = Path.Combine(_cli.Directory, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(source, Encoding.UTF8.GetBytes(_sourcePassword), null, "Argon2id", "ChaCha20");
        var before = File.ReadAllBytes(_cli.VaultPath);

        _cli.Prompt.Enqueue(_sourcePassword, _master);
        var exit = _cli.Run("import", source, "--vault", _cli.VaultPath, "--dry-run");

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("foreign.kdbx · KDBX 4.0 · Argon2id · ChaCha20", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("  3 entries in 4 groups", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("→ foreign/Banking", _cli.Out, StringComparison.Ordinal);
        Assert.Matches(@"Recycle Bin +1  skipped", _cli.Out);
        Assert.Matches(@"\.keypaste +1  skipped", _cli.Out);
        Assert.Contains("  nothing was written (--dry-run)", _cli.Out, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_cli.VaultPath));
        Assert.False(Directory.Exists(Path.Combine(Home, VaultClaim.DirectoryName)), "a dry run took the vault's claim");
    }

    [Fact]
    public void Import_NoTargetConfigured_IsInPlace_AndRemembersTheVault()
    {
        var source = Source("acme.kdbx");

        _cli.Prompt.Enqueue(_sourcePassword);
        var exit = _cli.Run("import", source);

        _cli.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Password for acme.kdbx: "], _cli.Prompt.PromptsSeen);
        Assert.Contains("  ✓ 2 entries · 1 project · editing in place", _cli.Err, StringComparison.Ordinal);
        Assert.Contains($"pass --vault {source} or set KEYPASTE_VAULT", _cli.Err, StringComparison.Ordinal);

        var remembered = Assert.Single(RecentVaults.Load(KeypasteHome.RecentPath(Home)));
        Assert.True(PathIdentity.SameFile(source, remembered.Path));
        Assert.Null(remembered.KeyfilePath);
    }

    [Fact]
    public void Import_InPlaceWithInto_IsUsage()
    {
        Seed();
        var source = Source("acme.kdbx");

        Assert.Equal(CliApp.ExitUsageError, _cli.Run("import", source, "--in-place", "--into", "moved", "--vault", _cli.VaultPath));
        Assert.Equal(CliApp.ExitUsageError, _cli.Run("import", source, "--into", "moved"));
        Assert.Empty(_cli.Prompt.PromptsSeen);
    }

    [Fact]
    public void Import_Blocked_Exits1_WritingNothing()
    {
        Seed();
        var source = Source("acme.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "bad-key", GroupPath = "env/acme-api", Password = "x" }));
        var before = File.ReadAllBytes(_cli.VaultPath);

        _cli.Prompt.Enqueue(_sourcePassword, _master);
        var exit = _cli.Run("import", source, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitUsageError, exit);
        Assert.Contains("  ✗ env/acme-api → env/acme-api: 'bad-key' is not a valid environment variable name", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("nothing was written", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_cli.VaultPath));
    }

    [Fact]
    public void Import_WrongSourcePassword_Exits4()
    {
        Seed();
        var source = Source("acme.kdbx");

        _cli.Prompt.Enqueue("not-the-password", _master);
        var exit = _cli.Run("import", source, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitAuthFailed, exit);
        Assert.Contains("keypaste import: wrong password for acme.kdbx", _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Master password: ", _cli.Prompt.PromptsSeen);
    }

    [Fact]
    public void Import_SourceKeyfileNone_IgnoresTheSibling()
    {
        Seed();
        var source = Source("acme.kdbx");
        File.WriteAllBytes(Path.Combine(_cli.Directory, "acme.keyx"), RandomNumberGenerator.GetBytes(64));

        _cli.Prompt.Enqueue(_sourcePassword);
        var withSibling = _cli.Run("import", source, "--vault", _cli.VaultPath);

        _cli.AssertExit(CliApp.ExitAuthFailed, withSibling);
        Assert.Contains("key file found: acme.keyx", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("wrong password or key file for acme.kdbx", _cli.Err, StringComparison.Ordinal);

        _cli.Stderr.GetStringBuilder().Clear();
        _cli.Prompt.Enqueue(_sourcePassword, _master);
        var without = _cli.Run("import", source, "--vault", _cli.VaultPath, "--source-keyfile", "none");

        _cli.AssertExit(CliApp.ExitSuccess, without);
        Assert.DoesNotContain("key file", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WhileTheTargetIsHeld_IsRefused()
    {
        Seed();
        var source = Source("acme.kdbx");
        var before = File.ReadAllBytes(_cli.VaultPath);

        Assert.True(VaultClaim.TryAcquire(Home, _cli.VaultPath, OwnerKind.DesktopApp, out var held, out var refusal), refusal);
        using (held)
        {
            _cli.Prompt.Enqueue(_sourcePassword, _master);
            var exit = _cli.Run("import", source, "--vault", _cli.VaultPath);

            _cli.AssertExit(CliApp.ExitInternalError, exit);
        }

        Assert.Contains("already unlocked in", _cli.Err, StringComparison.Ordinal);
        Assert.Empty(_cli.Prompt.PromptsSeen);
        Assert.Equal(before, File.ReadAllBytes(_cli.VaultPath));
    }

    [Fact]
    public void Import_IntoTheReservedGroup_Exits1()
    {
        Seed();
        var source = Source("acme.kdbx");
        var before = File.ReadAllBytes(_cli.VaultPath);

        _cli.Prompt.Enqueue(_sourcePassword, _master);
        var exit = _cli.Run("import", source, "--vault", _cli.VaultPath, "--into", ".keypaste");

        _cli.AssertExit(CliApp.ExitUsageError, exit);
        Assert.Contains(".keypaste is keypaste's own group; nothing is imported into it", _cli.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_cli.VaultPath));
    }

    [Fact]
    public void Import_NeverPrintsAValue()
    {
        Seed();
        var source = Source("acme.kdbx", vault => vault.AddEntry(new VaultEntry
        {
            Title = "sentinel",
            GroupPath = "Banking",
            Password = SecretHygieneTests.SentinelPassword,
            Username = SecretHygieneTests.SentinelUsername,
            Notes = SecretHygieneTests.SentinelNotes,
            Url = SecretHygieneTests.SentinelUrl,
        }));

        foreach (var shape in new[] { new[] { "--dry-run" }, [], ["--in-place"] })
        {
            _cli.Prompt.Enqueue(_sourcePassword, _master);
            _cli.AssertExit(CliApp.ExitSuccess, _cli.Run(["import", source, "--vault", _cli.VaultPath, .. shape]));
        }

        foreach (var sentinel in new[] { SecretHygieneTests.SentinelPassword, SecretHygieneTests.SentinelUsername, SecretHygieneTests.SentinelNotes, SecretHygieneTests.SentinelUrl, _sourcePassword, _master })
        {
            Assert.DoesNotContain(sentinel, _cli.Out, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, _cli.Err, StringComparison.Ordinal);
        }
    }

    private void Seed()
    {
        _cli.SeedVault(_master);
        _cli.Prompt.PromptsSeen.Clear();
    }

    private string Source(string name, Action<Vault>? more = null)
    {
        var path = Path.Combine(_cli.Directory, name);
        using var vault = Vault.Create(path, _sourcePassword);
        vault.AddEntry(new VaultEntry { Title = "Bank", GroupPath = "Banking", Password = "bank-pw" });
        vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "api-pw" });
        more?.Invoke(vault);
        vault.Save();
        return path;
    }
}
