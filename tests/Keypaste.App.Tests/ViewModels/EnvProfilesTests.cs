using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Projects;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A project's profiles on the Env Sets screen: the matrix, the profile edits go to, its references and its run command.</summary>
public sealed class EnvProfilesTests : IDisposable
{
    private const string _prodValue = "prod-value-sentinel";

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));

    public EnvProfilesTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(vault, "acme-api", "DATABASE_URL", "dev-db");
            ProjectVariables.Set(vault, "acme-api", "prod", "DATABASE_URL", _prodValue);
            ProjectVariables.Set(vault, "acme-api", "prod", "SENTRY_DSN", "prod-sentry");
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _countdown.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Matrix_ReflectsTheVault()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal([new EnvProfileInfo("dev", false), new EnvProfileInfo("prod", true)], project.Profiles);
        var matrix = Assert.IsType<EnvMatrix>(project.Matrix);
        Assert.Equal(["DATABASE_URL", "SENTRY_DSN"], matrix.Rows.Select(row => row.Key));
        Assert.Equal([EnvCellState.Missing, EnvCellState.Set], matrix.Row("SENTRY_DSN")!.Cells.Select(cell => cell.State));
    }

    [Fact]
    public void SelectingAProfile_EditsThatProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        project.SelectedProfile = "staging";
        Assert.Empty(project.Variables);

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STAGING_ONLY";
        project.ConfirmAddCommand.Execute(null);
        Assert.Null(screen.Error);

        Assert.Equal(["STAGING_ONLY"], project.Variables.Select(row => row.Key));
        Assert.Equal(["dev", "staging", "prod"], project.Profiles.Select(profile => profile.Name));

        project.SelectedProfile = "prod";
        Assert.Equal(["DATABASE_URL", "SENTRY_DSN"], project.Variables.Select(row => row.Key));
        project.BeginRemove(project.Variables.Single(row => row.Key == "SENTRY_DSN"));
        project.ConfirmRemoveCommand.Execute(null);

        project.SelectedProfile = "Prod";
        Assert.Equal("prod", project.SelectedProfile);

        using var vault = Vault.Open(_fixture.Path_, TempVault.Password);
        var staging = new EntryName("env/acme-api", ".env.staging");
        Assert.NotNull(vault.ReadField(staging, "STAGING_ONLY"));
        Assert.Equal(["env:acme-api:staging"], vault.Tags(staging));
        Assert.Null(vault.Find(new EntryName("env/acme-api/staging", "STAGING_ONLY")));
        Assert.Null(vault.ReadField(ProjectVariables.Home("acme-api", "prod"), "SENTRY_DSN"));
        Assert.Equal("dev-db", vault.ReadField(ProjectVariables.Home("acme-api"), "DATABASE_URL"));
    }

    [Fact]
    public void ATaggedKey_IsReadReplacedAndRemovedOnItsOwnEntry()
    {
        var stripe = TaggedStripe();
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        var row = project.Variables.Single(row => row.Key == "STRIPE_KEY");
        Assert.Equal(new EnvSource("STRIPE_KEY", stripe, "STRIPE_KEY"), row.Source);
        Assert.Equal("stripe-sentinel", project.Read(row));

        var before = _session.Unlocked!.ReadHistory(stripe)!.Count;
        project.BeginReplace(row);
        foreach (var c in "stripe-replaced")
        {
            project.ReplacementValue.Type(c);
        }

        project.ConfirmReplaceCommand.Execute(null);
        Assert.Null(screen.Error);
        Assert.Equal("stripe-replaced", _session.Unlocked.ReadField(stripe, "STRIPE_KEY"));
        Assert.Equal(before + 1, _session.Unlocked.ReadHistory(stripe)!.Count);

        project.BeginRemove(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Assert.Equal("Remove STRIPE_KEY from services/Stripe? Its value stays in the entry's history.", project.RemovePrompt);
        project.ConfirmRemoveCommand.Execute(null);

        Assert.Null(screen.Error);
        Assert.Equal("Removed STRIPE_KEY from services/Stripe. Its value stays in the entry's history.", screen.Notice);
        Assert.Null(_session.Unlocked.ReadField(stripe, "STRIPE_KEY"));
        Assert.DoesNotContain(project.Variables, row => row.Key == "STRIPE_KEY");
        Assert.Empty(_session.Unlocked.ReadRecycled());
    }

    [Fact]
    public void Add_OffersTheHomeEntryAndTheTaggedEntries_AndWritesOnTheOneChosen()
    {
        var stripe = TaggedStripe("env:acme-api:staging");
        var home = new EntryName("env/acme-api", ".env.staging");
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);
        project.SelectedProfile = "staging";

        project.BeginAddCommand.Execute(null);
        Assert.Equal(
            [new EnvEntryChoice(null, "env/acme-api/.env.staging, created on first use"), new EnvEntryChoice(stripe, "services/Stripe")],
            project.EntryChoices);
        Assert.Equal(project.EntryChoices[0], project.NewEntry);

        project.NewKey = "WEBHOOK_SECRET";
        project.NewEntry = project.EntryChoices[1];
        project.ConfirmAddCommand.Execute(null);
        Assert.Null(screen.Error);
        Assert.Equal(20, _session.Unlocked!.ReadField(stripe, "WEBHOOK_SECRET")?.Length);
        Assert.Null(_session.Unlocked.Find(home));

        project.BeginAddCommand.Execute(null);
        project.NewKey = "HOME_KEY";
        project.ConfirmAddCommand.Execute(null);
        Assert.Null(screen.Error);
        Assert.Equal(["env:acme-api:staging"], _session.Unlocked.Tags(home));
        Assert.True(Assert.Single(_session.Unlocked.Fields(home)!).IsProtected);
        Assert.Equal(new EnvEntryChoice(home, "env/acme-api/.env.staging"), project.EntryChoices[0]);

        project.BeginAddCommand.Execute(null);
        project.NewKey = "lower_case";
        project.ConfirmAddCommand.Execute(null);
        Assert.Contains("'lower_case' cannot be a project's variable", screen.Error, StringComparison.Ordinal);
        Assert.True(project.IsAdding);
        Assert.Single(_session.Unlocked.Fields(home)!);
    }

    [Fact]
    public void ReferencePreview_SwitchesWithTheProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.EndsWith("DATABASE_URL=kp://acme-api/dev/DATABASE_URL\n", project.ReferencePreview, StringComparison.Ordinal);

        project.SelectedProfile = "prod";
        Assert.EndsWith(
            "DATABASE_URL=kp://acme-api/prod/DATABASE_URL\nSENTRY_DSN=kp://acme-api/prod/SENTRY_DSN\n",
            project.ReferencePreview,
            StringComparison.Ordinal);
        Assert.DoesNotContain(_prodValue, project.ReferencePreview, StringComparison.Ordinal);

        var path = Path.Combine(_fixture.Home, EnvReferenceFile.FileName);
        Assert.StartsWith("Wrote 2 references", project.ExportReferences(path), StringComparison.Ordinal);
        Assert.Equal(project.ReferencePreview, File.ReadAllText(path));
        Assert.Contains("already exists", project.ExportReferences(path), StringComparison.Ordinal);
        Assert.StartsWith("Wrote", project.ExportReferences(path, replace: true), StringComparison.Ordinal);
        Assert.Contains("is a vault", project.ExportReferences(_fixture.Path_, replace: true), StringComparison.Ordinal);
        Assert.DoesNotContain(_prodValue, File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedReferences_CarryTaggedKeys_AndATagOnlyEnvironmentsKeys()
    {
        var vault = _session.Unlocked!;
        var stripe = new EntryName("services", "Stripe");
        vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "stripe-login" });
        Assert.True(vault.SetFields(stripe, [new FieldWrite("STRIPE_KEY", "stripe-sentinel")]));
        Assert.True(vault.AddTag(stripe, "env:acme-api"));
        Assert.True(vault.AddTag(stripe, "env:acme-api:staging"));
        vault.Save();
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);
        var path = Path.Combine(_fixture.Home, EnvReferenceFile.FileName);

        Assert.EndsWith(
            "DATABASE_URL=kp://acme-api/dev/DATABASE_URL\nSTRIPE_KEY=kp://acme-api/dev/STRIPE_KEY\n",
            project.ReferencePreview,
            StringComparison.Ordinal);
        Assert.StartsWith("Wrote 2 references", project.ExportReferences(path), StringComparison.Ordinal);
        Assert.Equal(project.ReferencePreview, File.ReadAllText(path));

        project.SelectedProfile = "staging";
        Assert.EndsWith("\nSTRIPE_KEY=kp://acme-api/staging/STRIPE_KEY\n", project.ReferencePreview, StringComparison.Ordinal);
        Assert.StartsWith("Wrote 1 reference", project.ExportReferences(path, replace: true), StringComparison.Ordinal);
        Assert.Equal(project.ReferencePreview, File.ReadAllText(path));
        Assert.DoesNotContain("stripe-sentinel", File.ReadAllText(path), StringComparison.Ordinal);

        var twin = new EntryName("services", "Twin");
        vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Twin", Password = "twin-login" });
        Assert.True(vault.SetFields(twin, [new FieldWrite("STRIPE_KEY", "twin-sentinel")]));
        Assert.True(vault.AddTag(twin, "env:acme-api:staging"));
        vault.Save();
        File.Delete(path);

        Assert.Equal(
            "STRIPE_KEY is on more than one entry (services/Stripe, services/Twin), so nothing was written.",
            project.ExportReferences(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RunCommand_NamesTheProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal("keypaste run -p dev acme-api -- npm start", project.RunCommand);

        project.SelectedProfile = "prod";
        Assert.Equal("keypaste run -p prod acme-api -- npm start", project.RunCommand);
        Assert.Equal("prod", project.Launch.Profile);
        Assert.Equal("prod", project.Import.Profile);

        var directory = Directory.CreateTempSubdirectory("keypaste-profiles-run-").FullName;
        try
        {
            Assert.True(ProjectMappings.Save(
                KeypasteHome.ProjectsPath(_fixture.Home),
                [new ProjectMapping(Path.GetFullPath(_fixture.Path_), "acme-api", directory, "dotnet run")]));

            screen.OpenCommand.Execute("acme-api");
            Assert.Equal("keypaste run -p dev acme-api -- dotnet run", screen.OpenProject!.RunCommand);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EntryDetail_ShowsItsEntryReference_UnderEnvToo()
    {
        using var entries = new EntriesViewModel(_session, _countdown);

        entries.Selected = entries.Rows.Single(row => row.GroupPath == "env/acme-api" && row.Title == ".env.prod");
        Assert.Equal("kp:///env/acme-api/.env.prod", entries.Detail!.Reference);

        entries.Selected = entries.Rows.Single(row => row.Title == "example");
        Assert.Equal("kp:///example", entries.Detail!.Reference);
    }

    [Fact]
    public void TheScreen_OpensTheFirstProject_AndThePickerSwitchesIt()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);

        Assert.Equal("acme-api", screen.SelectedProject);
        Assert.Equal(["acme-api"], screen.ProjectChoices);

        screen.SelectedProject = "billing";
        Assert.Equal("billing", screen.OpenProject?.Name);
        Assert.Equal(["acme-api", "billing"], screen.ProjectChoices);
    }

    [Fact]
    public void MatrixCells_FollowTheSelectedProfile_AndHoldNoValue()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal(["dev", "prod"], project.Columns.Select(column => column.Name));
        Assert.Equal([true, false], project.Columns.Select(column => column.IsSelected));

        var sentry = project.Rows.Single(row => row.Key == "SENTRY_DSN");
        Assert.Equal(["missing", "••••••"], sentry.Cells.Select(cell => cell.Label));
        Assert.Null(sentry.Variable);
        Assert.Equal("DATABASE_URL", project.Rows.Single(row => row.Key == "DATABASE_URL").Variable?.Key);

        sentry.Cells[0].Act.Execute(null);
        Assert.Equal("dev", project.SelectedProfile);
        Assert.True(project.IsAdding);
        Assert.Equal("SENTRY_DSN", project.NewKey);
        project.CancelAddCommand.Execute(null);

        project.Columns.Single(column => column.Name == "prod").Select.Execute(null);
        Assert.Equal("prod", project.SelectedProfile);
        Assert.Equal("SENTRY_DSN", project.Rows.Single(row => row.Key == "SENTRY_DSN").Variable?.Key);

        project.BeginAddProfileCommand.Execute(null);
        project.NewProfile = "staging";
        project.ConfirmAddProfileCommand.Execute(null);
        Assert.Equal(["dev", "staging", "prod"], project.Columns.Select(column => column.Name));
        Assert.True(project.IsAdding);

        foreach (var text in project.Rows.SelectMany(row => row.Cells).SelectMany(cell => new[] { cell.Label, cell.Note, cell.Tip ?? string.Empty }))
        {
            Assert.DoesNotContain(_prodValue, text, StringComparison.Ordinal);
            Assert.DoesNotContain("dev-db", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Export_WritesTheChosenFile_AndSaysSo()
    {
        var path = Path.Combine(_fixture.Home, "exported", EnvReferenceFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "old");
        var picker = new ReferencePicker(path);
        using var screen = new EnvSetsViewModel(_session, _countdown, picker);
        var project = Open(screen);

        await project.ExportReferencesCommand.ExecuteAsync();

        Assert.Equal(1, picker.Calls);
        Assert.Equal(project.ReferencePreview, File.ReadAllText(path));
        Assert.StartsWith("Wrote 1 reference", screen.Notice, StringComparison.Ordinal);
        Assert.Null(screen.Error);

        picker.Path = _fixture.Path_;
        await project.ExportReferencesCommand.ExecuteAsync();
        Assert.Contains("is a vault", screen.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_environment_lists_its_entries_and_an_entry_several_read_is_marked_with_them()
    {
        var stripe = Tagged("services", "Stripe", "STRIPE_KEY", "env:acme-api", "env:acme-api:staging", "env:billing");
        var mail = Tagged("services", "Mail", "MAIL_KEY", "env:acme-api");
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal(["dev", "staging", "prod"], project.Environments.Select(environment => environment.Name));
        var dev = project.Environments[0];
        Assert.Equal([ProjectVariables.Home("acme-api"), mail, stripe], dev.Members.Select(member => member.Entry));
        Assert.Equal(["DATABASE_URL", "MAIL_KEY", "STRIPE_KEY"], dev.Members.Select(member => member.KeysText));
        Assert.Equal(["", "", "also staging, billing/dev"], dev.Members.Select(member => member.AlsoText));
        Assert.Equal("also dev, billing/dev", Assert.Single(project.Environments[1].Members).AlsoText);
        Assert.Equal("env/acme-api/.env.prod", Assert.Single(project.Environments[2].Members).Display);

        var cells = project.Rows.Single(row => row.Key == "STRIPE_KEY").Cells;
        Assert.Equal(["services/Stripe · also staging, billing/dev", "services/Stripe · also dev, billing/dev", ""], cells.Select(cell => cell.SourceNote));
        Assert.Equal("env/acme-api/.env", project.Rows.Single(row => row.Key == "DATABASE_URL").Cells[0].SourceNote);

        project.BeginReplace(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Assert.Equal(
            "New value for STRIPE_KEY on services/Stripe, which acme-api/dev, acme-api/staging and billing/dev read. The old one stays in the entry's history.",
            project.ReplacePrompt);
        project.BeginRemove(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Assert.Equal(
            "Remove STRIPE_KEY from services/Stripe, which acme-api/dev, acme-api/staging and billing/dev read? Its value stays in the entry's history.",
            project.RemovePrompt);
        project.BeginReplace(project.Variables.Single(row => row.Key == "MAIL_KEY"));
        Assert.Equal("New value for MAIL_KEY on services/Mail. The old one stays in the entry's history.", project.ReplacePrompt);
    }

    [Fact]
    public void Add_entry_says_what_it_reaches_and_writes_the_tag_only_when_confirmed()
    {
        var stripe = TaggedStripe();
        var queue = Tagged("services", "Queue", "QUEUE_KEY");
        Tagged(".keypaste/tokens", "planted", "PLANTED_KEY");
        Tagged("services", "Deleted", "DELETED_KEY");
        Assert.Equal(DeletionOutcome.Recycled, _session.Unlocked!.RemoveEntry(new EntryName("services", "Deleted")));
        _session.Unlocked.Save();
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        project.Environments[0].AddEntry.Execute(null);
        Assert.True(project.IsAddingEntry);
        Assert.Equal("Add an entry to dev", project.AddEntryTitle);
        var offered = project.EntryCandidates.Select(candidate => candidate.Entry).ToList();
        Assert.Contains(queue, offered);
        Assert.Contains(ProjectVariables.Home("acme-api", "prod"), offered);
        Assert.DoesNotContain(stripe, offered);
        Assert.DoesNotContain(ProjectVariables.Home("acme-api"), offered);
        Assert.DoesNotContain(offered, entry => entry.Title is "planted" or "Deleted");

        project.EntryFilter = "QUEUE";
        project.ChosenEntry = Assert.Single(project.EntryCandidates);
        Assert.Equal(["env:acme-api puts services/Queue in acme-api/dev.", "Joining acme-api/dev: QUEUE_KEY."], project.TagChangeLines);
        Assert.Equal("Add to dev", project.TagChangeAction);

        var before = File.ReadAllBytes(_fixture.Path_);
        project.CancelTagChangeCommand.Execute(null);
        Assert.False(project.IsConfirmingTagChange);
        Assert.Empty(_session.Unlocked.Tags(queue)!);
        Assert.Equal(before, File.ReadAllBytes(_fixture.Path_));

        project.ChosenEntry = Assert.Single(project.EntryCandidates);
        project.ConfirmTagChangeCommand.Execute(null);

        Assert.Null(screen.Error);
        Assert.Equal("Added services/Queue to acme-api/dev.", screen.Notice);
        Assert.False(project.IsAddingEntry);
        Assert.Contains(queue, project.Environments[0].Members.Select(member => member.Entry));
        Assert.Contains("QUEUE_KEY", project.Variables.Select(row => row.Key));
        using var saved = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal(["env:acme-api"], saved.Tags(queue));
    }

    [Fact]
    public void Remove_entry_takes_off_every_tag_that_puts_it_there_and_keeps_the_entry_and_its_fields()
    {
        var mail = Tagged("services", "Mail", "MAIL_KEY", "env:acme-api", "env:acme-api:dev", "finance");
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        project.Environments[0].Members.Single(member => member.Entry == mail).Remove.Execute(null);
        Assert.Equal(
            ["Removing env:acme-api, env:acme-api:dev takes services/Mail out of acme-api/dev.", "Leaving acme-api/dev: MAIL_KEY."],
            project.TagChangeLines);
        Assert.Equal(("Remove from dev", "Keep it"), (project.TagChangeAction, project.TagChangeCancel));

        var before = File.ReadAllBytes(_fixture.Path_);
        project.CancelTagChangeCommand.Execute(null);
        Assert.Equal(before, File.ReadAllBytes(_fixture.Path_));

        project.Environments[0].Members.Single(member => member.Entry == mail).Remove.Execute(null);
        project.ConfirmTagChangeCommand.Execute(null);

        Assert.Null(screen.Error);
        Assert.Equal("Removed services/Mail from acme-api/dev. The entry and its fields are kept.", screen.Notice);
        Assert.DoesNotContain(mail, project.Environments[0].Members.Select(member => member.Entry));
        Assert.DoesNotContain("MAIL_KEY", project.Variables.Select(row => row.Key));
        using var saved = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal(["finance"], saved.Tags(mail));
        Assert.Equal("mail_key-sentinel", saved.ReadField(mail, "MAIL_KEY"));
        Assert.Empty(saved.ReadRecycled());
    }

    [Fact]
    public void The_screen_names_every_tag_that_puts_its_entry_in_no_project()
    {
        Tagged("services", "Odd", "ODD_KEY", "env:acme-api:Prod");
        using var screen = new EnvSetsViewModel(_session, _countdown);

        var problem = Assert.Single(screen.MalformedTags);
        Assert.StartsWith("services/Odd has the tag env:acme-api:Prod, which puts it in no project: ", problem.Sentence, StringComparison.Ordinal);
        Assert.EndsWith(" Every agent request for the entry is still asked live.", problem.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("services/Odd", Open(screen).Environments.SelectMany(environment => environment.Members).Select(member => member.Display));
    }

    /// <summary>An ordinary entry holding <paramref name="key"/> as a field, with these tags, saved.</summary>
    private EntryName Tagged(string group, string title, string key, params string[] tags)
    {
        var vault = _session.Unlocked!;
        var entry = new EntryName(group, title);
        vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = title + "-login" });
        Assert.True(vault.SetFields(entry, [new FieldWrite(key, key.ToLowerInvariant() + "-sentinel")]));

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(entry, tag));
        }

        vault.Save();
        return entry;
    }

    /// <summary>An ordinary entry tagged into one of acme-api's environments, dev unless named, holding STRIPE_KEY as a field, saved.</summary>
    private EntryName TaggedStripe(string tag = "env:acme-api")
    {
        var vault = _session.Unlocked!;
        var stripe = new EntryName("services", "Stripe");
        vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "stripe-login" });
        Assert.True(vault.SetFields(stripe, [new FieldWrite("STRIPE_KEY", "stripe-sentinel")]));
        Assert.True(vault.AddTag(stripe, tag));
        vault.Save();
        return stripe;
    }

    private static EnvProjectViewModel Open(EnvSetsViewModel screen)
    {
        screen.OpenCommand.Execute("acme-api");
        return screen.OpenProject!;
    }

    private sealed class ReferencePicker(string path) : IVaultFilePicker
    {
        internal string? Path { get; set; } = path;

        internal int Calls { get; private set; }

        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyfileAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickDotEnvAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickReferenceFileAsync(string suggestedName, string? directory)
        {
            Calls++;
            return Task.FromResult(Path);
        }
    }
}
