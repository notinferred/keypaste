using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Two vaults open in one app at once: each its own session, claim and pipe, one prompt in front of the person at a time
/// whichever vault asks, rules kept apart, one browser pairing for both, and every person's lock locking both.
/// </summary>
public sealed class OpenVaultsTests : IDisposable
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _absent = TimeSpan.FromMilliseconds(300);

    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly Dictionary<AppVaultSession, HeldPrompt> _prompts = [];
    private readonly OpenVaults _vaults;
    private readonly OpenVault _personal;
    private readonly OpenVault _acme;

    public OpenVaultsTests()
    {
        using (var acme = Vault.Create(AcmePath, TempVault.Password))
        {
            acme.AddEntry(new VaultEntry { Title = "example", Password = "acme-secret-5Rt8Lw2" });
            acme.Save();
            TempVault.Sees(acme, TempVault.Agent);
        }

        _fixture.Sees();

        // The open vaults own every session and authority made here.
#pragma warning disable CA2000
        _vaults = new OpenVaults(
            new AppVaultSession(_clock, home: _fixture.Home),
            () => new AppVaultSession(_clock, home: _fixture.Home),
            (vaults, session) =>
            {
                var prompt = new HeldPrompt();
                _prompts[session] = prompt;
                return new AppAuthority(session, approverOverride: null, () => prompt, turns: vaults.Turns, otherKeys: () => vaults.KeysBesides(session), pairings: vaults.Pairings);
            });
#pragma warning restore CA2000
        _personal = _vaults.InView;
        _acme = _vaults.Add();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string AcmePath => Path.Combine(_fixture.Home, "acme.kdbx");

    public void Dispose()
    {
        _vaults.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Both_vaults_unlock_at_once_each_with_its_own_claim_session_and_pipe()
    {
        UnlockBoth();

        var personal = Assert.IsType<AuthorityStatus.Serving>(_personal.Authority.Status);
        var acme = Assert.IsType<AuthorityStatus.Serving>(_acme.Authority.Status);
        Assert.NotEqual(personal.Endpoint, acme.Endpoint);
        Assert.NotEqual(personal.Session, acme.Session);
        Assert.Equal(ApproverEndpoint.Prefix + _personal.Session.Identity!.Key, personal.Endpoint);
        Assert.Equal(ApproverEndpoint.Prefix + _acme.Session.Identity!.Key, acme.Endpoint);
        Assert.Equal([_fixture.Path_, AcmePath], LocalAccount.Vaults(_fixture.Home));
    }

    /// <summary>A place opened by its file names that vault while locked, for the sidebar and a page's list, and gives way to another session that opens the file.</summary>
    [Fact]
    public void A_locked_place_names_its_vault_and_gives_way_to_the_session_that_opens_its_file()
    {
        Unlock(_personal.Session, _fixture.Path_);
        _vaults.Close(_acme);
        var acme = _vaults.Add(AcmePath);

        Assert.Same(acme, _vaults.Add(AcmePath));
        Assert.Equal(AcmePath, _vaults.PathOf(acme.Session));
        Assert.Same(acme, _vaults.Named("acme"));
        Assert.Equal(["acme"], _vaults.Pairings.Locked());

        var other = _vaults.Add();
        Unlock(other.Session, AcmePath);

        Assert.DoesNotContain(acme, _vaults.Listed);
        Assert.Same(other, _vaults.Holding(AcmePath));
        Assert.Empty(_vaults.Pairings.Locked());
    }

    /// <summary>A sync tool's save of the second vault is merged by that vault's own watch within the poll, said in the title bar while it is in view, and leaves the first alone.</summary>
    [Fact]
    public async Task Another_programs_save_of_the_second_vault_is_merged_within_five_seconds_and_the_first_is_untouched()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, _personal.Authority, clock: _clock) { Vaults = _vaults };
        shell.SelectedSidebarRow = shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Vault?.Title == "acme" && row.Place == ItemPlace.All);
        var personalSaved = _personal.Session.Unlocked!.SaveState().SavedAt;
        Assert.True(_acme.Session.Merges);

        using (var other = Vault.Open(AcmePath, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { Title = "synced", Password = "from-another-device" });
            other.Save();
        }

        for (var tick = 0; tick < 5 && _acme.Watch.Merging.IsCompleted; tick++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.False(_acme.Watch.Merging.IsCompleted, "the second vault's watch did not start a merge within five seconds");
        await _acme.Watch.Merging.WaitAsync(_connect, Token);

        Assert.NotNull(_acme.Session.Unlocked!.Find(new EntryName(string.Empty, "synced")));
        Assert.Contains("synced", Assert.IsType<EntriesViewModel>(shell.Content).Rows.Select(row => row.Title));
        Assert.StartsWith("updated ", shell.VaultSaveWords, StringComparison.Ordinal);
        Assert.Contains(AcmePath, shell.VaultStatusDetail, StringComparison.Ordinal);
        Assert.Equal(string.Empty, shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme").SaveMark);

        Assert.Null(_personal.Session.Unlocked!.Find(new EntryName(string.Empty, "synced")));
        Assert.Equal(VaultSaveStatus.Saved, _personal.Session.Unlocked!.SaveState().Status);
        Assert.Equal(personalSaved, _personal.Session.Unlocked!.SaveState().SavedAt);
        Assert.True(_personal.Watch.Merging.IsCompleted);
    }

    /// <summary>Your change to the second vault meeting another program's change to the same item is merged and saved there, the notice naming that vault.</summary>
    [Fact]
    public async Task A_conflict_on_the_second_vault_is_merged_there_and_the_notice_names_it()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, _personal.Authority, clock: _clock) { Vaults = _vaults };

        using (var other = Vault.Open(AcmePath, TempVault.Password))
        {
            Assert.True(other.UpdateEntry(other.Find("example")! with { Password = "theirs-example" }));
            other.Save();
        }

        var write = _acme.Session.Write(vault => vault.UpdateEntry(vault.Find("example")! with { Password = "mine-example" }), changed => changed);
        await _acme.Watch.Merging.WaitAsync(_connect, Token);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        Assert.Equal(
            "Another program saved acme.kdbx, and keypaste merged it with your changes and saved. Your change to example is saved. " +
            "You and the other program both changed example. Your version is current; the other program's is in its history.",
            shell.Notice);
        Assert.Contains(_acme.Session.Unlocked!.ReadHistory(new EntryName(string.Empty, "example"))!, revision => revision.Fields.Password == "theirs-example");

        Assert.DoesNotContain(_personal.Session.Unlocked!.ReadHistory(new EntryName(string.Empty, "example"))!, revision => revision.Fields.Password == "theirs-example");
        Assert.Equal(VaultSaveStatus.Saved, _personal.Session.Unlocked!.SaveState().Status);
    }

    [Fact]
    public void A_vault_another_place_holds_is_refused()
    {
        Unlock(_personal.Session, _fixture.Path_);
        var third = _vaults.Add();

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.HeldElsewhere, third.Session.TryUnlock(_fixture.Path_, master.Value));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("minimized")]
    [InlineData("closed")]
    public void A_persons_lock_of_either_vault_locks_both(string kind)
    {
        UnlockBoth();

        _acme.Session.Lock(kind switch { "manual" => VaultLockReason.Manual, "minimized" => VaultLockReason.Minimized, _ => VaultLockReason.Closed });

        Assert.False(_personal.Session.IsUnlocked);
        Assert.False(_acme.Session.IsUnlocked);
        Assert.IsType<AuthorityStatus.Locked>(_personal.Authority.Status);
    }

    [Fact]
    public void Keypaste_lock_and_a_lock_alone_lock_one_vault()
    {
        UnlockBoth();

        _acme.Session.Lock(VaultLockReason.Requested);
        Assert.True(_personal.Session.IsUnlocked);

        Unlock(_acme.Session, AcmePath);
        _vaults.LockAlone(_personal);
        Assert.False(_personal.Session.IsUnlocked);
        Assert.True(_acme.Session.IsUnlocked);
    }

    [Fact]
    public void The_idle_lock_of_the_vault_unlocked_first_locks_both()
    {
        Unlock(_personal.Session, _fixture.Path_);
        _clock.Advance(TimeSpan.FromMinutes(10));
        Unlock(_acme.Session, AcmePath);

        _clock.Advance(AppVaultSession.DefaultIdleTimeout - TimeSpan.FromMinutes(10));

        Assert.False(_personal.Session.IsUnlocked);
        Assert.False(_acme.Session.IsUnlocked);
    }

    [Fact]
    public void A_persons_activity_keeps_every_vault_open()
    {
        UnlockBoth();

        _clock.Advance(AppVaultSession.DefaultIdleTimeout - TimeSpan.FromMinutes(5));
        _vaults.Touch();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(_personal.Session.IsUnlocked);
        Assert.True(_acme.Session.IsUnlocked);
    }

    /// <summary>A request to the second vault waits behind the prompt the first put up, and is asked only once that is answered (THREATS.md T-11).</summary>
    [Fact]
    public async Task One_prompt_is_in_front_of_the_person_at_a_time_whichever_vault_asks()
    {
        UnlockBoth();
        var personalPrompt = _prompts[_personal.Session];
        var acmePrompt = _prompts[_acme.Session];

        await using var personal = await AttachAsync(_personal, _fixture.Path_);
        await using var acme = await AttachAsync(_acme, AcmePath);
        var first = personal.Client.RequestAsync(Request(_fixture.Path_, personal.Session), Token).AsTask();
        await personalPrompt.Shown.WaitAsync(_connect, Token);

        var second = acme.Client.RequestAsync(Request(AcmePath, acme.Session), Token).AsTask();
        await Task.Delay(_absent, Token);
        Assert.False(acmePrompt.Shown.IsCompleted, "the second vault's prompt showed beside the first");

        personalPrompt.Answer(ApprovalAnswer.ApprovedOnce);
        Assert.Equal(AuditDecision.Granted, (await first.WaitAsync(_connect, Token))?.Decision);

        await acmePrompt.Shown.WaitAsync(_connect, Token);
        acmePrompt.Answer(ApprovalAnswer.Denied);
        Assert.Equal(AuditDecision.Denied, (await second.WaitAsync(_connect, Token))?.Decision);
    }

    /// <summary>A bridge scoped to one vault reaches that vault's owner alone: the other vault's name on its pipe is refused.</summary>
    [Fact]
    public async Task An_agent_scoped_to_one_vault_cannot_reach_the_other()
    {
        UnlockBoth();
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(_personal.Authority.Status).Endpoint;

        await using var client = await ApproverClient.TryConnectAsync(endpoint, _connect, Token);
        Assert.NotNull(client);
        var refused = await client.AttachAsync(new AttachRequest(AcmePath), Token);
        Assert.False(refused?.Attached);

        var attached = await client.AttachAsync(new AttachRequest(_fixture.Path_), Token);
        var reply = await client.RequestAsync(Request(AcmePath, attached!.Session!), Token);

        Assert.Equal(AuditDecision.Denied, reply?.Decision);
        Assert.Null(reply?.Value);
        Assert.False(_prompts[_acme.Session].Shown.IsCompleted);
    }

    [Fact]
    public void A_rule_changed_for_one_vault_leaves_the_others_rules_alone()
    {
        UnlockBoth();

        var changed = _acme.Authority.ChangeRules(TempVault.Agent, rules => rules.With(TempVault.Agent, FieldCategory.Password, CategoryRule.Block));

        Assert.True(changed.Changed, changed.Reason);
        Assert.True(_acme.Authority.TryRules(out var acme, out _));
        Assert.True(_personal.Authority.TryRules(out var personal, out _));
        Assert.Equal(CategoryRule.Block, acme.RuleFor(TempVault.Agent, FieldCategory.Password));
        Assert.Null(personal.RuleFor(TempVault.Agent, FieldCategory.Password));
    }

    [Fact]
    public void Prompts_name_each_vault_beside_its_item()
    {
        Unlock(_personal.Session, _fixture.Path_);
        Assert.Equal("test", OpenVaults.NameShown(_personal.Session));

        Unlock(_acme.Session, AcmePath);
        Assert.Equal("test", OpenVaults.NameShown(_personal.Session));
        Assert.Equal("acme", OpenVaults.NameShown(_acme.Session));
    }

    [Fact]
    public void The_sidebar_lists_each_open_vault_its_places_on_its_own_session_and_a_locked_one_as_locked()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };
        Assert.True(shell.ShowsAccounts);
        Assert.Equal(["Favorites", "All Accounts", AccountSection.LocalTitle, "test"], shell.SidebarRows.Take(4).Select(row => row.ToString()!.Split(',')[0]));
        Assert.Contains(shell.SidebarRows, row => row is PlaceRow { Vault.Title: "acme", Place: ItemPlace.All });

        // A lock of acme alone reaches the sidebar at the next status tick.
        _vaults.LockAlone(_acme);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["test", "acme"], shell.SidebarRows.OfType<VaultRow>().Select(row => row.Title));
        var acme = shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme");
        Assert.True(acme.IsLocked);
        Assert.DoesNotContain(shell.SidebarRows, row => row is PlaceRow { Vault: var vault } && vault == acme.Vault);

        shell.SelectedSidebarRow = acme;
        var locked = Assert.IsType<LockedVaultViewModel>(shell.Content);
        Assert.Equal("acme", locked.Unlock.Heading);
        Assert.Equal("acme.kdbx", locked.Unlock.SelectedName);
        Assert.Equal(AcmePath, locked.Unlock.SelectedPath);

        Unlock(_acme.Session, AcmePath);
        _clock.Advance(TimeSpan.FromSeconds(1));
        shell.GoTo(1);
        shell.SelectedSidebarRow = shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Vault?.Title == "acme" && row.Place == ItemPlace.All);

        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal(["example"], items.Rows.Select(row => row.Title));
        Assert.Equal("acme", items.ListSubtitle);
        Assert.Equal("1", shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Vault?.Title == "acme" && row.Place == ItemPlace.All).Count);
    }

    [Fact]
    public async Task A_vault_opened_into_the_account_unlocks_from_its_own_form_on_its_own_session()
    {
        Unlock(_personal.Session, _fixture.Path_);
        _vaults.Close(_acme);
        var picker = new FakeVaultFilePicker { ExistingPath = AcmePath };
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock, picker: picker) { Vaults = _vaults };

        await shell.OpenVaultCommand.ExecuteAsync(null);

        var acme = Assert.Single(_vaults.Listed, vault => vault != _personal);
        var form = Assert.IsType<LockedVaultViewModel>(shell.Content);
        Assert.Equal("acme", form.Unlock.Heading);
        Assert.Equal(shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme"), shell.SelectedSidebarRow);
        Assert.True(_personal.Session.IsUnlocked);

        foreach (var character in TempVault.Password)
        {
            form.Unlock.Type(character);
        }

        await form.Unlock.UnlockAsync();

        Assert.True(acme.Session.IsUnlocked);
        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("acme", items.ListSubtitle);
        Assert.Equal(["example"], items.Rows.Select(row => row.Title));
        Assert.NotEqual(
            Assert.IsType<AuthorityStatus.Serving>(_personal.Authority.Status).Endpoint,
            Assert.IsType<AuthorityStatus.Serving>(acme.Authority.Status).Endpoint);
        Assert.Equal([_fixture.Path_, AcmePath], LocalAccount.Vaults(_fixture.Home));
    }

    [Fact]
    public void Closing_a_vault_takes_it_out_of_the_account_and_the_sidebar_and_leaves_its_file()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };
        shell.SelectedSidebarRow = shell.SidebarRows.OfType<PlaceRow>().Single(row => row.Vault?.Title == "acme" && row.Place == ItemPlace.All);

        shell.CloseVault(shell.Account.Vaults.Single(vault => vault.Title == "acme"));

        Assert.False(_acme.Session.IsUnlocked);
        Assert.False(shell.ShowsAccounts);
        Assert.DoesNotContain(shell.SidebarRows, row => row is AccountRow or VaultRow or PlaceRow { IsTop: true, Place: ItemPlace.All });
        Assert.Contains(shell.SidebarRows, row => row is PlaceRow { Vault.Title: "test", Place: ItemPlace.All });
        Assert.Equal([_fixture.Path_], LocalAccount.Vaults(_fixture.Home));
        Assert.True(File.Exists(AcmePath));
        Assert.Equal("test", Assert.IsType<EntriesViewModel>(shell.Content).ListSubtitle);
    }

    [Fact]
    public void The_account_lists_its_other_vaults_locked_after_an_unlock_and_a_pages_unlock_shows_that_vaults_form()
    {
        UnlockBoth();
        _vaults.Close(_acme);
        Assert.Equal([_fixture.Path_, AcmePath], LocalAccount.Vaults(_fixture.Home));

        _vaults.HoldAccount();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };

        var acme = shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme");
        Assert.True(acme.IsLocked);
        Assert.Equal(1, _vaults.Listed.Count(vault => _vaults.PathOf(vault.Session) == AcmePath));

        shell.ShowAsked(Core.Ipc.BrowserShow.Unlock, "acme", string.Empty);

        Assert.Equal("acme", Assert.IsType<LockedVaultViewModel>(shell.Content).Unlock.Heading);
        Assert.Equal(acme, shell.SelectedSidebarRow);
    }

    [Fact]
    public void A_pages_new_login_opens_in_the_sites_vault_with_the_page_filled_in()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };

        shell.ShowAsked(Core.Ipc.BrowserShow.NewLogin, "acme", "https://github.com");

        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("acme", items.ListSubtitle);
        Assert.Equal(ItemPlace.Logins, items.Place);
        Assert.Equal("https://github.com", items.NewItem?.Url);
        Assert.Equal(ItemTemplate.Login, items.NewItem?.Template);
    }

    [Fact]
    public void All_Accounts_lists_and_searches_every_unlocked_vault_and_acts_on_each_items_own_vault()
    {
        UnlockBoth();
        Assert.True(_acme.Session.Write(vault => { vault.AddEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "acme-gh-9Qe2" }); return true; }, added => added).Outcome == WriteOutcome.Saved);
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };

        shell.SelectedSidebarRow = shell.SidebarRows.OfType<PlaceRow>().Single(row => row is { IsTop: true, Place: ItemPlace.All });

        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("3", shell.SidebarRows.OfType<PlaceRow>().Single(row => row is { IsTop: true, Place: ItemPlace.All }).Count);
        Assert.Equal(["example · acme", "example · test", "github · acme"], items.Rows.Select(row => $"{row.Title} · {row.VaultName}").Order(StringComparer.Ordinal));
        Assert.Equal("All Accounts", items.ListSubtitle);

        shell.Search = "maya";
        Assert.Equal(["github"], items.Rows.Select(row => row.Title));

        shell.Search = string.Empty;
        items.Selected = items.Rows.Single(row => row is { Title: "example", VaultName: "acme" });
        Assert.Equal("acme", items.ItemVaultTitle);
        Assert.NotNull(items.Detail);

        items.DeleteCommand.Execute(null);
        items.ConfirmDeleteCommand.Execute(null);

        Assert.Null(_acme.Session.Unlocked!.Find(new EntryName(string.Empty, "example")));
        Assert.NotNull(_personal.Session.Unlocked!.Find(new EntryName(string.Empty, "example")));
        Assert.Equal(["example · test", "github · acme"], items.Rows.Select(row => $"{row.Title} · {row.VaultName}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Lock_locks_every_vault_and_a_vaults_own_lock_keeps_the_other_open()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, authority: null, clock: _clock) { Vaults = _vaults };
        shell.SelectedSidebarRow = shell.SidebarRows.OfType<PlaceRow>().Single(row => row is { IsTop: true, Place: ItemPlace.All });

        await shell.LockAloneAsync(shell.Account.Vaults.Single(vault => vault.Title == "acme"));
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(_personal.Session.IsUnlocked);
        Assert.Equal(["test"], Assert.IsType<EntriesViewModel>(shell.Content).Rows.Select(row => row.VaultName.Length == 0 ? "test" : row.VaultName).Distinct());
        Assert.True(shell.SidebarRows.OfType<VaultRow>().Single(row => row.Title == "acme").IsLocked);

        Unlock(_acme.Session, AcmePath);
        await shell.LockCommand.ExecuteAsync(null);

        Assert.False(_personal.Session.IsUnlocked);
        Assert.False(_acme.Session.IsUnlocked);
    }

    [Fact]
    public void Settings_Agents_and_Security_act_on_the_vault_their_picker_chose()
    {
        UnlockBoth();
        using var shell = new ShellViewModel(_personal.Session, _fixture.Home, _personal.Authority, clock: _clock) { Vaults = _vaults };

        shell.Current = Navigation.Destinations.Of(Navigation.DestinationKind.Settings);
        var settings = Assert.IsType<SettingsViewModel>(shell.Content);
        Assert.Equal(["test", "acme"], settings.VaultChoices.Select(choice => choice.Title));
        Assert.Equal("test", settings.ShownVault?.Title);
        settings.ShownVault = settings.VaultChoices.Single(choice => choice.Title == "acme");
        var acmeSettings = Assert.IsType<SettingsViewModel>(shell.Content);
        Assert.Equal("acme", acmeSettings.ShownVault?.Title);
        Assert.True(acmeSettings.ShowsThisVault);
        Assert.Equal(VaultBackups.DirectoryFor(AcmePath), acmeSettings.BackupsPath);

        shell.Current = Navigation.Destinations.Of(Navigation.DestinationKind.AgentActivity);
        var agents = Assert.IsType<AgentActivityViewModel>(shell.Content);
        Assert.Equal("test", agents.ShownVault?.Title);
        agents.ShownVault = agents.VaultChoices.Single(choice => choice.Title == "acme");
        Assert.Equal("acme", Assert.IsType<AgentActivityViewModel>(shell.Content).ShownVault?.Title);

        shell.Current = Navigation.Destinations.Of(Navigation.DestinationKind.Recommendations);
        var security = Assert.IsType<RecommendationsViewModel>(shell.Content);
        Assert.Equal(["All vaults", "test", "acme"], security.VaultChoices.Select(choice => choice.Title));
        Assert.Equal("All vaults", security.ShownVault?.Title);
        Assert.Equal("test", security.VaultHeading);
        Assert.Equal(["acme"], security.AlsoShown.Select(check => check.VaultHeading));
        Assert.Same(_acme.Session, security.AlsoShown[0].Session);

        security.ShownVault = security.VaultChoices.Single(choice => choice.Title == "acme");
        var acmeOnly = Assert.IsType<RecommendationsViewModel>(shell.Content);
        Assert.Same(_acme.Session, acmeOnly.Session);
        Assert.Empty(acmeOnly.AlsoShown);
        Assert.Equal(string.Empty, acmeOnly.VaultHeading);
    }

    [Fact]
    public async Task One_pairing_made_through_one_vault_serves_the_other_after_that_one_locks()
    {
        UnlockBoth();
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        string? personalHash;

        await using (var personal = await BrowserAsync(_personal, _fixture.Path_))
        {
            personalHash = personal.DatabaseHash;
            var paired = await personal.Link.PairAsync(new BrowserPairRequest(key) { Vault = _fixture.Path_, Session = personal.Session }, Token);
            Assert.Equal("laptop", paired?.Pairing);
        }

        _vaults.LockAlone(_personal);
        await using var acme = await BrowserAsync(_acme, AcmePath);
        var check = await acme.Link.CheckAsync(new BrowserCheckRequest("laptop", key) { Vault = AcmePath, Session = acme.Session }, Token);

        Assert.Equal(BrowserOutcome.Done, check?.Outcome);
        Assert.Equal(personalHash, acme.DatabaseHash);
    }

    private static async Task<Browsing> BrowserAsync(OpenVault vault, string path)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(vault.Authority.Status).Endpoint;
        var link = await BrowserLink.TryConnectAsync(endpoint, _connect, Token);
        Assert.NotNull(link);
        var attached = await link.AttachAsync(new AttachRequest(path) { Browser = Core.Browser.BrowserIdentity.ChromeWebStore.Caller }, Token);
        Assert.True(attached?.Attached, attached?.Reason);
        return new Browsing(link, attached!.Session!, attached.DatabaseHash);
    }

    private sealed record Browsing(BrowserLink Link, string Session, string? DatabaseHash) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Link.DisposeAsync();
    }

    private void UnlockBoth()
    {
        Unlock(_personal.Session, _fixture.Path_);
        Unlock(_acme.Session, AcmePath);
    }

    private static void Unlock(AppVaultSession session, string path)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value));
    }

    private static async Task<Attached> AttachAsync(OpenVault vault, string path)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(vault.Authority.Status).Endpoint;
        var client = await ApproverClient.TryConnectAsync(endpoint, _connect, Token);
        Assert.NotNull(client);
        var attached = await client.AttachAsync(new AttachRequest(path) { Client = new AttachClient("Claude Code", "2.1.0", TempVault.Agent) }, Token);
        Assert.True(attached?.Attached, attached?.Reason);
        return new Attached(client, attached!.Session!);
    }

    private static CredentialRequest Request(string vault, string session) => new()
    {
        Entry = "example",
        Field = "password",
        Reason = "deploy",
        Exposure = ["**"],
        ClientLabel = TempVault.Agent,
        Vault = vault,
        Session = session,
    };

    private sealed record Attached(ApproverClient Client, string Session) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }

    /// <summary>A person who sees a prompt and answers it when the test says.</summary>
    private sealed class HeldPrompt : IApprovalChannel
    {
        private readonly TaskCompletionSource _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ApprovalAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Shown => _shown.Task;

        internal void Answer(ApprovalAnswer answer) => _answer.TrySetResult(answer);

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            _shown.TrySetResult();
            return await _answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<PairingAnswer> AskAsync(PairingPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PairingAnswer(ApprovalAnswer.ApprovedOnce, "laptop"));
    }
}
