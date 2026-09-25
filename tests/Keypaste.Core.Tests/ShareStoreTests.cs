using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The vault's record of share links: metadata and a revoke token, never a key or a value.</summary>
public sealed class ShareStoreTests : IDisposable
{
    private readonly ShareFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static ShareInfo Info(string id, DateTimeOffset? expires = null) => new(
        id,
        "env/acme-api/STRIPE_SECRET_KEY",
        "password",
        "sam@acme.dev",
        new DateTimeOffset(2026, 9, 24, 14, 40, 0, TimeSpan.Zero),
        expires ?? new DateTimeOffset(2026, 9, 25, 14, 40, 0, TimeSpan.Zero),
        1,
        true,
        "https://keypaste.com");

    [Fact]
    public async Task Add_StoresRevokeTokenAndMetadata_NeverTheKeyOrValue()
    {
        string link;
        using (var vault = _fixture.Open())
        {
            var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(passphrase: "a long enough passphrase", to: "sam@acme.dev"), CancellationToken.None);
            Assert.True(outcome.Ok, outcome.Message);
            link = outcome.Link!;
        }

        Assert.True(ShareLink.TryParse(link, out var id, out var key));

        using var reopened = _fixture.Open();
        var record = Assert.Single(reopened.ReadEntries(), e => e.GroupPath == ReservedGroups.Shares);
        Assert.Equal(id, record.Title);
        Assert.Equal("sam@acme.dev", record.Username);
        Assert.Equal("https://keypaste.com", record.Url);
        Assert.Equal(_fixture.Server.Shares[id].RevokeSha256, FakeShareServer.Sha256(record.Password));
        Assert.Contains("\"what\":\"env/acme-api/STRIPE_SECRET_KEY\"", record.Notes, StringComparison.Ordinal);
        Assert.Contains("\"passphrase\":true", record.Notes, StringComparison.Ordinal);

        var info = Assert.Single(new ShareStore(reopened).List());
        Assert.Equal(id, info.Id);
        Assert.Equal("1 view · 24h · passphrase", info.Rule);

        var shareRecords = string.Join("\n", reopened.ReadEntries()
            .Where(e => ReservedGroups.IsReserved(e.GroupPath))
            .Select(e => string.Join("|", e.Title, e.Username, e.Password, e.Url, e.Notes)));
        Assert.DoesNotContain(key, shareRecords, StringComparison.Ordinal);
        Assert.DoesNotContain(ShareFixture.StripeValue, shareRecords, StringComparison.Ordinal);
        Assert.DoesNotContain("a long enough passphrase", shareRecords, StringComparison.Ordinal);
        Assert.DoesNotContain(key, _fixture.EverythingOnDisk(), StringComparison.Ordinal);
        Assert.DoesNotContain(link, _fixture.EverythingOnDisk(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, File.ReadAllText(_fixture.VaultPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_Purges()
    {
        const string id = "Qm9vYmFyQm9vYmFyQm9vYg";
        using (var vault = _fixture.Open())
        {
            var store = new ShareStore(vault);
            store.Add(Info(id), "revoke-token");
            vault.Save();

            Assert.True(store.Remove(id));
            vault.Save();
            Assert.False(store.Remove(id));
        }

        using var reopened = _fixture.Open();
        Assert.Empty(new ShareStore(reopened).List());
        Assert.DoesNotContain(reopened.ReadRecycled(), recycled => recycled.Title == id);
        Assert.DoesNotContain("revoke-token", _fixture.EverythingOnDisk(), StringComparison.Ordinal);
    }

    [Fact]
    public void Find_ByPrefix_Ambiguity()
    {
        using var vault = _fixture.Open();
        var store = new ShareStore(vault);
        store.Add(Info("Qm9vYmFyAAAAAAAAAAAAAA"), "t1");
        store.Add(Info("Qm9vYmFyBBBBBBBBBBBBBA"), "t2");
        store.Add(Info("ZZZZZZZZZZZZZZZZZZZZZA"), "t3");

        Assert.Null(store.Find("Qm9vYmFy", out var ambiguous));
        Assert.True(ambiguous);

        var found = store.Find("Qm9vYmFyB", out ambiguous);
        Assert.False(ambiguous);
        Assert.Equal("Qm9vYmFyBBBBBBBBBBBBBA", found!.Id);
        Assert.True(store.TryGetRevokeToken(found.Id, out var token));
        Assert.Equal("t2", token);

        Assert.Null(store.Find("qm9vYmFyB", out ambiguous));
        Assert.False(ambiguous);
        Assert.Null(store.Find("nothing", out _));
    }

    [Fact]
    public void List_SkipsRecordsItDidNotWrite()
    {
        using var vault = _fixture.Open();
        vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Shares, Title = "not-an-id", Notes = "{}" });
        vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Shares, Title = "Qm9vYmFyQm9vYmFyQm9vYg", Notes = "not json" });
        var store = new ShareStore(vault);
        store.Add(Info("ZZZZZZZZZZZZZZZZZZZZZA"), "t");

        Assert.Equal("ZZZZZZZZZZZZZZZZZZZZZA", Assert.Single(store.List()).Id);
    }

    [Theory]
    [InlineData(1, 60 * 24, "1 view · 24h")]
    [InlineData(3, 60, "3 views · 1h")]
    [InlineData(10, 60 * 24 * 7, "10 views · 7d")]
    [InlineData(1, 60 * 48, "1 view · 2d")]
    [InlineData(1, 90, "1 view · 90m")]
    public void Rule_NamesViewsAndTtl(int views, int minutes, string expected)
    {
        var created = DateTimeOffset.UnixEpoch;
        var info = new ShareInfo("id", "what", "password", null, created, created.AddMinutes(minutes), views, false, "https://keypaste.com");

        Assert.Equal(expected, info.Rule);
    }
}
