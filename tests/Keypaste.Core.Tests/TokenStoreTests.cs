using System.Buffers.Text;
using System.Text;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Tokens in a vault: stored only as a verifier, checked against the file as saved, and gone for
/// good when revoked.
/// </summary>
public sealed class TokenStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tokens-").FullName;
    private readonly Vault _vault;

    public TokenStoreTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        _vault.Save();
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Create_StoresOnlyTheVerifier()
    {
        var info = Create("ci", "read:acme-api/staging/*", out var token);
        _vault.Save();

        Assert.True(TokenSecret.TryParse(token, out var id, out var secret));
        var secretText = Base64Url.EncodeToString(secret);
        var entry = Assert.Single(_vault.ReadEntries(), e => e.GroupPath == ReservedGroups.Tokens);

        Assert.Equal(info.Id, entry.Title);
        Assert.Equal("ci", entry.Username);
        Assert.Equal(TokenSecret.Verifier(id, secret), entry.Password);

        foreach (var field in new[] { entry.Title, entry.Username, entry.Password, entry.Url, entry.Notes })
        {
            Assert.DoesNotContain(token, field, StringComparison.Ordinal);
            Assert.DoesNotContain(secretText, field, StringComparison.Ordinal);
        }

        var file = File.ReadAllBytes(VaultPath);
        Assert.Equal(-1, file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secretText)));
        Assert.Equal(-1, file.AsSpan().IndexOf(secret));
    }

    [Fact]
    public void Create_RefusesProtectedWithoutAllowProd()
    {
        var store = new TokenStore(_vault);

        Assert.False(store.TryCreate("ci", Scopes("read:acme-api/prod/*"), TimeSpan.FromDays(1), allowProd: false, _now, out _, out var token, out var error));
        Assert.Null(token);
        Assert.Contains("protected", error, StringComparison.Ordinal);
        Assert.Empty(store.List());

        Assert.True(store.TryCreate("ci", Scopes("read:acme-api/prod/*"), TimeSpan.FromDays(1), allowProd: true, _now, out var info, out _, out error), error);
        Assert.True(info.AllowProd);
    }

    [Fact]
    public void Create_RefusesADuplicateName()
    {
        Create("ci", "read:a/dev/*", out _);

        Assert.False(new TokenStore(_vault).TryCreate("ci", Scopes("read:b/dev/*"), TimeSpan.FromDays(1), false, _now, out _, out _, out var error));
        Assert.Contains("already exists", error, StringComparison.Ordinal);
        Assert.Single(new TokenStore(_vault).List());
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("-lead")]
    [InlineData("has space")]
    [InlineData("")]
    public void Create_RefusesABadName(string name) =>
        Assert.False(new TokenStore(_vault).TryCreate(name, Scopes("read:a/dev/*"), TimeSpan.FromDays(1), false, _now, out _, out _, out _));

    [Fact]
    public void Verify_Valid()
    {
        var info = Create("ci", "read:acme-api/staging/*", out var token);
        _vault.Save();

        Assert.Equal(TokenCheck.Valid, new TokenStore(_vault).Verify(token, _now.AddDays(29), out var verified));
        Assert.NotNull(verified);
        Assert.Equal((info.Id, info.Name, info.Created, info.Expires), (verified.Id, verified.Name, verified.Created, verified.Expires));
        Assert.Equal(info.Scopes, verified.Scopes);
    }

    [Fact]
    public void Verify_Expired_ByNotes_OrByAnEntryExpirySetElsewhere()
    {
        Create("short", "read:a/dev/*", out var shortLived, TimeSpan.FromMinutes(1));
        var info = Create("long", "read:a/dev/*", out var longLived);
        _vault.SetExpiryUnchecked(new EntryName(ReservedGroups.Tokens, info.Id), _now.AddHours(1));
        _vault.Save();

        var store = new TokenStore(_vault);

        Assert.Equal(TokenCheck.Valid, store.Verify(shortLived, _now.AddSeconds(59), out _));
        Assert.Equal(TokenCheck.Expired, store.Verify(shortLived, _now.AddMinutes(1), out _));
        Assert.Equal(TokenCheck.Valid, store.Verify(longLived, _now.AddMinutes(59), out _));
        Assert.Equal(TokenCheck.Expired, store.Verify(longLived, _now.AddHours(1), out var expired));
        Assert.Equal("long", expired!.Name);
    }

    [Fact]
    public void AnEntryExpirySetElsewhere_ShortensTheTokensExpiry_NeverLengthensIt()
    {
        var shortened = Create("shortened", "read:a/dev/*", out var token);
        var lengthened = Create("lengthened", "read:a/dev/*", out _, TimeSpan.FromDays(1));
        _vault.SetExpiryUnchecked(new EntryName(ReservedGroups.Tokens, shortened.Id), _now.AddHours(1));
        _vault.SetExpiryUnchecked(new EntryName(ReservedGroups.Tokens, lengthened.Id), _now.AddDays(60));
        _vault.Save();

        var store = new TokenStore(_vault);

        Assert.Equal([_now.AddDays(1), _now.AddHours(1)], store.List().Select(info => info.Expires));
        Assert.Equal(TokenCheck.Valid, store.Verify(token, _now, out var verified));
        Assert.Equal(_now.AddHours(1), verified!.Expires);
    }

    [Fact]
    public void Verify_ChangedOnDisk_IsNotUnknown()
    {
        Create("ci", "read:a/dev/*", out var token);
        _vault.Save();

        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            other.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            other.Save();
        }

        Assert.Equal(TokenCheck.ChangedOnDisk, new TokenStore(_vault).Verify(token, _now, out var info));
        Assert.Null(info);
    }

    [Fact]
    public void Verify_Unsaved_IsNotUnknown()
    {
        Create("ci", "read:a/dev/*", out var token);

        Assert.Equal(TokenCheck.Unsaved, new TokenStore(_vault).Verify(token, _now, out _));
    }

    [Fact]
    public void Verify_Unknown_AfterRevoke()
    {
        Create("ci", "read:a/dev/*", out var token);
        _vault.Save();

        Assert.True(new TokenStore(_vault).Revoke("ci"));
        _vault.Save();

        Assert.Equal(TokenCheck.Unknown, new TokenStore(_vault).Verify(token, _now, out var info));
        Assert.Null(info);
        Assert.False(new TokenStore(_vault).Revoke("ci"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kpt_nothing")]
    [InlineData("not a token at all, not even close to fifty-six chars!")]
    public void Verify_Malformed(string token) =>
        Assert.Equal(TokenCheck.Malformed, new TokenStore(_vault).Verify(token, _now, out _));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"v":1,"name":"other","scopes":["read:a/dev/*"],"created":"2026-09-01T12:00:00Z","expires":"2027-09-01T12:00:00Z","allow_prod":false}""")]
    [InlineData("""{"v":2,"name":"ci","scopes":["read:a/dev/*"],"created":"2026-09-01T12:00:00Z","expires":"2027-09-01T12:00:00Z","allow_prod":false}""")]
    [InlineData("""{"v":1,"name":"ci","scopes":["read:*/dev/*"],"created":"2026-09-01T12:00:00Z","expires":"2027-09-01T12:00:00Z","allow_prod":false}""")]
    [InlineData("""{"v":1,"name":"ci","name":"ci","scopes":["read:a/dev/*"],"created":"2026-09-01T12:00:00Z","expires":"2027-09-01T12:00:00Z","allow_prod":false}""")]
    public void Verify_TamperedNotes_IsUnknown(string notes)
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        var entry = _vault.Find(new EntryName(ReservedGroups.Tokens, info.Id))!;
        _vault.UpdateEntry(entry with { Notes = notes });
        _vault.Save();

        Assert.Equal(TokenCheck.Unknown, new TokenStore(_vault).Verify(token, _now, out _));
        Assert.Empty(new TokenStore(_vault).List());
    }

    [Fact]
    public void Verify_WrongSecretSameId_IsUnknown()
    {
        var info = Create("ci", "read:a/dev/*", out _);
        _vault.Save();

        var forged = TokenSecret.Prefix + info.Id + "_" + Base64Url.EncodeToString(new byte[32]);

        Assert.Equal(TokenCheck.Unknown, new TokenStore(_vault).Verify(forged, _now, out var verified));
        Assert.Null(verified);
    }

    [Fact]
    public void Revoke_PurgesFromTheRecycleBin()
    {
        Assert.True(_vault.RecyclesDeletedEntries);
        var first = Create("first", "read:a/dev/*", out _);
        Create("second", "read:a/dev/*", out _);
        _vault.Save();

        Assert.True(new TokenStore(_vault).Revoke("second"));
        Assert.True(new TokenStore(_vault).Revoke(first.Id));
        _vault.Save();

        Assert.Empty(new TokenStore(_vault).List());
        Assert.Empty(_vault.ReadRecycled());
    }

    [Fact]
    public void RevokeId_NeverTakesTheIdAsAName()
    {
        var meant = Create("meant", "read:a/dev/*", out var meantToken);
        Create(meant.Id, "read:a/dev/*", out var decoyToken);
        _vault.Save();

        Assert.True(new TokenStore(_vault).RevokeId(meant.Id));
        _vault.Save();

        var store = new TokenStore(_vault);
        Assert.Equal([meant.Id], store.List().Select(info => info.Name));
        Assert.Equal(TokenCheck.Unknown, store.Verify(meantToken, _now, out _));
        Assert.Equal(TokenCheck.Valid, store.Verify(decoyToken, _now, out _));
        Assert.False(store.RevokeId("meant"));
    }

    [Fact]
    public void List_NeverCarriesTheVerifier()
    {
        Create("ci", "read:a/dev/*,read:b/staging/KEY", out _);
        Create("alpha", "read:a/dev/*", out _);

        var listed = new TokenStore(_vault).List();

        Assert.Equal(["alpha", "ci"], listed.Select(info => info.Name));

        foreach (var entry in _vault.ReadEntries().Where(e => e.GroupPath == ReservedGroups.Tokens))
        {
            Assert.All(listed, info => Assert.DoesNotContain(entry.Password, info.ToString(), StringComparison.Ordinal));
            Assert.All(listed, info => Assert.DoesNotContain(entry.Password, info.Prefix, StringComparison.Ordinal));
        }

        Assert.Equal(["read:a/dev/*", "read:b/staging/KEY"], listed[1].Scopes.Select(scope => scope.ToString()));
    }

    private TokenInfo Create(string name, string scopes, out string token, TimeSpan? ttl = null)
    {
        Assert.True(
            new TokenStore(_vault).TryCreate(name, Scopes(scopes), ttl ?? TimeSpan.FromDays(30), false, _now, out var info, out var minted, out var error),
            error);
        token = minted;
        return info;
    }

    private static IReadOnlyList<TokenScope> Scopes(string text)
    {
        Assert.True(TokenScope.TryParseList(text, out var scopes, out var error), error);
        return scopes;
    }
}
