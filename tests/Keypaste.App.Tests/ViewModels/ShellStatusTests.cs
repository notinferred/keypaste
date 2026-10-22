using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The titlebar says whether the file holds what is open, and the MCP card counts the distinct
/// clients attached to the live session (D-0361).
/// </summary>
public sealed class ShellStatusTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Titlebar_SaysSavedUnsavedChangedOnDisk()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, home: fixture.Home);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var name = Path.GetFileName(fixture.Path_);

        Assert.Equal($"{name} · saved", shell.VaultStatus);
        Assert.True(shell.VaultStatusOk);
        Assert.StartsWith("Saved ", shell.VaultStatusDetail, StringComparison.Ordinal);

        session.Unlocked!.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "NEW", Password = "x" });
        Assert.Equal($"{name} · unsaved changes", shell.VaultStatus);
        Assert.Equal(StatusTone.Accent, shell.VaultStatusTone);

        session.Unlocked.Save();
        Assert.Equal($"{name} · saved", shell.VaultStatus);

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "ELSEWHERE", Password = "y" });
            other.Save();
        }

        for (var tick = 0; tick < 5; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal($"{name} · changed on disk", shell.VaultStatus);
        Assert.True(shell.VaultStatusDanger);
    }

    [Fact]
    public void AnotherProgramsSave_OffersReload_WhichLoadsTheFileAndDiscardsWhatWasNotWritten()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var name = Path.GetFileName(fixture.Path_);
        Assert.False(shell.HasVaultChanged);

        SaveElsewhere(fixture, "ELSEWHERE");

        for (var tick = 0; tick < 5; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal($"Another program saved {name}. Agents are refused until you reload it.", shell.VaultChanged);

        session.Unlocked!.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "MINE", Password = "z" });
        Assert.Throws<VaultChangedOnDiskException>(session.Unlocked.Save);

        Assert.Equal($"{name} · changed on disk", shell.VaultStatus);
        Assert.EndsWith(" Your change to env/app/MINE was not saved, and reloading discards it.", shell.VaultChanged, StringComparison.Ordinal);

        var before = shell.Content;
        shell.ReloadCommand.Execute(null);

        Assert.False(shell.HasVaultChanged);
        Assert.Equal($"{name} · saved", shell.VaultStatus);
        Assert.NotSame(before, shell.Content);
        var titles = session.Unlocked!.ReadEntries().Select(entry => entry.Title).ToList();
        Assert.Contains("ELSEWHERE", titles);
        Assert.DoesNotContain("MINE", titles);
    }

    /// <summary>The notice names every item a reload would discard, by name and never by value, and a save that wrote them forgets them.</summary>
    [Fact]
    public void TheReloadNotice_NamesEachItemItWouldDiscard()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);

        session.Unlocked!.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "SAVED", Password = "s" });
        session.Unlocked.Save();
        Assert.Empty(session.Unsaved);

        SaveElsewhere(fixture, "ELSEWHERE");
        session.Unlocked.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "FIRST", Password = "secret-first" });
        session.Unlocked.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "SECOND", Password = "secret-second" });

        Assert.EndsWith(" Your changes to env/app/FIRST and env/app/SECOND were not saved, and reloading discards them.", shell.VaultChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-", shell.VaultChanged, StringComparison.Ordinal);

        shell.ReloadCommand.Execute(null);

        Assert.Empty(session.Unsaved);
        Assert.False(shell.HasVaultChanged);
    }

    /// <summary>
    /// Another program's save, written beside the vault and renamed over it, is noticed from the vault's
    /// folder before the five-second poll would have read the file (N.16).
    /// </summary>
    [Fact]
    public async Task AnotherProgramsSave_IsNoticedBeforeThePollReadsTheFile()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var name = Path.GetFileName(fixture.Path_);
        Assert.False(shell.HasVaultChanged);

        SaveElsewhere(fixture, "ELSEWHERE");

        // Short of the fifth status tick, which is when the poll hashes the file.
        var step = TimeSpan.FromMilliseconds(50);
        var advanced = TimeSpan.Zero;

        while (!shell.HasVaultChanged && advanced + step < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(100, Token);
            clock.Advance(step);
            advanced += step;
        }

        Assert.True(shell.HasVaultChanged, "the save was noticed only by the poll, or not at all");
        Assert.Equal($"{name} · changed on disk", shell.VaultStatus);
    }

    [Fact]
    public void AReload_AfterAnotherProgramChangedThePassword_LocksSoTheNewOneIsAsked()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var locked = new List<VaultLockReason>();
        session.Locked += (_, reason) => locked.Add(reason);

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            const string next = "a different horse battery staple";
            Assert.Equal(
                VaultAccessOutcome.Changed,
                other.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), next, next).Outcome);
        }

        shell.ReloadCommand.Execute(null);

        Assert.Equal([VaultLockReason.ReloadRefused], locked);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task TheAgentsRow_CountsDistinctConnectedClients()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock);
        Assert.Equal("Answering agents · none connected", shell.AgentsDetail);

        var endpoint = Assert.IsType<AuthorityStatus.Serving>(authority.Status).Endpoint;
        List<ApproverClient> clients = [];

        foreach (var identity in new AttachClient?[] { new("claude", "1", "cc"), new("claude", "1", "cc"), new("cursor", "1", null), null })
        {
            var client = await ApproverClient.TryConnectAsync(endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = identity }, Token))!.Attached);
            clients.Add(client);
        }

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("Answering agents · 2 connected", shell.AgentsDetail);

        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheAgentsRow_KeepsTheClientCountWhileARequestWaits()
    {
        using var fixture = new TempVault();
        fixture.Sees("cc");
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NeverAnswers());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock);
        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        List<ApproverClient> clients = [];

        foreach (var identity in new AttachClient[] { new("claude", "1", "cc"), new("cursor", "1", null) })
        {
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = identity }, Token))!.Attached);
            clients.Add(client);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = clients[0].RequestAsync(
            new CredentialRequest
            {
                Entry = "example",
                Field = "password",
                Reason = "run the tests",
                TtlSeconds = 60,
                Exposure = ["**"],
                ClientName = "claude",
                ClientLabel = "cc",
                Vault = fixture.Path_,
                Session = serving.Session,
            },
            stop.Token);

        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (authority.Activity.Waiting.Count == 0)
            {
                await Task.Delay(20, deadline.Token);
            }
        }

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("Answering agents · 1 waiting · 2 connected", shell.AgentsDetail);

        await stop.CancelAsync();
        await waiting;

        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>A change waiting for the person's confirmation counts in the sidebar as Agents lists it, under Waiting for you.</summary>
    [Fact]
    public async Task TheAgentsRow_CountsAChangeWaitingForConfirmation()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NeverAnswers());
#pragma warning restore CA2000
        Unlock(authority.Session, fixture);

        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock);
        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, reset: false, out var edit, out var error), error);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var changing = authority.ChangeAccessAsync("claude-code", edit, stop.Token).AsTask();

        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            deadline.CancelAfter(_connect);

            while (authority.Activity.WaitingChanges.Count == 0)
            {
                await Task.Delay(20, deadline.Token);
            }
        }

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("Answering agents · 1 waiting · none connected", shell.AgentsDetail);
        Assert.Equal("1", shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity).Count);

        await stop.CancelAsync();
        await Task.WhenAny(changing, Task.Delay(_connect, Token));
    }

    /// <summary>The idle warning counts the seconds left, rounded up, from the deadline the shell keeps, and is announced once.</summary>
    [Fact]
    public void TheIdleWarning_CountsDownEachSecond_AndIsAnnouncedOnce()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5), home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var announced = 0;
        shell.PropertyChanged += (_, e) => announced += e.PropertyName == nameof(ShellViewModel.CountdownAnnouncement) ? 1 : 0;

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow + TimeSpan.FromMilliseconds(100));
        Assert.Equal("Locking in 30 seconds.", shell.Countdown);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Locking in 29 seconds.", shell.Countdown);

        for (var tick = 0; tick < 28; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal("Locking in 1 second.", shell.Countdown);
        Assert.True(session.IsUnlocked);
        Assert.Equal("Locking in 30 seconds.", shell.CountdownAnnouncement);
        Assert.Equal(1, announced);
    }

    [Fact]
    public void ActivityClearsTheIdleWarning_AndTheNextOneCountsFromItsOwnDeadline()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5), home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow + TimeSpan.FromMilliseconds(100));
        Assert.True(shell.HasCountdown);

        session.Touch();
        shell.ClearCountdown();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(shell.HasCountdown);
        Assert.Equal(string.Empty, shell.CountdownAnnouncement);

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow - TimeSpan.FromSeconds(1));

        Assert.Equal("Locking in 30 seconds.", shell.Countdown);
        Assert.Equal("Locking in 30 seconds.", shell.CountdownAnnouncement);
    }

    /// <summary>A warning the session withdrew for activity this shell did not see goes at its deadline, rather than staying at one second.</summary>
    [Fact]
    public void AWarningTheSessionWithdrew_GoesAtItsDeadline()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5), home: fixture.Home);
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow + TimeSpan.FromMilliseconds(100));
        session.Touch();

        for (var tick = 0; tick < 30; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.False(shell.HasCountdown);
        Assert.True(session.IsUnlocked);
    }

    private static void Unlock(AppVaultSession session, TempVault fixture)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
    }

    private static void SaveElsewhere(TempVault fixture, string title)
    {
        using var other = Vault.Open(fixture.Path_, TempVault.Password);
        other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = title, Password = "y" });
        other.Save();
    }

    private sealed class NeverAnswers : IApprovalChannel
    {
        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return ApprovalAnswer.Denied;
        }

        public async ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return ApprovalAnswer.Denied;
        }
    }

    private sealed class NobodyAnswers : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);
    }
}
