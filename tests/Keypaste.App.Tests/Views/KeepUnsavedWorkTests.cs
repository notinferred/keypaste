using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What was typed is kept unless the person discards it, and each form answers Return and Escape as the keyboard expects.</summary>
public sealed class KeepUnsavedWorkTests
{
    /// <summary>A form as a test drives it: whether it is showing, and what makes its confirm able to write.</summary>
    private sealed record Form(Func<bool> IsOpen, Action Fill, bool IsDanger = false, bool Writes = true);

    public static TheoryData<string> ConfirmingForms =>
    [
        "new item", "new group", "rename group", "move group", "rotate", "rename or move", "add field", "replace field",
        "rename field", "edit", "new project", "new environment", "add key", "replace key", "new token", "tag change",
        "restore revision", "import env",
    ];

    public static TheoryData<string> DangerForms =>
        ["delete group", "delete item", "remove field", "remove key", "remove attachment", "revoke token", "change access"];

    private static void Seed(Vault vault)
    {
        vault.AddEntry(new VaultEntry { Title = "box", GroupPath = "servers" });
        vault.AddEntry(new VaultEntry { Title = "spare", GroupPath = "other" });
        using var content = new MemoryStream([1, 2, 3]);
        vault.AddAttachment(new EntryName(string.Empty, "github"), "note.txt", content, out _);

        // A revision whose restore changes a project, which is what Restore asks about.
        Assert.True(vault.AddTag(new EntryName(string.Empty, "github"), "env:billing:old"));
        Assert.True(vault.RemoveTags(new EntryName(string.Empty, "github"), ["env:billing:old"]));
    }

    private static Action<Vault> SeedFor(string name) => vault =>
    {
        Seed(vault);

        if (name is "delete group" or "delete item")
        {
            vault.SetRecyclesDeletedEntries(false);
        }
    };

    private static Form Open(RenderedShell shell, string name)
    {
        switch (name)
        {
            case "delete .env file":
                var offering = shell.OpenProject();
                var imported = Path.Combine(Path.GetDirectoryName(shell.Session.VaultPath)!, "import.env");
                File.WriteAllText(imported, "IMPORTED_KEY=value\n");
                offering.Import.Preview(imported);
                offering.Import.ConfirmCommand.Press();
                return new Form(() => offering.Import.IsOfferingDelete, () => { }, IsDanger: true);

            case "delete for good":
                var seeding = shell.Show<EntriesViewModel>(DestinationKind.Entries);
                seeding.Selected = seeding.Rows.Single(row => row.Title == "spare");
                seeding.DeleteCommand.Press();
                var purging = shell.Show<TrashViewModel>(DestinationKind.Trash);
                purging.Selected = purging.Rows.Single();
                purging.PurgeCommand.Press();
                return new Form(() => purging.IsConfirmingPurge, () => { }, IsDanger: true);

            case "empty trash":
                var filling = shell.Show<EntriesViewModel>(DestinationKind.Entries);
                filling.Selected = filling.Rows.Single(row => row.Title == "spare");
                filling.DeleteCommand.Press();
                var emptying = shell.Show<TrashViewModel>(DestinationKind.Trash);
                emptying.EmptyCommand.Press();
                return new Form(() => emptying.IsConfirmingEmpty, () => { }, IsDanger: true);

            case "forget recent":
                var settings = shell.Show<SettingsViewModel>(DestinationKind.Settings);
                settings.AskForgetAllCommand.Press();
                return new Form(() => settings.IsConfirmingForgetAll, () => { }, IsDanger: true);

            case "new project":
                var env = shell.Show<EnvSetsViewModel>(DestinationKind.EnvSets);
                env.BeginAddCommand.Press();
                return new Form(() => env.IsAdding, () => env.NewProject = "acme", Writes: false);

            case "new environment":
                var profiling = shell.OpenProject();
                profiling.BeginAddProfileCommand.Press();
                return new Form(() => profiling.IsAddingProfile, () => profiling.NewProfile = "staging", Writes: false);

            case "add key":
                var adding = shell.OpenProject();
                adding.BeginAddCommand.Press();
                return new Form(() => adding.IsAdding, () => adding.NewKey = "ANOTHER_KEY");

            case "replace key":
                var replacing = shell.OpenProject();
                replacing.BeginReplace(replacing.Variables.Single(row => row.Key == "STRIPE_KEY"));
                return new Form(() => replacing.IsReplacing, () => replacing.ReplacementValue.Type('v'));

            case "remove key":
                var removing = shell.OpenProject();
                removing.BeginRemove(removing.Variables.Single(row => row.Key == "STRIPE_KEY"));
                return new Form(() => removing.IsRemoving, () => { }, IsDanger: true);

            case "import env":
                var importing = shell.OpenProject();
                var file = Path.Combine(Path.GetDirectoryName(shell.Session.VaultPath)!, "import.env");
                File.WriteAllText(file, "IMPORTED_KEY=value\n");
                importing.Import.Preview(file);
                return new Form(() => importing.Import.IsPreviewing, () => { });

            case "revoke token":
                var revoking = shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
                Assert.True(revoking.Create("ci", "read:billing/default/*", TimeSpan.FromDays(30), false).Ok);
                RenderedShell.Drain();
                revoking.AskRevokeCommand.Press(revoking.Rows.Single());
                return new Form(() => revoking.Rows.Single().IsConfirming, () => { }, IsDanger: true);

            case "change access":
                var access = shell.Show<SettingsViewModel>(DestinationKind.Settings).Access;
                access.SetPassword = true;
                RenderedShell.Drain();
                foreach (var (field, typed) in new[] { ("CurrentPassword", RenderedShell.Master), ("AccessNewPassword", "a much later password"), ("AccessConfirmPassword", "a much later password") })
                {
                    shell.Named<MaskedInput>(field).Focus();
                    WindowInput.Type(shell.Window, typed);
                }

                access.ReviewCommand.Press();
                Assert.True(access.IsConfirming);
                return new Form(() => access.IsConfirming, () => { }, IsDanger: true);

            case "new token":
                var tokens = shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
                tokens.OpenFormCommand.Press();
                return new Form(
                    () => tokens.IsFormOpen,
                    () =>
                    {
                        tokens.Name = "ci";
                        tokens.Scope = "read:billing/default/*";
                    });
        }

        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        switch (name)
        {
            case "new item":
                entries.BeginAddCommand.Press();
                return new Form(() => entries.IsAdding, () => entries.NewItem!.Title = "made");

            case "new group":
                entries.BeginCreateGroupCommand.Press();
                return new Form(() => entries.IsCreatingGroup, () => entries.DraftGroupName = "made");

            case "rename group":
                entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
                entries.BeginRenameGroupCommand.Press();
                return new Form(() => entries.IsRenamingGroup, () => entries.DraftGroupName = "hosts");

            case "move group":
                entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
                entries.BeginMoveGroupCommand.Press();
                return new Form(
                    () => entries.IsMovingGroup,
                    () => entries.GroupMoveTarget = entries.MoveTargets.Single(group => group.Path == "other"));

            case "delete group":
                entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
                entries.BeginDeleteGroupCommand.Press();
                return new Form(() => entries.IsConfirmingGroupDelete, () => { }, IsDanger: true);
        }

        var pane = shell.OpenPane();
        entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);

        switch (name)
        {
            case "rotate":
                pane.RotateCommand.Press();
                return new Form(() => pane.IsConfirmingRotate, () => { });

            case "delete item":
                entries.DeleteCommand.Press();
                var deleting = entries;
                return new Form(() => deleting.IsConfirmingDelete, () => { }, IsDanger: true);

            case "rename or move":
                entries.OrganizeCommand.Press();
                var organizing = entries;
                return new Form(() => organizing.IsOrganizing, () => organizing.DraftTitle = "renamed");

            case "add field":
                pane.BeginAddFieldCommand.Press();
                return new Form(() => pane.IsAddingField, () => pane.DraftFieldName = "MADE_KEY");

            case "replace field":
                pane.BeginReplaceField(pane.Fields.Single(row => row.Name == "API_TOKEN"));
                return new Form(() => pane.IsReplacingField, () => pane.ReplacementFieldValue.Type('v'));

            case "rename field":
                pane.BeginRenameField(pane.Fields.Single(row => row.Name == "Region"));
                return new Form(() => pane.IsRenamingField, () => pane.DraftFieldRename = "Area");

            case "remove field":
                pane.BeginRemoveField(pane.Fields.Single(row => row.Name == "Region"));
                return new Form(() => pane.IsRemovingField, () => { }, IsDanger: true);

            case "remove attachment":
                pane.Attachments.BeginRemove(pane.Attachments.Rows.Single());
                return new Form(() => pane.Attachments.IsRemoving, () => { }, IsDanger: true);

            case "tag change":
                pane.DraftTag = "env:billing:staging";
                pane.AddTagCommand.Press();
                return new Form(() => pane.IsConfirmingTagChange, () => { });

            case "restore revision":
                pane.History.ToggleCommand.Press();
                RenderedShell.Drain();
                pane.History.Selected = pane.History.Rows[0];
                pane.History.RestoreCommand.Press();
                return new Form(() => pane.History.IsConfirmingRestore, () => { });

            case "edit":
                pane.EditCommand.Press();
                return new Form(() => pane.IsEditing, () => pane.DraftUsername = "you");

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "no such form");
        }
    }

    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }

    private static byte[] VaultBytes(RenderedShell shell) => File.ReadAllBytes(shell.Session.VaultPath!);

    [Theory]
    [MemberData(nameof(ConfirmingForms))]
    public Task Return_confirms_a_form_and_writes(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        var form = Open(shell, name);
        form.Fill();
        RenderedShell.Drain();
        Assert.True(form.IsOpen());
        var before = VaultBytes(shell);

        Press(shell, PhysicalKey.Enter);

        Assert.False(form.IsOpen(), $"Return left {name} open");

        if (form.Writes)
        {
            Assert.NotEqual(before, VaultBytes(shell));
        }
    });

    [Theory]
    [MemberData(nameof(ConfirmingForms))]
    [MemberData(nameof(DangerForms))]
    [InlineData("delete .env file")]
    [InlineData("delete for good")]
    [InlineData("empty trash")]
    [InlineData("forget recent")]
    public Task Every_confirmation_puts_Cancel_first_and_the_action_last(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: SeedFor(name));
        Assert.True(Open(shell, name).IsOpen());
        RenderedShell.Drain();

        ConfirmationButtons.AssertCancelComesFirst(shell.Window);
    });

    [Theory]
    [MemberData(nameof(DangerForms))]
    public Task Return_never_confirms_a_danger_form(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: SeedFor(name));
        var form = Open(shell, name);
        Assert.True(form.IsDanger);
        RenderedShell.Drain();
        var before = VaultBytes(shell);

        Press(shell, PhysicalKey.Enter);

        Assert.Equal(before, VaultBytes(shell));
    });

    [Theory]
    [MemberData(nameof(ConfirmingForms))]
    [MemberData(nameof(DangerForms))]
    public Task Escape_closes_an_untouched_form_without_asking(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: SeedFor(name));
        var form = Open(shell, name);
        RenderedShell.Drain();
        var before = VaultBytes(shell);

        Press(shell, PhysicalKey.Escape);

        Assert.False(shell.Shell.HasDiscardPrompt);
        Assert.False(form.IsOpen(), $"Escape left {name} open");
        Assert.Equal(before, VaultBytes(shell));
    });

    [Theory]
    [InlineData("new item")]
    [InlineData("new group")]
    [InlineData("rename group")]
    [InlineData("rename or move")]
    [InlineData("add field")]
    [InlineData("rename field")]
    [InlineData("edit")]
    [InlineData("new project")]
    [InlineData("new environment")]
    [InlineData("add key")]
    [InlineData("new token")]
    public Task Escape_on_a_form_holding_a_draft_asks_first(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        var form = Open(shell, name);
        form.Fill();
        RenderedShell.Drain();

        Press(shell, PhysicalKey.Escape);
        Assert.True(shell.Shell.HasDiscardPrompt, $"Escape did not ask on {name}");
        Assert.True(form.IsOpen());

        Press(shell, PhysicalKey.Escape);
        Assert.False(shell.Shell.HasDiscardPrompt);
        Assert.True(form.IsOpen(), "Escape on the question should keep the draft");

        Press(shell, PhysicalKey.Escape);
        shell.Shell.ConfirmDiscardCommand.Press();
        RenderedShell.Drain();
        Assert.False(form.IsOpen());
    });

    [Theory]
    [InlineData("replace field", "ReplacementFieldValue")]
    [InlineData("replace key", "ReplacementEnvValue")]
    public Task Escape_in_a_masked_field_clears_it_and_reaches_the_form(string name, string field) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        var form = Open(shell, name);
        RenderedShell.Drain();
        var masked = shell.Named<MaskedInput>(field);
        masked.Focus();
        WindowInput.Type(shell.Window, "typed");
        Assert.Equal(5, masked.MaskedLength);

        Press(shell, PhysicalKey.Escape);

        Assert.Equal(0, masked.MaskedLength);
        Assert.False(form.IsOpen());
    });

    [Fact]
    public Task The_share_dialog_has_Cancel_and_answers_Return_and_Escape() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        var dialog = shell.Named<Border>("ShareDialog");
        Assert.Same(shell.Shell.Share!.CreateCommand, FormKeys.GetConfirm(dialog));

        shell.Named<TextBox>("Recipient").Text = "someone";
        RenderedShell.Drain();
        Press(shell, PhysicalKey.Escape);
        Assert.Equal("Discard this share link?", shell.Shell.DiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        Assert.True(shell.Shell.HasShare);
        Assert.Equal("someone", shell.Shell.Share!.Recipient);

        shell.Named<Button>("CancelShare").Command!.Press();
        RenderedShell.Drain();
        Assert.False(shell.Shell.HasShare);

        shell.Shell.ShareCommand.Press("github");
        RenderedShell.Drain();
        Press(shell, PhysicalKey.Escape);
        Assert.False(shell.Shell.HasShare);
    });

    [Fact]
    public Task Changing_place_keeps_a_draft_after_Cancel_and_loses_it_after_Discard() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Press();
        entries.NewItem!.Title = "half typed";
        RenderedShell.Drain();

        shell.Shell.Current = Destinations.Of(DestinationKind.Settings);
        RenderedShell.Drain();
        Assert.Equal("Discard this new item?", shell.Shell.DiscardPrompt);
        Assert.Same(entries, shell.Shell.Content);
        Assert.Same(shell.Named<Button>("KeepDraft"), shell.Window.FocusManager?.GetFocusedElement());

        // Return on the question takes its focused Cancel, never Discard.
        Press(shell, PhysicalKey.Enter);
        Assert.False(shell.Shell.HasDiscardPrompt);
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.Equal("half typed", entries.NewItem!.Title);

        shell.Shell.Current = Destinations.Of(DestinationKind.Settings);
        shell.Shell.ConfirmDiscardCommand.Press();
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
        Assert.Null(entries.NewItem);
    });

    [Fact]
    public Task An_untouched_form_changes_place_without_asking() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Show<EntriesViewModel>(DestinationKind.Entries).BeginAddCommand.Press();

        shell.Shell.Current = Destinations.Of(DestinationKind.Settings);

        Assert.False(shell.Shell.HasDiscardPrompt);
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
    });

    [Fact]
    public Task A_shortcut_asks_as_the_sidebar_does() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var pane = shell.OpenPane();
        pane.EditCommand.Press();
        pane.DraftUsername = "half typed";

        var settings = Destinations.All.First(place => place.Shortcut > 0 && place.Kind != DestinationKind.Entries);
        Assert.True(shell.Shell.GoTo(settings.Shortcut));

        Assert.Equal("Discard your changes to this item?", shell.Shell.DiscardPrompt);
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        shell.Shell.KeepDraftCommand.Press();
        Assert.Equal("half typed", pane.DraftUsername);
    });

    [Fact]
    public Task A_sidebar_row_comes_back_after_Cancel() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Press();
        entries.NewItem!.Title = "half typed";
        RenderedShell.Drain();
        var chosen = shell.Shell.SelectedSidebarRow;

        shell.Named<ListBox>("Nav").SelectedItem = shell.Shell.MainNav.Single(row => row.Destination.Kind == DestinationKind.AgentActivity);
        RenderedShell.Drain();
        Assert.True(shell.Shell.HasDiscardPrompt);

        shell.Shell.KeepDraftCommand.Press();
        RenderedShell.Drain();
        Assert.Same(chosen, shell.Named<ListBox>("Nav").SelectedItem);
    });

    [Fact]
    public Task Find_elsewhere_asks_and_Cancel_stays_where_the_draft_is() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var tokens = shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
        tokens.OpenFormCommand.Press();
        tokens.Name = "half-typed";

        shell.Shell.FocusSearch();
        Assert.Equal("Discard this new token?", shell.Shell.DiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        Assert.Same(tokens, shell.Shell.Content);
        Assert.Equal("half-typed", tokens.Name);

        shell.Shell.FocusSearch();
        shell.Shell.ConfirmDiscardCommand.Press();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        RenderedShell.Drain();
        Assert.True(shell.Named<TextBox>("ShellSearch").IsFocused);
    });

    [Fact]
    public Task Choosing_a_row_asks_before_losing_a_new_item() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var row = entries.Rows.Single(candidate => candidate.Title == "github");
        entries.BeginAddCommand.Press();
        entries.NewItem!.Title = "half typed";

        entries.Selected = row;
        Assert.True(shell.Shell.HasDiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        Assert.Equal("half typed", entries.NewItem!.Title);

        entries.Selected = row;
        shell.Shell.ConfirmDiscardCommand.Press();
        Assert.False(entries.IsAdding);
        Assert.Same(row, entries.Selected);
    });

    [Fact]
    public Task A_lock_discards_a_draft_without_asking() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Press();
        entries.NewItem!.Title = "half typed";

        shell.PressLock();

        Assert.True(shell.IsLocked);
        Assert.Null(entries.NewItem);
    });

    [Fact]
    public Task New_item_from_the_generator_waits_for_the_answer() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var pane = shell.OpenPane();
        pane.EditCommand.Press();
        pane.DraftUsername = "half typed";
        shell.Shell.OpenGeneratorCommand.Press();
        var generated = shell.Shell.GeneratorDialog!.MaskedLength;

        shell.Shell.GeneratorDialog.NewItemCommand.Press();
        Assert.True(shell.Shell.HasDiscardPrompt);
        Assert.True(shell.Shell.HasGeneratorDialog);

        shell.Shell.ConfirmDiscardCommand.Press();
        Assert.False(shell.Shell.HasGeneratorDialog);
        Assert.Equal(generated, Assert.IsType<EntriesViewModel>(shell.Shell.Content).NewItem!.Password.MaskedLength);
    });

    [Theory]
    [InlineData("NewItemTitleProblem", "That is not a name")]
    [InlineData("NewItemKeyNameProblem", "Name the key as a project reads it")]
    [InlineData("NewItemTagProblem", "")]
    [InlineData("OrganizeTitleProblem", "That is not a name")]
    [InlineData("NewKeyProblem", "cannot be a project's variable")]
    public Task An_error_appears_under_its_field_as_it_is_typed(string error, string says) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        string field;

        switch (error)
        {
            case "NewKeyProblem":
                var project = shell.OpenProject();
                project.BeginAddCommand.Press();
                field = "New key";
                RenderedShell.Drain();
                Assert.False(shell.Named<TextBlock>(error).IsEffectivelyVisible);
                project.NewKey = "lower case";
                break;

            case "OrganizeTitleProblem":
                shell.OpenPane();
                var organizing = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
                organizing.OrganizeCommand.Press();
                field = "OrganizeTitle";
                RenderedShell.Drain();
                Assert.False(shell.Named<TextBlock>(error).IsEffectivelyVisible);
                organizing.DraftTitle = "a/b";
                break;

            default:
                var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
                entries.BeginAddCommand.Press();
                var form = entries.NewItem!;
                form.Template = ItemTemplate.ApiKey;
                RenderedShell.Drain();
                Assert.False(shell.Named<TextBlock>(error).IsEffectivelyVisible);
                field = error switch { "NewItemTitleProblem" => "NewItemTitle", "NewItemKeyNameProblem" => "NewItemKeyName", _ => "NewItemTag" };

                if (error == "NewItemTitleProblem")
                {
                    form.Title = "a/b";
                }
                else if (error == "NewItemKeyNameProblem")
                {
                    form.KeyName = "lower case";
                }
                else
                {
                    form.DraftTag = "a,b";
                }

                break;
        }

        RenderedShell.Drain();
        var shown = shell.Named<TextBlock>(error);
        Assert.True(shown.IsEffectivelyVisible);
        Assert.Contains(says, shown.Text, StringComparison.Ordinal);

        var input = shell.Window.GetVisualDescendants().OfType<Control>()
            .Single(control => control.Name == field || (control is TextBox && AutomationProperties.GetName(control) == field));
        Assert.True(shown.TranslatePoint(default, shell.Window)!.Value.Y > input.TranslatePoint(default, shell.Window)!.Value.Y);
    });
}
