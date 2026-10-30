using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The titlebar says whether the file holds what is open, another program's save is merged and named in the notice or,
/// refused, offered for Reload, and the MCP card counts the distinct clients attached to the live session (D-0361, D-0542);
/// Lock gives the window back before the endpoint has stopped.
/// </summary>
public sealed class ShellStatusTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _absent = TimeSpan.FromMilliseconds(300);

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

    /// <summary>
    /// Lock returns to its caller, the window, at once, and completes only once the endpoint has stopped, which waits
    /// here for a prompt that comes down after its withdrawal, as F.35 found the pool can make any delivery wait (D-0392).
    /// </summary>
    [Fact]
    public async Task Lock_GivesTheWindowBackBeforeTheEndpointHasStopped()
    {
        using var fixture = new TempVault();
        fixture.Sees("cc");
        var clock = new ManualClock(AppClock.Start);
        var prompt = new ComesDownWhenTold();
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => prompt);
#pragma warning restore CA2000
        Unlock(authority.Session, fixture);
        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock);
        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);

        await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = new("claude", "1", "cc") }, Token))!.Attached);
        var pending = client.RequestAsync(
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
            Token).AsTask();
        await prompt.Asked.WaitAsync(_connect, Token);

        Task locking;

        // Down on every way out: a prompt left up holds the endpoint's stop, and the authority's disposal behind it, for good.
        try
        {
            var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(() => returned.SetResult(shell.LockCommand.ExecuteAsync()), Token);
            locking = await returned.Task.WaitAsync(_connect, Token);
            await prompt.Withdrawn.WaitAsync(_connect, Token);

            Assert.False(authority.Session.IsUnlocked);
            Assert.False(locking.IsCompleted, "Lock finished before the endpoint could have stopped");
        }
        finally
        {
            prompt.ComeDown();
        }

        await locking.WaitAsync(_connect, Token);
        var reply = await pending.WaitAsync(_connect, Token);
        await using var afterLock = await ApproverClient.TryConnectAsync(serving.Endpoint, _absent, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Null(reply.Value);
        Assert.Null(afterLock);
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

    /// <summary>
    /// The app's edit whose save another program's refused is merged with that save and saved once, and the notice names it
    /// as saved, never by value (D-0542).
    /// </summary>
    [Fact]
    public async Task AnotherProgramsSave_IsMergedWithTheEditItRefused_AndTheNoticeNamesIt()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        var session = authority.Session;
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority, clock: clock);
        var name = Path.GetFileName(fixture.Path_);

        SaveElsewhere(fixture, "ELSEWHERE");
        var write = session.Write(
            vault =>
            {
                vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "MINE", Password = "secret-mine" });
                return true;
            },
            added => added);
        await shell.Merging.WaitAsync(_connect, Token);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        Assert.False(write.Unwritten);
        Assert.False(shell.HasVaultChanged);
        Assert.Equal($"{name} · saved", shell.VaultStatus);
        Assert.Equal(
            $"Another program saved {name}, and keypaste merged it with your changes and saved. Your change to env/app/MINE is saved.",
            shell.Notice);
        Assert.DoesNotContain("secret-", shell.Notice, StringComparison.Ordinal);

        using var file = Vault.Open(fixture.Path_, TempVault.Password);
        Assert.NotNull(file.Find("env/app/ELSEWHERE"));
        Assert.Equal("secret-mine", file.Find("env/app/MINE")!.Password);
    }

    /// <summary>
    /// An item both programs changed is named in the notice, which a later merge adds to and which stays until it is
    /// dismissed, while the item's History holds the other program's version (D-0542).
    /// </summary>
    [Fact]
    public async Task AConflict_StaysInTheNoticeUntilDismissed()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        var session = authority.Session;
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority, clock: clock);
        var name = Path.GetFileName(fixture.Path_);

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            Assert.True(other.UpdateEntry(other.Find("example")! with { Password = "theirs-example" }));
            other.Save();
        }

        var write = session.Write(vault => vault.UpdateEntry(vault.Find("example")! with { Password = "mine-example" }), changed => changed);
        await shell.Merging.WaitAsync(_connect, Token);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        var conflict = $"Another program saved {name}, and keypaste merged it with your changes and saved. Your change to example is saved. " +
            "You and the other program both changed example. Your version is current; the other program's is in its history.";
        Assert.Equal(conflict, shell.Notice);
        Assert.Contains(
            session.Unlocked!.ReadHistory(new EntryName(string.Empty, "example"))!,
            revision => revision.Fields.Password == "theirs-example");

        SaveElsewhere(fixture, "LATER");
        await MergedByThePoll(shell, clock);

        Assert.StartsWith($"{name} · updated ", shell.VaultStatus, StringComparison.Ordinal);
        Assert.Equal(conflict, shell.Notice);

        SaveElsewhere(fixture, "LATEST");
        Assert.Equal(WriteOutcome.Saved, session.Write(vault => vault.UpdateEntry(vault.Find("env/app/LATER")! with { Password = "mine-later" }), changed => changed).Outcome);
        await shell.Merging.WaitAsync(_connect, Token);

        Assert.StartsWith(conflict + " Another program saved", shell.Notice, StringComparison.Ordinal);
        Assert.EndsWith("Your change to env/app/LATER is saved.", shell.Notice, StringComparison.Ordinal);

        shell.DismissNoticeCommand.Execute(null);

        Assert.False(shell.HasNotice);
    }

    /// <summary>
    /// A file that now holds another vault is not merged: the notice says why and offers Reload, the poll does not merge the
    /// same bytes again, and Reload opens what the file holds (D-0543).
    /// </summary>
    [Fact]
    public async Task AFileHoldingAnotherVault_IsNotMerged_AndReloadIsOffered()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        var session = authority.Session;
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority, clock: clock);
        var name = Path.GetFileName(fixture.Path_);
        var merges = 0;
        session.Merged += (_, _) => Interlocked.Increment(ref merges);

        var other = Path.Combine(fixture.Home, "other.kdbx");

        using (var vault = Vault.Create(other, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "OTHER", Password = "o" });
            vault.Save();
        }

        File.Copy(other, fixture.Path_, overwrite: true);
        await MergedByThePoll(shell, clock);

        Assert.Equal(
            $"Another program saved {name}, and keypaste could not merge it: the file now holds another vault. Agents are refused until you reload it.",
            shell.VaultChanged);
        Assert.Equal($"{name} · changed on disk", shell.VaultStatus);
        Assert.Equal(1, merges);

        for (var tick = 0; tick < 5; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await shell.Merging.WaitAsync(_connect, Token);
        Assert.Equal(1, merges);

        shell.ReloadCommand.Execute(null);

        Assert.False(shell.HasVaultChanged);
        Assert.NotNull(session.Unlocked!.Find("env/app/OTHER"));
    }

    /// <summary>An item open for editing when the poll merges another program's save is left as typed, and its save writes into the merged copy (D-0542).</summary>
    [Fact]
    public async Task AnItemOpenForEditing_IsLeftAsTyped_AndItsSaveWritesToTheMergedCopy()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000
        var session = authority.Session;
        Unlock(session, fixture);
        using var shell = new ShellViewModel(session, fixture.Home, authority, clock: clock);
        var name = Path.GetFileName(fixture.Path_);

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == "example");
        var detail = entries.Detail!;
        detail.EditCommand.Execute(null);
        detail.DraftNotes = "typed before the merge";

        SaveElsewhere(fixture, "ELSEWHERE");
        await MergedByThePoll(shell, clock);

        Assert.StartsWith($"{name} · updated ", shell.VaultStatus, StringComparison.Ordinal);
        Assert.Same(entries, shell.Content);
        Assert.Same(detail, entries.Detail);
        Assert.True(detail.IsEditing);
        Assert.Equal("typed before the merge", detail.DraftNotes);

        detail.SaveCommand.Execute(null);
        Assert.False(detail.IsEditing, entries.Error);

        using var file = Vault.Open(fixture.Path_, TempVault.Password);
        Assert.Equal("typed before the merge", file.Find("example")!.Notes);
        Assert.NotNull(file.Find("env/app/ELSEWHERE"));
    }

    /// <summary>Advances the clock a second at a time until the poll or the folder watch has started a merge, then waits for it.</summary>
    private static async Task MergedByThePoll(ShellViewModel shell, ManualClock clock)
    {
        for (var tick = 0; tick < 5 && shell.Merging.IsCompleted; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await shell.Merging.WaitAsync(_connect, Token);
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

    /// <summary>A prompt that comes down only when the test says, however long after its withdrawal.</summary>
    private sealed class ComesDownWhenTold : IApprovalChannel
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _withdrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _down = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Asked => _asked.Task;

        internal Task Withdrawn => _withdrawn.Task;

        internal void ComeDown() => _down.TrySetResult();

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            using var withdrawal = cancellationToken.Register(() => _withdrawn.TrySetResult());
            _asked.TrySetResult();
            await _down.Task.ConfigureAwait(false);
            return ApprovalAnswer.Denied;
        }
    }
}
