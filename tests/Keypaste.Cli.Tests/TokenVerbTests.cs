using System.Text.Json;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste token create|ls|revoke|bundle</c>: the token printed once and alone, nothing else
/// ever printing it or a verifier, and a bundle written whole, audited and never for a protected profile.
/// </summary>
public sealed class TokenVerbTests : IDisposable
{
    internal const string Master = "token-verbs-master";
    internal const string Value = "postgres://token-verb-sentinel@db/app";
    internal const string ProdValue = "postgres://token-verb-prod@db/app";

    private readonly CliHarness _harness = new();

    public TokenVerbTests()
    {
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
        _harness.SeedVault(
            Master,
            ("env/acme-api/staging/DATABASE_URL", Value),
            ("env/acme-api/staging/API_KEY", "sk_token_verb_sentinel"),
            ("env/acme-api/prod/DATABASE_URL", ProdValue));
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private string BundlePath => Path.Combine(_harness.Directory, "b.kpb");

    [Fact]
    public void Create_PrintsTheTokenOnceOnStdout_AndStatusOnStderr()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");

        Assert.True(TokenSecret.TryParse(token, out var id, out _));
        Assert.Equal(token + Environment.NewLine, _harness.Out);
        Assert.DoesNotContain(token, _harness.Err, StringComparison.Ordinal);
        Assert.Contains($"✓ kpt_{id}… · inject-only · shown once", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("scope read:acme-api/staging/* · expires 2026-08-25", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesABadScopeOrTtl_AndProdWithoutAllowProd()
    {
        string[][] cases =
        [
            ["--scope", "write:acme-api/staging/*"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "400d"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "5s"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "99999999d"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "2147483647h"],
            ["--scope", "read:acme-api/staging/*", "--expires", "2147483647m"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "0m"],
            ["--scope", "read:acme-api/staging/*", "--ttl", "1d", "--expires", "1d"],
            ["--scope", "read:acme-api/prod/*"],
        ];

        foreach (var args in cases)
        {
            _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(["token", "create", "ci", .. args, "--vault", _harness.VaultPath]));
        }

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("--allow-prod", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_Json()
    {
        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run(
            "token", "create", "ci-staging", "--scope", "read:acme-api/staging/*", "--expires", "7d", "--json", "--vault", _harness.VaultPath));

        using var json = JsonDocument.Parse(_harness.Out);
        var created = json.RootElement;
        Assert.Equal(JsonValueKind.Object, created.ValueKind);
        Assert.Equal(["name", "token", "prefix", "scopes", "expires"], created.EnumerateObject().Select(property => property.Name));
        var token = created.GetProperty("token").GetString()!;

        Assert.True(TokenSecret.TryParse(token, out var id, out _));
        Assert.Equal("ci-staging", created.GetProperty("name").GetString());
        Assert.Equal($"kpt_{id}…", created.GetProperty("prefix").GetString());
        Assert.Equal(["read:acme-api/staging/*"], created.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.Equal("2026-08-02T15:00:00Z", created.GetProperty("expires").GetString());
        Assert.Single(_harness.Out.Trim().Split('\n'));
        Assert.DoesNotContain(token, _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_ShowsPrefixScopeModeExpiry_NeverTheSecretOrVerifier()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*,read:acme-api/staging/API_KEY");
        Mint(_harness, "short", "read:acme-api/staging/API_KEY", "--ttl", "1h");
        Clear();

        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run("token", "ls", "--vault", _harness.VaultPath));

        var lines = _harness.Out.TrimEnd().Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^  NAME\s+TOKEN\s+SCOPE\s+MODE\s+EXPIRES$", lines[0]);
        Assert.Matches(@"^  ci-staging\s+kpt_[0-9a-f]{8}…\s+read:acme-api/staging/\*, read:acme-api/staging/API_KEY\s+inject-only\s+in 30 days$", lines[1]);
        Assert.Matches(@"^  short\s+kpt_[0-9a-f]{8}…\s+read:acme-api/staging/API_KEY\s+inject-only\s+in 1 hour$", lines[2]);

        AssertNoSecretOrVerifier(token);
    }

    [Fact]
    public void Ls_SaysExpired()
    {
        Mint(_harness, "short", "read:acme-api/staging/*", "--ttl", "1m");
        Clear();
        _harness.Clock.Now = _harness.Clock.Now.AddMinutes(1);

        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run("token", "ls", "--vault", _harness.VaultPath));

        Assert.EndsWith("inject-only  expired", _harness.Out.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_Json()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        Clear();

        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run("token", "ls", "--json", "--vault", _harness.VaultPath));

        using var json = JsonDocument.Parse(_harness.Out);
        var row = Assert.Single(json.RootElement.EnumerateArray().ToList());

        Assert.Equal(
            ["name", "prefix", "scopes", "mode", "created", "expires", "allow_prod", "expired"],
            row.EnumerateObject().Select(property => property.Name));
        Assert.Equal("inject-only", row.GetProperty("mode").GetString());
        Assert.Equal("2026-07-26T15:00:00Z", row.GetProperty("created").GetString());
        Assert.False(row.GetProperty("expired").GetBoolean());
        Assert.False(row.GetProperty("allow_prod").GetBoolean());
        AssertNoSecretOrVerifier(token);
    }

    [Fact]
    public void Revoke()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        Clear();

        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run("token", "revoke", "ci-staging", "--vault", _harness.VaultPath));

        Assert.Contains("✓ revoked ci-staging", _harness.Err, StringComparison.Ordinal);
        using var vault = Vault.Open(_harness.VaultPath, Master);
        Assert.Empty(new TokenStore(vault).List());
        Assert.Equal(TokenCheck.Unknown, new TokenStore(vault).Verify(token, _harness.Clock.Now, out _));
        Assert.Empty(vault.ReadRecycled());
    }

    [Fact]
    public void Revoke_Unknown_Exits3()
    {
        _harness.Prompt.Enqueue(Master);

        _harness.AssertExit(CliApp.ExitNotFound, _harness.Run("token", "revoke", "x", "--vault", _harness.VaultPath));
        Assert.Contains("keypaste token revoke: no token named 'x'", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_WritesTheScopedSets_EncryptedAndAudited()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        Clear();

        _harness.AssertExit(0, Bundle(token));

        var file = File.ReadAllBytes(BundlePath);
        Assert.DoesNotContain(Value, System.Text.Encoding.UTF8.GetString(file), StringComparison.Ordinal);
        Assert.True(TokenBundle.TryOpen(file, token, _harness.Clock.Now, out var contents, out var error), error);
        Assert.Equal([("acme-api", "staging")], contents.Pairs);
        Assert.Contains(new EnvVariable("DATABASE_URL", Value), contents.Resolve(null, null).Variables);

        Assert.Matches(@"✓ wrote b\.kpb · 2 values · encrypted to kpt_[0-9a-f]{8}… · expires 2026-08-25", _harness.Err);
        Assert.DoesNotContain(Value, _harness.Out + _harness.Err, StringComparison.Ordinal);

        var line = Assert.Single(File.ReadAllLines(KeypasteHome.AuditPath(_harness.Directory)));
        Assert.Contains("\"method\":\"token\"", line, StringComparison.Ordinal);
        Assert.Contains("bundled 2 variable(s)", line, StringComparison.Ordinal);
        Assert.DoesNotContain(token[13..], line, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_harness.Directory, "*.kpb*"));
    }

    [Fact]
    public void AnEntryExpirySetElsewhere_BoundsTheBundleAndTheListing()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        var expires = _harness.Clock.Now.AddDays(1);

        using (var vault = Vault.Open(_harness.VaultPath, Master))
        {
            var entry = Assert.Single(vault.ReadEntries(), entry => entry.GroupPath == ReservedGroups.Tokens);
            vault.SetExpiryUnchecked(EntryName.Of(entry), expires);
            vault.Save();
        }

        Clear();
        _harness.AssertExit(0, Bundle(token));

        var file = File.ReadAllBytes(BundlePath);
        Assert.Contains("expires 2026-07-27", _harness.Err, StringComparison.Ordinal);
        Assert.True(TokenBundle.TryOpen(file, token, expires.AddSeconds(-1), out _, out var error), error);
        Assert.False(TokenBundle.TryOpen(file, token, expires, out _, out error));
        Assert.Contains("expired", error, StringComparison.Ordinal);

        Clear();
        _harness.Clock.Now = expires;
        _harness.Prompt.Enqueue(Master);
        _harness.AssertExit(0, _harness.Run("token", "ls", "--json", "--vault", _harness.VaultPath));

        using var json = JsonDocument.Parse(_harness.Out);
        var row = Assert.Single(json.RootElement.EnumerateArray().ToList());
        Assert.Equal("2026-07-27T15:00:00Z", row.GetProperty("expires").GetString());
        Assert.True(row.GetProperty("expired").GetBoolean());
    }

    [Fact]
    public void Bundle_RequiresAVerifiedToken()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        var other = Mint(_harness, "other", "read:acme-api/staging/API_KEY");
        Clear();

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("token", "bundle", "ci-staging", "-o", BundlePath, "--vault", _harness.VaultPath));
        Assert.Contains("KEYPASTE_TOKEN is not set", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitInternalError, Bundle(TokenSecret.New(out _, out _)));
        Assert.Contains("the token is not valid for this vault", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitInternalError, Bundle(other));
        Assert.Contains("that token is not 'ci-staging'", _harness.Err, StringComparison.Ordinal);

        Assert.False(File.Exists(BundlePath));
        Assert.DoesNotContain(token[13..], _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_RefusesToOverwrite()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        File.WriteAllText(BundlePath, "keep me");
        Clear();

        _harness.AssertExit(CliApp.ExitInternalError, Bundle(token));
        Assert.Contains("--force", _harness.Err, StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(BundlePath));

        _harness.AssertExit(0, Bundle(token, "--force"));
        Assert.True(TokenBundle.TryOpen(File.ReadAllBytes(BundlePath), token, _harness.Clock.Now, out _, out _));
    }

    [Fact]
    public void Bundle_NeverReplacesAVault_EvenWithForce()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        var other = Path.Combine(_harness.Directory, "other.kdbx");
        File.Copy(_harness.VaultPath, other);
        var vaultBytes = File.ReadAllBytes(_harness.VaultPath);
        var otherBytes = File.ReadAllBytes(other);
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;

        foreach (var destination in new[] { _harness.VaultPath, other })
        {
            Clear();
            _harness.Prompt.Enqueue(Master);
            _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(
                "token", "bundle", "ci-staging", "-o", destination, "--force", "--vault", _harness.VaultPath));
            Assert.Contains("--force does not lift this", _harness.Err, StringComparison.Ordinal);
        }

        Assert.Equal(vaultBytes, File.ReadAllBytes(_harness.VaultPath));
        Assert.Equal(otherBytes, File.ReadAllBytes(other));
        Assert.False(File.Exists(KeypasteHome.AuditPath(_harness.Directory)));
    }

    [Fact]
    public void Bundle_RefusesAProtectedScope()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*,read:acme-api/prod/*", "--allow-prod");
        Clear();

        _harness.AssertExit(CliApp.ExitInternalError, Bundle(token));

        Assert.Contains(
            "keypaste token bundle: acme-api/prod is protected; a bundle opens without asking anybody, so it cannot carry it",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.False(File.Exists(BundlePath));
        Assert.False(File.Exists(KeypasteHome.AuditPath(_harness.Directory)));
    }

    [Fact]
    public void Bundle_AnUnusableSet_NamesTheKeyAndWritesNothing()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/MISSING");
        Clear();

        _harness.AssertExit(CliApp.ExitInternalError, Bundle(token));

        Assert.Contains("MISSING is not in this profile's set", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(BundlePath));
    }

    [Fact]
    public void Bundle_AuditUnwritable_LeavesNoFile()
    {
        var token = Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        Clear();

        using (new FileStream(KeypasteHome.AuditPath(_harness.Directory) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            _harness.AssertExit(CliApp.ExitInternalError, Bundle(token));
        }

        Assert.Contains("nothing was written", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_harness.Directory, "*.kpb*"));
        Assert.Empty(Directory.GetFiles(_harness.Directory, "*.tmp"));
    }

    [Fact]
    public void CreateAndRevoke_WhileTheVaultIsHeld_AreRefused()
    {
        Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        Clear();
        var before = File.ReadAllBytes(_harness.VaultPath);

        Assert.True(VaultClaim.TryAcquire(_harness.Directory, _harness.VaultPath, OwnerKind.TerminalAgent, out var claim, out var refusal), refusal);

        using (claim)
        {
            _harness.Prompt.Enqueue(Master, Master);
            _harness.AssertExit(CliApp.ExitInternalError, _harness.Run(
                "token", "create", "second", "--scope", "read:acme-api/staging/*", "--vault", _harness.VaultPath));
            _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("token", "revoke", "ci-staging", "--vault", _harness.VaultPath));
        }

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.Out);
        Assert.Contains("Lock it there first", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));
    }

    /// <summary>Mints a token through the CLI and returns it, as <c>&gt; tok.txt</c> would capture it.</summary>
    internal static string Mint(CliHarness harness, string name, string scopes, params string[] extra)
    {
        harness.Prompt.Enqueue(Master);
        harness.Stdout.GetStringBuilder().Clear();
        harness.AssertExit(0, harness.Run(["token", "create", name, "--scope", scopes, .. extra, "--vault", harness.VaultPath]));
        return harness.Out.Trim();
    }

    private int Bundle(string token, params string[] extra)
    {
        _harness.Prompt.Enqueue(Master);
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;
        return _harness.Run(["token", "bundle", "ci-staging", "-o", BundlePath, .. extra, "--vault", _harness.VaultPath]);
    }

    private void Clear()
    {
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.PromptsSeen.Clear();
    }

    private void AssertNoSecretOrVerifier(string token)
    {
        using var vault = Vault.Open(_harness.VaultPath, Master);
        var verifiers = vault.ReadEntries().Where(entry => entry.GroupPath == ReservedGroups.Tokens).Select(entry => entry.Password).ToList();

        Assert.NotEmpty(verifiers);
        Assert.DoesNotContain(token[13..], _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.All(verifiers, verifier => Assert.DoesNotContain(verifier, _harness.Out + _harness.Err, StringComparison.Ordinal));
    }
}
