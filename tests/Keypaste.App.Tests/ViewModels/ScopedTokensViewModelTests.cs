using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Agents › Scoped tokens over the session's vault: rows that never carry a secret, a token handed
/// back once and saved, and keypaste's own groups kept off the Entries screen.
/// </summary>
public sealed class ScopedTokensViewModelTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-scoped-tokens-").FullName;
    private readonly ManualClock _clock = new();
    private readonly AppVaultSession _session;

    public ScopedTokensViewModelTests()
    {
        var path = Path.Combine(_directory, "vault.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api/staging", Title = "DATABASE_URL", Password = "db" });
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        _session = new AppVaultSession(_clock, home: _directory);

        using var master = TempVault.Secret(_master);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(path, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Rows_ShowNamePrefixScopeModeExpiry()
    {
        using var screen = new ScopedTokensViewModel(_session);
        Assert.Empty(screen.Rows);

        var (ok, token, _) = screen.Create("ci-staging", "read:acme-api/staging/*, read:acme-api/staging/DATABASE_URL", TimeSpan.FromDays(29), false);
        Assert.True(ok);

        var row = Assert.Single(screen.Rows);
        Assert.True(TokenSecret.TryParse(token!, out var id, out _));
        Assert.Equal("ci-staging", row.Name);
        Assert.Equal($"kpt_{id}…", row.Prefix);
        Assert.Equal("read:acme-api/staging/*, read:acme-api/staging/DATABASE_URL", row.Scope);
        Assert.Equal("inject-only", row.Mode);
        Assert.Equal("29 days", row.Expires);
        Assert.False(row.IsExpired);
        Assert.DoesNotContain(token![13..], row.ToString(), StringComparison.Ordinal);

        _clock.AdvanceWallOnly(TimeSpan.FromDays(29));
        screen.Refresh();
        Assert.Equal("expired", Assert.Single(screen.Rows).Expires);
        Assert.True(screen.Rows[0].IsExpired);
    }

    [Fact]
    public void Create_ReturnsTheTokenOnce_AndSaves()
    {
        using var screen = new ScopedTokensViewModel(_session);

        var (ok, token, message) = screen.Create("ci", "read:acme-api/staging/*", TimeSpan.FromDays(30), false);

        Assert.True(ok, message);
        Assert.NotNull(token);
        Assert.Contains("shown once", message, StringComparison.Ordinal);
        Assert.Equal(TokenCheck.Valid, new TokenStore(_session.Unlocked!).Verify(token, _clock.GetUtcNow(), out _));

        using (var reopened = Vault.Open(_session.VaultPath!, _master))
        {
            Assert.Equal("ci", Assert.Single(new TokenStore(reopened).List()).Name);
        }

        var refused = screen.Create("prod", "read:acme-api/prod/*", TimeSpan.FromDays(1), false);
        Assert.False(refused.Ok);
        Assert.Null(refused.Token);
        Assert.Contains("protected", refused.Message, StringComparison.Ordinal);

        var invalid = screen.Create("bad", "write:acme-api/staging/*", TimeSpan.FromDays(1), false);
        Assert.False(invalid.Ok);
        Assert.Single(screen.Rows);
    }

    [Fact]
    public void Revoke_RemovesTheRow()
    {
        using var screen = new ScopedTokensViewModel(_session);
        var (_, token, _) = screen.Create("ci", "read:acme-api/staging/*", TimeSpan.FromDays(30), false);

        Assert.Null(screen.Revoke(Assert.Single(screen.Rows)));

        Assert.Empty(screen.Rows);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(_session.Unlocked!).Verify(token!, _clock.GetUtcNow(), out _));
        Assert.Empty(_session.Unlocked!.ReadRecycled());
    }

    [Fact]
    public void Revoke_RemovesTheRowsToken_NotOneNamedLikeItsId()
    {
        using var screen = new ScopedTokensViewModel(_session);
        var (_, meantToken, _) = screen.Create("meant", "read:acme-api/staging/*", TimeSpan.FromDays(30), false);
        var meant = Assert.Single(screen.Rows);
        var (_, decoyToken, _) = screen.Create(meant.Id, "read:acme-api/staging/*", TimeSpan.FromDays(30), false);

        Assert.Null(screen.Revoke(meant));

        Assert.Equal(meant.Id, Assert.Single(screen.Rows).Name);
        var store = new TokenStore(_session.Unlocked!);
        Assert.Equal(TokenCheck.Unknown, store.Verify(meantToken!, _clock.GetUtcNow(), out _));
        Assert.Equal(TokenCheck.Valid, store.Verify(decoyToken!, _clock.GetUtcNow(), out _));
    }

    [Fact]
    public void EntriesView_DoesNotListReservedGroups()
    {
        using var screen = new ScopedTokensViewModel(_session);
        Assert.True(screen.Create("ci", "read:acme-api/staging/*", TimeSpan.FromDays(30), false).Ok);

        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
        using var entries = new EntriesViewModel(_session, countdown);

        Assert.DoesNotContain(entries.Rows, row => ReservedGroups.IsReserved(row.GroupPath));
        Assert.DoesNotContain(entries.Groups, group => group.Path.StartsWith(ReservedGroups.Root, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, entries.TotalCount);
        Assert.Contains(entries.Rows, row => row.Title == "github");

        entries.Search = "ci";
        Assert.DoesNotContain(entries.Rows, row => ReservedGroups.IsReserved(row.GroupPath));
    }
}
