using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>A vault unlocked in its own session, with the Items screen over it and, when asked for, the env screen beside it.</summary>
internal sealed class UnlockedScreens : IDisposable
{
    private readonly EnvSetsViewModel? _envSets;

    /// <param name="vaultPath">The vault to unlock.</param>
    /// <param name="master">The vault's master password.</param>
    /// <param name="clock">The session's clock, or null for one at <see cref="AppClock.Start"/>.</param>
    /// <param name="home">The keypaste home whose files the session reads, or null for the process's.</param>
    /// <param name="picker">What Items asks for a file, or null for none.</param>
    /// <param name="envSets">Whether the env screen is built beside Items.</param>
    internal UnlockedScreens(
        string vaultPath,
        string master,
        ManualClock? clock = null,
        string? home = null,
        IVaultFilePicker? picker = null,
        bool envSets = false)
    {
        Clock = clock ?? new ManualClock(AppClock.Start);
        Session = new AppVaultSession(Clock, home: home);

        using (var secret = TempVault.Secret(master))
        {
            Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, secret.Value));
        }

        Countdown = new ClipboardCountdown(Clipboard, new ManualClock(AppClock.Start));
        Entries = new EntriesViewModel(Session, Countdown, picker: picker);

        if (envSets)
        {
            _envSets = new EnvSetsViewModel(Session, Countdown);
        }
    }

    /// <summary>The session's clock.</summary>
    internal ManualClock Clock { get; }

    internal AppVaultSession Session { get; }

    internal Vault Vault => Session.Unlocked!;

    internal FakeClipboard Clipboard { get; } = new();

    internal ClipboardCountdown Countdown { get; }

    internal EntriesViewModel Entries { get; }

    internal EnvSetsViewModel EnvSets => _envSets ?? throw new InvalidOperationException("The env screen is built only with envSets: true.");

    /// <summary>Selects the item <paramref name="name"/> in Items and returns its pane.</summary>
    internal EntryDetailViewModel Open(EntryName name)
    {
        Entries.Selected = Entries.Rows.Single(row => row.Title == name.Title && row.GroupPath == name.GroupPath);
        return Entries.Detail!;
    }

    public void Dispose()
    {
        _envSets?.Dispose();
        Entries.Dispose();
        Countdown.Dispose();
        Session.Dispose();
    }
}
