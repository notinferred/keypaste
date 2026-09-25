using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>The detail pane's Rotate: it asks first, replaces the password, keeps the old one in history and says when.</summary>
public sealed class EntryRotateTests : IDisposable
{
    private const string _old = "ROTATE-PANE-OLD-8b1f";

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session = new(new ManualClock());
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock());
    private readonly EntriesViewModel _entries;

    public EntryRotateTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "token", Password = _old });
            vault.Save();
        }

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
        }

        _entries = new EntriesViewModel(_session, _countdown);
        _entries.Selected = _entries.Rows.Single(row => row.Path == "api/token");
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Rotate_AsksFirst_ThenReplaces_AndUpdatesRotated()
    {
        var detail = _entries.Detail!;
        Assert.NotEmpty(detail.Created);
        Assert.Equal(detail.Created, detail.Rotated);

        detail.RotateCommand.Execute(null);

        Assert.True(detail.IsConfirmingRotate);
        Assert.Contains("20-character one? keypaste only changes its copy", detail.RotatePrompt, StringComparison.Ordinal);
        Assert.Equal(_old, _session.Unlocked!.Find(new EntryName("api", "token"))!.Password);

        detail.ConfirmRotateCommand.Execute(null);

        Assert.False(detail.IsConfirmingRotate);
        var rotated = _session.Unlocked.Find(new EntryName("api", "token"))!.Password;
        Assert.NotEqual(_old, rotated);
        Assert.Equal(20, rotated.Length);
        Assert.Contains(_session.Unlocked.ReadHistory(new EntryName("api", "token"))!, revision => revision.Fields.Password == _old);
        Assert.Equal(VaultSaveStatus.Saved, _session.Unlocked.SaveState().Status);
        Assert.EndsWith("today", detail.Rotated, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_Cancel_ChangesNothing()
    {
        var detail = _entries.Detail!;

        detail.RotateCommand.Execute(null);
        detail.CancelRotateCommand.Execute(null);

        Assert.False(detail.IsConfirmingRotate);
        Assert.Equal(_old, _session.Unlocked!.Find(new EntryName("api", "token"))!.Password);
    }

    [Fact]
    public void Rotate_ChangedOnDisk_Reports()
    {
        using (var other = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            other.Save();
        }

        var detail = _entries.Detail!;
        detail.RotateCommand.Execute(null);
        detail.ConfirmRotateCommand.Execute(null);

        Assert.Contains("Something else changed this vault", _entries.Error, StringComparison.Ordinal);
    }
}
