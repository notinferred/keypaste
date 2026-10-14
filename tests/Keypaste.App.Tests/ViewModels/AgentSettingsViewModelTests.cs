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
/// An agent's settings panel on Agents: a choice that gives the agent nothing more applies at once, in
/// the vault and in <c>clients.toml</c>; any other waits for the person's confirmation and changes
/// nothing without it; each is recorded under keypaste app; <c>*</c> only takes away.
/// </summary>
public sealed class AgentSettingsViewModelTests
{
    private const string _sentinel = "SENTINEL-AGENT-SETTINGS-91c4";

    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_agent_shows_the_defaults_and_the_name_they_follow()
    {
        await using var app = await App.StartAsync();
        using var model = app.Settings();

        Assert.Equal("Ask, allow for a while", Chosen(model.ModeChoices));
        Assert.Equal("Yes", Chosen(model.ValueChoices));
        Assert.Equal("Off", Chosen(model.RunChoices));
        Assert.Equal("All projects", Chosen(model.SeeScopes));
        Assert.False(model.IsOnly);
        Assert.Contains("ci-probe", model.Identity, StringComparison.Ordinal);
        Assert.Contains("no settings of its own", model.Identity, StringComparison.Ordinal);
        Assert.Empty(model.Rows);
        Assert.Contains("billing:dev", model.NewRowScopes);
        Assert.Contains("billing", model.NewRowScopes);
        Assert.DoesNotContain("billing:prod", model.NewRowScopes);
        Assert.Contains("billing:prod", model.SeeChoices.Select(choice => choice.Words));
        Assert.False(model.IsHeldBack);
        Assert.True(model.CanChange);
    }

    [Fact]
    public async Task A_narrowing_applies_at_once_in_the_vault_and_on_this_computer_and_ends_the_agents_grants()
    {
        await using var app = await App.StartAsync();
        app.Person.Credential = ApprovalAnswer.Approved;
        Assert.Equal(AuditMethod.Prompt, (await app.RequestAsync())!.Method);
        Assert.Single(app.Authority.Activity.Grants);
        using var model = app.Settings();

        await model.ChooseAsync(Choice(model.ModeChoices, "Ask every time"));

        Assert.Empty(app.Person.Changes);
        Assert.Equal("Ask every time", Chosen(model.ModeChoices));
        Assert.StartsWith("Changed: ", model.Message, StringComparison.Ordinal);
        Assert.Equal(AgentMode.Ask, Assert.Single(app.Caps().Rows).Mode);
        Assert.Equal(AgentMode.Ask, app.Record().Mode);
        Assert.Empty(app.Authority.Activity.Grants);

        var line = app.LastLine();
        Assert.Equal(
            (AuditTools.AccessChange, "keypaste app", "ci-probe", "granted", "narrowing"),
            (line.Tool, line.Name, line.Label, line.Decision, line.Method));
    }

    [Fact]
    public async Task A_widening_waits_for_the_person_and_a_refusal_changes_nothing()
    {
        await using var app = await App.StartAsync();
        app.Person.Change = ApprovalAnswer.Denied;
        using var model = app.Settings();

        await model.ChooseAsync(Choice(model.RunChoices, "On, each command still asked about"));

        var asked = Assert.Single(app.Person.Changes);
        Assert.Equal(("ci-probe", AccessOrigin.Settings), (asked.Agent, asked.Origin));
        Assert.Equal("asked from this app's agent settings", asked.OriginText);
        Assert.Contains("may run commands, each still asked about", asked.Gains);
        Assert.Equal("Off", Chosen(model.RunChoices));
        Assert.StartsWith("Not changed: a person refused this change", model.Message, StringComparison.Ordinal);
        Assert.True(app.Authority.TrySettings("ci-probe", out var settings, out _));
        Assert.False(settings.HasRecord);
        Assert.Equal(("denied", "prompt"), (app.LastLine().Decision, app.LastLine().Method));

        // Refusing their own change holds nothing back: trying it again asks again.
        await model.ChooseAsync(Choice(model.RunChoices, "On, each command still asked about"));

        Assert.Equal(2, app.Person.Changes.Count);
        Assert.Equal(("denied", "prompt"), (app.LastLine().Decision, app.LastLine().Method));
    }

    [Fact]
    public async Task A_confirmed_Automatic_is_saved_and_then_releases_without_asking()
    {
        await using var app = await App.StartAsync();
        app.Person.Change = ApprovalAnswer.ApprovedOnce;
        using var model = app.Settings();

        await model.ChooseAsync(Choice(model.ModeChoices, "Automatic"));

        Assert.Single(app.Person.Changes);
        Assert.Equal("Automatic", Chosen(model.ModeChoices));
        Assert.Equal(AgentMode.Automatic, app.Record().Mode);
        Assert.Equal(("granted", "prompt"), (app.LastLine().Decision, app.LastLine().Method));

        var released = await app.RequestAsync();

        Assert.Equal((AuditDecision.Granted, AuditMethod.Automatic, _sentinel), (released!.Decision, released.Method, released.Value));
        Assert.Equal(0, app.Person.Credentials);
    }

    [Fact]
    public async Task Every_other_agent_offers_no_Automatic_and_no_commands()
    {
        await using var app = await App.StartAsync();
        using var model = app.Settings(AgentNames.Any);

        Assert.Equal(["Ask every time", "Ask, allow for a while"], model.ModeChoices.Select(choice => choice.Words));
        Assert.Empty(model.RunChoices);
        Assert.False(model.OffersRun);
        Assert.Contains("only take away", model.Identity, StringComparison.Ordinal);
        Assert.All(model.Rows, row => Assert.DoesNotContain(row.Modes, choice => choice.Word == "automatic"));

        await model.ChooseAsync(Choice(model.ValueChoices, "No, only into commands it runs"));

        Assert.Empty(app.Person.Changes);
        Assert.False(app.Caps().For(null).Values);
    }

    [Fact]
    public async Task What_this_computer_holds_back_is_named_and_lifted_once_the_person_confirms_the_vaults_settings()
    {
        await using var app = await App.StartAsync(vault => Assert.Equal(
            AgentAccessWrite.Written,
            new AgentAccessStore(vault).Write("ci-probe", AgentAccess.Defaults with { Mode = AgentMode.Automatic })));
        File.WriteAllText(app.ClientsPath, "[[client]]\nlabel = \"ci-probe\"\nmode = \"ask\"\n");
        app.Person.Change = ApprovalAnswer.ApprovedOnce;
        using var model = app.Settings();

        Assert.Equal("Automatic", Chosen(model.ModeChoices));
        Assert.True(model.IsHeldBack);
        Assert.Contains("by default: Automatic", model.HeldBack, StringComparison.Ordinal);

        await model.UseVaultAsync();

        Assert.Contains("by default: Automatic: released without asking, recorded in history", Assert.Single(app.Person.Changes).Gains);
        Assert.False(model.IsHeldBack);
        Assert.Equal(AgentMode.Automatic, app.Caps().Rows.Single(row => row is { Label: "ci-probe", Project: null }).Mode);
        Assert.Equal(AgentMode.Automatic, app.Record().Mode);
        Assert.Equal(
            ("granted", "prompt", "a person confirmed it, lifting what this computer held back: by default: Automatic: released without asking, recorded in history (ci-probe: use the vault's settings)"),
            (app.LastLine().Decision, app.LastLine().Method, app.LastLine().Reason));
    }

    [Fact]
    public async Task A_row_starts_at_the_mode_it_has_and_its_mode_limit_and_removal_are_changes_of_their_own()
    {
        await using var app = await App.StartAsync();
        app.Person.Change = ApprovalAnswer.ApprovedOnce;
        using var model = app.Settings();
        model.NewRowScope = "billing:dev";

        await model.AddRowAsync();

        Assert.Empty(app.Person.Changes);
        var row = Assert.Single(model.Rows);
        Assert.Equal(("billing:dev", "Ask, allow for a while"), (row.Name, Chosen(row.Modes)));
        Assert.DoesNotContain("billing:dev", model.NewRowScopes);

        await model.ChooseAsync(Choice(row.Modes, "Automatic"));

        Assert.Single(app.Person.Changes);
        Assert.Equal("Automatic", Chosen(Assert.Single(model.Rows).Modes));
        Assert.Equal(["billing:dev"], model.LimitTargets);
        Assert.Equal("billing:dev", model.LimitTarget);

        model.PerHourText = "5";
        await model.ApplyLimitAsync();

        Assert.Single(app.Person.Changes);
        Assert.Equal("at most 5 an hour", Assert.Single(model.Rows).Limit);
        Assert.Equal("5", model.PerHourText);

        await model.RemoveRowAsync(Assert.Single(model.Rows));

        Assert.Single(app.Person.Changes);
        Assert.Empty(model.Rows);
        Assert.Empty(app.Record().Projects);
    }

    /// <summary>
    /// Until is a day, and a time of day or the day's close, on this computer's clock: sent and kept in
    /// UTC, recorded so, and shown back on that clock with its offset; a time of day it cannot read, or
    /// a day already past, changes nothing and says why.
    /// </summary>
    [Fact]
    public async Task Until_is_read_and_shown_on_this_computers_clock_and_kept_in_UTC()
    {
        await using var app = await App.StartAsync(
            vault => Assert.Equal(
                AgentAccessWrite.Written,
                new AgentAccessStore(vault).Write("ci-probe", AgentAccess.Defaults with { Mode = AgentMode.Automatic })),
            TestZones.PlusTwo);
        using var model = app.Settings();

        Assert.Equal(["by default"], model.LimitTargets);
        Assert.Equal("Now: no limit.", model.LimitNow);

        model.UntilDate = new DateTime(2026, 12, 31);
        await model.ApplyLimitAsync();

        Assert.Empty(app.Person.Changes);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero), app.Record().Limit.Until);
        Assert.EndsWith("(ci-probe: per-hour none, until 2026-12-31T21:59:59Z)", app.LastLine().Reason, StringComparison.Ordinal);
        Assert.EndsWith("(ci-probe: per-hour none, until 2026-12-31 23:59 +02:00).", model.Message, StringComparison.Ordinal);
        Assert.Equal("Now: until 2026-12-31 23:59 +02:00.", model.LimitNow);
        Assert.Equal((new DateTime(2026, 12, 31), string.Empty), (model.UntilDate, model.UntilTimeText));

        model.UntilTimeText = "18:00";
        await model.ApplyLimitAsync();

        Assert.Equal(new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero), app.Record().Limit.Until);
        Assert.Equal("Now: until 2026-12-31 18:00 +02:00.", model.LimitNow);
        Assert.Equal((new DateTime(2026, 12, 31), "18:00"), (model.UntilDate, model.UntilTimeText));

        model.UntilTimeText = "6pm";
        await model.ApplyLimitAsync();

        Assert.Equal("Not changed: a time of day is written like 18:00 on this computer's clock, or left empty for the close of the day.", model.Message);

        model.UntilDate = new DateTime(2026, 7, 1);
        model.UntilTimeText = string.Empty;
        await model.ApplyLimitAsync();

        Assert.Equal("Not changed: until 2026-07-01 23:59 +02:00 has already passed on this computer's clock.", model.Message);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero), app.Record().Limit.Until);
        Assert.Empty(app.Person.Changes);
    }

    /// <summary>
    /// Can see is all projects or only those picked: picks change nothing until they are applied, then go
    /// as one change, so switching to a few projects never passes through seeing nothing; seeing less
    /// applies at once and seeing more is confirmed.
    /// </summary>
    [Fact]
    public async Task Can_see_is_all_projects_or_only_the_picks_sent_as_one_change()
    {
        await using var app = await App.StartAsync();
        app.Person.Change = ApprovalAnswer.ApprovedOnce;
        using var model = app.Settings();

        Assert.False(model.ChooseCommand.CanExecute(Choice(model.SeeChoices, "billing:dev")));

        await model.ChooseAsync(Choice(model.SeeScopes, "Only these"));
        await model.ChooseAsync(Choice(model.SeeChoices, "billing:dev"));
        await model.ChooseAsync(Choice(model.SeeChoices, "billing"));
        await model.ChooseAsync(Choice(model.SeeChoices, "billing"));

        Assert.True(model.IsOnly);
        Assert.Equal("Only these", Chosen(model.SeeScopes));
        Assert.Equal(["billing:dev"], model.SeeChoices.Where(choice => choice.IsChosen).Select(choice => choice.Words));
        Assert.True(app.Authority.TrySettings("ci-probe", out var untouched, out _));
        Assert.False(untouched.HasRecord);

        await model.ApplyPicksAsync();

        Assert.Empty(app.Person.Changes);
        Assert.Equal(["tag:env:billing:dev"], app.Record().See.Globs);
        Assert.Equal(("granted", "narrowing"), (app.LastLine().Decision, app.LastLine().Method));
        Assert.EndsWith("(ci-probe: see tag:env:billing:dev)", app.LastLine().Reason, StringComparison.Ordinal);
        Assert.Equal("Only these", Chosen(model.SeeScopes));
        Assert.False(model.ApplyPicksCommand.CanExecute(null));

        await model.ChooseAsync(Choice(model.SeeChoices, "billing:prod"));
        await model.ApplyPicksAsync();

        Assert.Contains("sees tag:env:billing:dev, tag:env:billing:prod", Assert.Single(app.Person.Changes).Gains);
        Assert.Equal(["tag:env:billing:dev", "tag:env:billing:prod"], app.Record().See.Globs);

        await model.ChooseAsync(Choice(model.SeeScopes, "All projects"));

        Assert.Equal(2, app.Person.Changes.Count);
        Assert.Equal([EntryExposure.DefaultGlob], app.Record().See.Globs);
        Assert.Equal("All projects", Chosen(model.SeeScopes));
        Assert.False(model.IsOnly);

        model.ToggleAdvancedCommand.Execute(null);
        Assert.True(model.IsAdvancedOpen);
        model.SeePattern = "api/**, tag:env:billing:dev";
        await model.ApplySeeAsync();

        Assert.Equal(["api/**", "tag:env:billing:dev"], app.Record().See.Globs);
        Assert.Equal(["billing:dev"], model.SeeChoices.Where(choice => choice.IsChosen).Select(choice => choice.Words));
        Assert.Equal("It also sees, by pattern: api/**.", model.AlsoSees);

        // A change of picks keeps what it sees by pattern.
        await model.ChooseAsync(Choice(model.SeeChoices, "billing:dev"));
        await model.ApplyPicksAsync();

        Assert.Equal(["api/**"], app.Record().See.Globs);
    }

    /// <summary>
    /// With nothing picked and no pattern left, the caption beside Apply says that applying lets the agent see
    /// no items, and says how picks are applied again once one is picked; the choice itself stays available.
    /// </summary>
    [Fact]
    public async Task Only_these_with_nothing_picked_says_applying_lets_it_see_nothing()
    {
        await using var app = await App.StartAsync();
        using var model = app.Settings();
        List<string?> raised = [];
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await model.ChooseAsync(Choice(model.SeeScopes, "Only these"));

        Assert.Equal("Nothing picked: applying lets it see no items.", model.PicksCaption);
        Assert.True(model.ApplyPicksCommand.CanExecute(null));
        Assert.Contains(nameof(AgentSettingsViewModel.PicksCaption), raised);

        await model.ChooseAsync(Choice(model.SeeChoices, "billing"));

        Assert.Equal("Pick the projects and environments it may see, then apply them as one change.", model.PicksCaption);
    }

    /// <summary>Choosing Only these and then All projects again, before applying anything, changes nothing.</summary>
    [Fact]
    public async Task Only_these_changes_nothing_until_its_picks_are_applied()
    {
        await using var app = await App.StartAsync();
        using var model = app.Settings();

        await model.ChooseAsync(Choice(model.SeeScopes, "Only these"));
        await model.ChooseAsync(Choice(model.SeeChoices, "billing"));
        await model.ChooseAsync(Choice(model.SeeScopes, "All projects"));

        Assert.False(model.IsOnly);
        Assert.Empty(app.Person.Changes);
        Assert.True(app.Authority.TrySettings("ci-probe", out var settings, out _));
        Assert.False(settings.HasRecord);
    }

    [Fact]
    public async Task A_locked_app_reads_no_settings_and_offers_no_change()
    {
        await using var app = await App.StartAsync();
        using var model = app.Settings();

        app.Authority.Session.Lock(VaultLockReason.Manual);
        model.Read();

        Assert.True(model.HasProblem);
        Assert.False(model.CanChange);
        Assert.False(model.ChooseCommand.CanExecute(Choice(model.ModeChoices, "Automatic")));
    }

    private static SettingChoice Choice(IReadOnlyList<SettingChoice> choices, string words) =>
        choices.Single(choice => choice.Words == words);

    private static string Chosen(IReadOnlyList<SettingChoice> choices) => choices.Single(choice => choice.IsChosen).Words;

    private sealed class App : IAsyncDisposable
    {
        private readonly ApproverClient _client;
        private readonly EntryName _item = ProjectVariables.Home("billing");

        private App(TempVault fixture, Person person, AppAuthority authority, ApproverClient client, string session)
        {
            Fixture = fixture;
            Person = person;
            Authority = authority;
            _client = client;
            Session = session;
        }

        internal TempVault Fixture { get; }

        internal Person Person { get; }

        internal AppAuthority Authority { get; }

        internal string Session { get; }

        internal string ClientsPath => KeypasteHome.ClientsPath(Fixture.Home);

        /// <param name="arrange">Writes into the vault before the app unlocks it, which then saves it.</param>
        /// <param name="zone">This computer's time zone, or the runner's.</param>
        internal static async Task<App> StartAsync(Action<Vault>? arrange = null, TimeZoneInfo? zone = null)
        {
            var fixture = new TempVault();

            using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
            {
                ProjectVariables.Set(vault, "billing", "STRIPE_KEY", _sentinel);
                ProjectVariables.Set(vault, "billing", "prod", "STRIPE_KEY", "prod-value");
                arrange?.Invoke(vault);
                vault.Save();
            }

            var person = new Person();
#pragma warning disable CA2000
            var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start) { Zone = zone ?? TimeZoneInfo.Local }, home: fixture.Home), null, () => person);
#pragma warning restore CA2000

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = new AttachClient("claude", "2.0", "ci-probe") }, Token))!.Attached);

            return new App(fixture, person, authority, client, serving.Session);
        }

        internal AgentSettingsViewModel Settings(string agent = "ci-probe") => new(Authority, agent);

        internal AccessCaps Caps()
        {
            Assert.True(AccessCaps.TryLoad(ClientsPath, out var caps, out var problem), problem);
            return caps;
        }

        /// <summary>The agent's own record, as the vault's file holds it.</summary>
        internal AgentAccess Record()
        {
            Assert.True(Authority.TrySettings("ci-probe", out var settings, out var problem), problem);
            Assert.True(settings.HasRecord, "the vault holds no record for the agent");
            Assert.False(settings.BeneathDefaults, "the record is not what the vault's file holds");
            return settings.Vault;
        }

        internal AuditEntry LastLine()
        {
            Assert.True(AuditReader.TryRead(KeypasteHome.AuditPath(Fixture.Home), out var entries, out _, out var error), error);
            return entries[^1];
        }

        internal Task<CredentialReply?> RequestAsync() =>
            _client.RequestAsync(
                new CredentialRequest
                {
                    Entry = $"{_item.GroupPath}/{_item.Title}",
                    Field = "STRIPE_KEY",
                    Reason = "deploy",
                    TtlSeconds = 60,
                    Exposure = ["**"],
                    ClientName = "claude",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            Authority.Dispose();
            Fixture.Dispose();
        }
    }

    /// <summary>The person at the app: answers each credential prompt and each confirmation as the test says, and keeps what it was shown.</summary>
    private sealed class Person : IApprovalChannel
    {
        internal ApprovalAnswer Credential { get; set; } = ApprovalAnswer.Denied;

        internal ApprovalAnswer Change { get; set; } = ApprovalAnswer.Denied;

        internal int Credentials { get; private set; }

        internal List<AccessChangePrompt> Changes { get; } = [];

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Credentials++;
            return ValueTask.FromResult(Credential);
        }

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
        {
            Changes.Add(prompt);
            return ValueTask.FromResult(Change);
        }
    }
}
