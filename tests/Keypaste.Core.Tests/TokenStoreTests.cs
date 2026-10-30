using System.Buffers.Text;
using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Tokens in a vault: stored only as a verifier, checked against the file as saved, and revoked for
/// good, through keypaste's merge and KeePassXC's (D-0544).
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

    /// <summary>A copy of the vault taken before a revocation, as a sync service or another computer still holds one.</summary>
    private string OlderPath => Path.Combine(_directory, "older.kdbx");

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
        var entry = Assert.Single(_vault.ReadEntries(includeReserved: true), e => e.GroupPath == ReservedGroups.Tokens);

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
        _vault.SetExpiryUnchecked(Name(info), _now.AddHours(1));
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
        _vault.SetExpiryUnchecked(Name(shortened), _now.AddHours(1));
        _vault.SetExpiryUnchecked(Name(lengthened), _now.AddDays(60));
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

        Assert.True(new TokenStore(_vault).Revoke("ci", _now));
        _vault.Save();

        Assert.Equal(TokenCheck.Unknown, new TokenStore(_vault).Verify(token, _now, out var info));
        Assert.Null(info);
        Assert.False(new TokenStore(_vault).Revoke("ci", _now));
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
    [InlineData("""{"v":1,"name":"ci","scopes":["read:a/dev/*"],"created":"2026-09-01T12:00:00Z","expires":"2027-09-01T12:00:00Z","allow_prod":false,"revoked":"2026-09-01T12:00:00Z"}""")]
    public void Verify_TamperedNotes_IsUnknown(string notes)
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        var entry = _vault.Find(Name(info))!;
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

    /// <summary>KeePassXC's default merge carries the newer version and drops deletions, so a revocation is a version (D-0544, D-0629).</summary>
    [Fact]
    public void Revoke_WritesANewerVersionWithNoVerifier_KeepingTheTokensOneInHistory()
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();
        var live = _vault.Find(Name(info))!;
        var modified = _vault.ReadTimes(Name(info))!.Modified;

        Assert.True(new TokenStore(_vault).Revoke("ci", _now));
        _vault.Save();

        var revoked = _vault.Find(Name(info))!;
        Assert.Equal(("ci", string.Empty), (revoked.Username, revoked.Password));
        Assert.Contains("\"expires\":\"2026-10-01T12:00:00Z\"", revoked.Notes, StringComparison.Ordinal);
        Assert.Contains("\"revoked\":\"2026-09-01T12:00:00Z\"", revoked.Notes, StringComparison.Ordinal);
        Assert.True(_vault.ReadTimes(Name(info))!.Modified > modified);
        var version = Assert.Single(_vault.ReadHistory(Name(info))!);
        Assert.Equal((live.Password, live.Notes), (version.Fields.Password, version.Fields.Notes));
        Assert.Empty(_vault.ReadRecycled());
        Assert.Empty(new TokenStore(_vault).List());
        Assert.Equal(TokenCheck.Unknown, new TokenStore(_vault).Verify(token, _now, out _));
        Assert.True(new TokenStore(_vault).TryCreate("ci", Scopes("read:a/dev/*"), TimeSpan.FromDays(1), false, _now, out _, out _, out var error), error);
    }

    /// <summary>
    /// A record is purged only once its notes' expiry has passed, which an expiry set on the entry in one copy does not change in
    /// another, at the next create or revoke (D-0630).
    /// </summary>
    [Fact]
    public void ARevokedRecord_IsPurgedByTheNextCreateOrRevoke_OnceItsNotesSayTheTokenHasExpired()
    {
        var revoked = Create("revoked", "read:a/dev/*", out _, TimeSpan.FromDays(2));
        var shortened = Create("shortened", "read:a/dev/*", out _, TimeSpan.FromDays(2));
        var other = Create("other", "read:a/dev/*", out _);
        _vault.SetExpiryUnchecked(Name(shortened), _now.AddDays(1));
        var store = new TokenStore(_vault);

        Assert.True(store.Revoke("revoked", _now));
        Assert.True(store.Revoke("shortened", _now.AddDays(1)));
        Assert.Equal(string.Empty, _vault.Find(Name(shortened))!.Password);
        Assert.True(store.Revoke("other", _now.AddDays(2).AddSeconds(-1)));
        Assert.NotNull(_vault.Find(Name(revoked)));
        var tombstones = _vault.TombstoneCount;

        Assert.True(store.TryCreate("next", Scopes("read:a/dev/*"), TimeSpan.FromDays(1), false, _now.AddDays(2), out _, out _, out var error), error);

        Assert.Null(_vault.Find(Name(revoked)));
        Assert.Null(_vault.Find(Name(shortened)));
        Assert.Equal(string.Empty, _vault.Find(Name(other))!.Password);
        Assert.Equal(tombstones + 2, _vault.TombstoneCount);
        Assert.Empty(_vault.ReadRecycled());
        Assert.Equal(["next"], store.List().Select(info => info.Name));
    }

    [Fact]
    public void RevokingAnExpiredToken_PurgesItsRecordAtOnce()
    {
        Assert.True(_vault.RecyclesDeletedEntries);
        var info = Create("ci", "read:a/dev/*", out _, TimeSpan.FromDays(1));
        _vault.Save();
        var tombstones = _vault.TombstoneCount;

        Assert.True(new TokenStore(_vault).Revoke(info.Id, _now.AddDays(1)));
        _vault.Save();

        Assert.Null(_vault.Find(Name(info)));
        Assert.Empty(_vault.ReadRecycled());
        Assert.Equal(tombstones + 1, _vault.TombstoneCount);
    }

    [Fact]
    public void RevokeId_NeverTakesTheIdAsAName()
    {
        var meant = Create("meant", "read:a/dev/*", out var meantToken);
        Create(meant.Id, "read:a/dev/*", out var decoyToken);
        _vault.Save();

        Assert.True(new TokenStore(_vault).RevokeId(meant.Id, _now));
        _vault.Save();

        var store = new TokenStore(_vault);
        Assert.Equal([meant.Id], store.List().Select(info => info.Name));
        Assert.Equal(TokenCheck.Unknown, store.Verify(meantToken, _now, out _));
        Assert.Equal(TokenCheck.Valid, store.Verify(decoyToken, _now, out _));
        Assert.False(store.RevokeId("meant", _now));
    }

    [Fact]
    public void TakeBack_RemovesACreationNoSaveWrote_LeavingNoRecord()
    {
        var info = Create("ci", "read:a/dev/*", out _);

        Assert.True(new TokenStore(_vault).TakeBack(info.Id));

        Assert.Null(_vault.Find(Name(info)));
        Assert.Empty(_vault.ReadRecycled());
        Assert.False(new TokenStore(_vault).TakeBack(info.Id));
    }

    /// <summary>KeePassXC's default merge applies no deletion, so a file it merged can hold a token's record beside its tombstone (D-0544).</summary>
    [Fact]
    public void ARecordBesideItsTombstone_ReadsAsRevoked()
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();
        InFile(VaultPath, database => database.DeletedObjects.Add(new PwDeletedObject(Record(database, info.Id).Uuid, DateTime.UtcNow)));

        using var reopened = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        var store = new TokenStore(reopened);

        Assert.NotNull(reopened.Find(Name(info)));
        Assert.Equal(TokenCheck.Unknown, store.Verify(token, _now, out var verified));
        Assert.Null(verified);
        Assert.Empty(store.List());
        Assert.False(store.Revoke("ci", _now));
    }

    /// <summary>This copy revoked the token and saved, and an older copy still holding it is moved over the file, as a sync service delivers one.</summary>
    [Fact]
    public void ARevocationHere_SurvivesAMergeWithAnOlderCopyOfTheFile()
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();
        File.Copy(VaultPath, OlderPath);
        Assert.True(new TokenStore(_vault).Revoke("ci", _now));
        _vault.Save();
        File.Copy(OlderPath, VaultPath, overwrite: true);

        using var merged = Merged(out var report);

        Assert.True(report.TookFromOpenCopy);
        Assert.Equal(string.Empty, merged.Find(Name(info))!.Password);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token, _now, out _));
        Assert.Empty(new TokenStore(merged).List());
    }

    /// <summary>Another keypaste revoked the token and saved, and this copy, still holding it with a change of its own unsaved, merges that save.</summary>
    [Fact]
    public void ARevocationInTheFile_SurvivesAMergeWithThisCopyStillHoldingTheToken()
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();

        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            Assert.True(new TokenStore(other).Revoke("ci", _now));
            other.Save();
        }

        _vault.AddEntry(new VaultEntry { Title = "unsaved here", Password = "x" });

        using var merged = Merged(out _);

        Assert.NotNull(merged.Find(new EntryName(string.Empty, "unsaved here")));
        Assert.Equal(string.Empty, merged.Find(Name(info))!.Password);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token, _now, out _));
    }

    /// <summary>
    /// A tombstone wins over a token's record whatever the times or changes: one copy deleted the record, as keypaste before 1.4b5
    /// revoked and KeePassXC deletes, and the other changed it afterwards, which would keep an ordinary item in the bin (D-0540, D-0631).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATombstone_WinsOverATokensRecord_ThatTheOtherCopyChanged(bool deletedInFile)
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();

        if (deletedInFile)
        {
            InFile(VaultPath, database =>
            {
                var record = Record(database, info.Id);
                record.ParentGroup.Entries.Remove(record);
                database.DeletedObjects.Add(new PwDeletedObject(record.Uuid, DateTime.UtcNow));
            });
            Assert.True(_vault.UpdateEntry(_vault.Find(Name(info))! with { Url = "https://ci.example" }));
        }
        else
        {
            Assert.Equal(DeletionOutcome.Recycled, _vault.RemoveEntry(Name(info), out var recycled));
            Assert.True(_vault.PurgeRecycled(recycled));
            InFile(VaultPath, database =>
            {
                var record = Record(database, info.Id);
                record.CreateBackup(database);
                record.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://ci.example"));
                record.LastModificationTime = DateTime.UtcNow.AddHours(1);
            });
        }

        using var merged = Merged(out var report);

        Assert.Null(merged.Find(Name(info)));
        Assert.Empty(merged.ReadRecycled());
        Assert.Empty(report.Kept);
        Assert.Equal(1, merged.TombstoneCount);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token, _now, out _));
    }

    /// <summary>
    /// KeePassXC's default merge of an older copy still holding the token, into the file or the file into it: the revoked version is
    /// the newer and current either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AKeePassXcMergeOfAnOlderCopy_KeepsTheRevokedVersionCurrent(bool intoTheFile)
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();
        File.Copy(VaultPath, OlderPath);
        Assert.True(new TokenStore(_vault).Revoke("ci", _now));
        _vault.Save();

        var target = intoTheFile ? VaultPath : OlderPath;
        KeePassXcMerge(target, intoTheFile ? OlderPath : VaultPath);

        using var merged = Vault.Open(target, EnvStoreTests.MasterPassword);
        Assert.Equal(string.Empty, merged.Find(Name(info))!.Password);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token, _now, out _));
        Assert.Empty(new TokenStore(merged).List());
    }

    /// <summary>
    /// A token deleted with only a tombstone, as keypaste before 1.4b5 revoked one, comes back when KeePassXC merges in an older copy,
    /// beside its tombstone; it reads as revoked, and keypaste's merge then removes it (D-0544, D-0631).
    /// </summary>
    [Fact]
    public void ATokenKeePassXcMergesBackBesideItsTombstone_ReadsAsRevoked_AndKeypastesMergeRemovesIt()
    {
        var info = Create("ci", "read:a/dev/*", out var token);
        _vault.Save();
        File.Copy(VaultPath, OlderPath);
        Assert.Equal(DeletionOutcome.Recycled, _vault.RemoveEntry(Name(info), out var recycled));
        Assert.True(_vault.PurgeRecycled(recycled));
        _vault.Save();

        KeePassXcMerge(VaultPath, OlderPath);

        using (var reopened = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            Assert.NotNull(reopened.Find(Name(info)));
            Assert.Equal(TokenCheck.Unknown, new TokenStore(reopened).Verify(token, _now, out _));
        }

        using var merged = Merged(out var report);

        Assert.True(report.TookFromOpenCopy);
        Assert.Null(merged.Find(Name(info)));
        Assert.Equal(1, merged.TombstoneCount);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token, _now, out _));
    }

    [Fact]
    public void List_NeverCarriesTheVerifier()
    {
        Create("ci", "read:a/dev/*,read:b/staging/KEY", out _);
        Create("alpha", "read:a/dev/*", out _);

        var listed = new TokenStore(_vault).List();

        Assert.Equal(["alpha", "ci"], listed.Select(info => info.Name));

        foreach (var entry in _vault.ReadEntries(includeReserved: true).Where(e => e.GroupPath == ReservedGroups.Tokens))
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

    private static EntryName Name(TokenInfo info) => new(ReservedGroups.Tokens, info.Id);

    /// <summary>The open copy merged into a fresh open of the vault's file, as an owner merges another program's save, and saved there.</summary>
    private Vault Merged(out VaultMergeReport report)
    {
        var file = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);

        try
        {
            Assert.Equal(VaultMergeOutcome.Merged, _vault.MergeInto(file, out var merged));
            report = merged!;
            file.Save();
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// KeePassXC's default merge of one closed file into another, as 1.4a read KeePassXC 2.8's <c>Merger.cpp</c>: entries matched by
    /// UUID, the source's version current with the target's in its history when it is the newer, an entry only the source holds added
    /// though the target holds its tombstone, and no deletion applied or carried.
    /// </summary>
    private static void KeePassXcMerge(string target, string source) => InFile(target, into => Open(source, from =>
    {
        foreach (var entry in from.RootGroup.GetEntries(true))
        {
            if (into.RootGroup.FindEntry(entry.Uuid, true) is not { } local)
            {
                (into.RootGroup.FindGroup(entry.ParentGroup.Uuid, true) ?? into.RootGroup).AddEntry(entry.CloneDeep(), true, false);
            }
            else if (Second(entry.LastModificationTime) > Second(local.LastModificationTime))
            {
                local.CreateBackup(into);
                local.AssignProperties(entry, false, false, false);
            }
        }
    }));

    /// <summary>Works on the vault's closed file as another KeePass program would, and saves it.</summary>
    private static void InFile(string path, Action<PwDatabase> work) => Open(path, database =>
    {
        work(database);
        database.Save(null);
    });

    private static void Open(string path, Action<PwDatabase> use)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(EnvStoreTests.MasterPassword), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            use(database);
        }
        finally
        {
            database.Close();
        }
    }

    private static PwEntry Record(PwDatabase database, string id) =>
        database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe(PwDefs.TitleField) == id);

    private static long Second(DateTime time) => time.Ticks / TimeSpan.TicksPerSecond;
}
