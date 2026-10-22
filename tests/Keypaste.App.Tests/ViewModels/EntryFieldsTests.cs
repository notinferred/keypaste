using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The entry pane's custom fields and tags: listed without a value, and each change made through
/// core as one revision, refused with a sentence when core or the file refuses it.
/// </summary>
public sealed class EntryFieldsTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _secret = "SENTINEL-FIELD-VALUE-2c77d1";

    private static readonly EntryName _checking = new("Banking", "Checking");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-entry-fields-").FullName;
    private readonly string _vaultPath;

    public EntryFieldsTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        KeePassInterop.WriteForeignUnchecked(_vaultPath, System.Text.Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_pane_lists_fields_by_name_and_kind_and_holds_no_value()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);

        Assert.Equal(["PIN", "otp"], detail.Fields.Select(field => field.Name));
        Assert.Equal(["protected", "KeePassXC"], detail.Fields.Select(field => field.Kind));
        Assert.All(detail.Fields, field => Assert.Equal(EntryFieldRow.MaskWidth, field.MaskedLength));
        Assert.False(detail.Fields[1].ReplaceCommand.CanExecute(null));
        Assert.False(detail.Fields[1].RemoveCommand.CanExecute(null));
        Assert.False(detail.Fields[1].ToggleProtectionCommand.CanExecute(null));
        Assert.Equal("4321", detail.Fields[0].Reveal());
    }

    [Fact]
    public void Adding_a_field_is_one_revision_and_protected_unless_switched_off()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var revisions = Revisions();

        Add(detail, "STRIPE_SECRET_KEY", _secret, protect: true);
        Add(detail, "Region", "eu", protect: false);

        Assert.Null(context.Entries.Error);
        Assert.Equal(revisions + 2, Revisions());
        Assert.Equal(
            [new EntryField("PIN", true, false), new EntryField("Region", false, false), new EntryField("STRIPE_SECRET_KEY", true, false), new EntryField("otp", true, true)],
            Fields());
        Assert.Equal(_secret, Read("STRIPE_SECRET_KEY"));
        Assert.Equal(["PIN", "Region", "STRIPE_SECRET_KEY", "otp"], detail.Fields.Select(field => field.Name));
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("Password")]
    [InlineData("KPXC_X")]
    [InlineData("PIN")]
    public void A_name_the_pane_refuses_writes_nothing_and_says_why(string name)
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var before = File.ReadAllBytes(_vaultPath);

        Add(detail, name, _secret, protect: true);

        Assert.NotNull(context.Entries.Error);
        Assert.True(detail.IsAddingField);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public void Replacing_a_value_keeps_its_protection_and_switching_protection_keeps_its_value()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var revisions = Revisions();

        detail.Fields.Single(field => field.Name == "PIN").ReplaceCommand.Execute(null);
        foreach (var c in "9876")
        {
            detail.ReplacementFieldValue.Type(c);
        }

        detail.ConfirmReplaceFieldCommand.Execute(null);
        Assert.False(detail.IsReplacingField);
        Assert.Equal("9876", Read("PIN"));
        Assert.Contains(new EntryField("PIN", true, false), Fields());

        detail.Fields.Single(field => field.Name == "PIN").ToggleProtectionCommand.Execute(null);
        Assert.Contains(new EntryField("PIN", false, false), Fields());
        Assert.Equal("9876", Read("PIN"));
        Assert.Equal(revisions + 2, Revisions());
        Assert.Equal("plain", detail.Fields.Single(field => field.Name == "PIN").Kind);
    }

    [Fact]
    public void Removing_a_field_asks_first_and_is_one_revision()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var revisions = Revisions();

        detail.Fields.Single(field => field.Name == "PIN").RemoveCommand.Execute(null);
        Assert.True(detail.IsRemovingField);
        Assert.Equal(revisions, Revisions());

        detail.ConfirmRemoveFieldCommand.Execute(null);

        Assert.False(detail.IsRemovingField);
        Assert.Equal(["otp"], detail.Fields.Select(field => field.Name));
        Assert.Equal(revisions + 1, Revisions());
    }

    /// <summary>
    /// A field that is not a variable and that nothing names is renamed at once, as one revision that
    /// keeps its value and protection.
    /// </summary>
    [Fact]
    public void Renaming_a_plain_field_is_one_revision_that_keeps_its_value_and_protection()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        Add(detail, "Region", "eu", protect: false);
        var revisions = Revisions();

        detail.Fields.Single(field => field.Name == "Region").RenameCommand.Execute(null);
        Assert.True(detail.IsRenamingField);
        Assert.Equal("Region", detail.DraftFieldRename);

        detail.DraftFieldRename = "Zone";
        detail.ConfirmRenameFieldCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.False(detail.IsRenamingField);
        Assert.Contains(new EntryField("Zone", false, false), Fields());
        Assert.DoesNotContain(Fields(), field => field.Name == "Region");
        Assert.Equal("eu", Read("Zone"));
        Assert.Equal(revisions + 1, Revisions());
        Assert.Contains(detail.Fields, field => field.Name == "Zone");
    }

    /// <summary>
    /// A field named as a project variable is renamed only after the pane has named the environments it
    /// leaves and the references it breaks, never a value; cancelling writes nothing (D-0415's pattern).
    /// </summary>
    [Fact]
    public void Renaming_a_variable_names_what_it_breaks_first_and_cancelled_writes_nothing()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        detail.DraftTag = "env:billing:staging";
        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);
        var before = File.ReadAllBytes(_vaultPath);

        detail.Fields.Single(field => field.Name == "PIN").RenameCommand.Execute(null);
        detail.DraftFieldRename = "PIN_CODE";
        detail.ConfirmRenameFieldCommand.Execute(null);

        Assert.True(detail.IsConfirmingFieldRename);
        Assert.Equal(
            "PIN is a variable of billing/staging. Renamed, those environments give PIN_CODE instead, and nothing there gives PIN.",
            detail.FieldRenameLines[0]);
        Assert.Contains(detail.FieldRenameLines, line => line.StartsWith("These references name PIN and will find nothing: kp://", StringComparison.Ordinal));
        Assert.All(detail.FieldRenameLines, line => Assert.DoesNotContain("4321", line, StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        detail.CancelRenameFieldCommand.Execute(null);
        Assert.False(detail.IsRenamingField);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        detail.Fields.Single(field => field.Name == "PIN").RenameCommand.Execute(null);
        detail.DraftFieldRename = "PIN_CODE";
        detail.ConfirmRenameFieldCommand.Execute(null);
        detail.ConfirmRenameFieldCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.False(detail.IsRenamingField);
        Assert.Contains(new EntryField("PIN_CODE", true, false), Fields());
        Assert.Equal("4321", Read("PIN_CODE"));
    }

    /// <summary>A name keypaste does not write is refused before anything is asked or written.</summary>
    [Theory]
    [InlineData("Password")]
    [InlineData("otp")]
    public void A_refused_field_rename_writes_nothing_and_says_why(string name)
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var before = File.ReadAllBytes(_vaultPath);

        detail.Fields.Single(field => field.Name == "PIN").RenameCommand.Execute(null);
        detail.DraftFieldRename = name;
        detail.ConfirmRenameFieldCommand.Execute(null);
        detail.ConfirmRenameFieldCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.True(detail.IsRenamingField);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    /// <summary>
    /// The Environments card lists each environment the item's own tags put it in, saying "approval
    /// required" where no agent gets the item without a person's answer, which a protected environment
    /// always is (D-0441, D-0371); an item in no project has no card.
    /// </summary>
    [Fact]
    public void The_environments_card_says_where_approval_is_required()
    {
        using (var vault = Vault.Open(_vaultPath, _master))
        {
            Assert.True(ProjectScope.TryCreate("billing", "staging", out var staging, out var error), error);
            Assert.Equal(
                AgentAccessWrite.Written,
                new AgentAccessStore(vault).Write("claude-code", AgentAccess.Defaults with { Projects = [new ProjectAccess(staging, AgentMode.Automatic)] }));
            vault.Save();
        }

        using var context = new Context(_vaultPath, _directory);
        var detail = context.Open(_checking);

        Assert.False(detail.ShowsEnvironments);

        detail.DraftTag = "env:billing:staging";
        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);

        var staged = Assert.Single(detail.Environments);
        Assert.True(detail.ShowsEnvironments);
        Assert.Equal(("billing · staging", "set", false), (staged.Label, staged.Status, staged.RequiresApproval));

        detail.DraftTag = "env:billing:prod";
        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);

        Assert.Equal(["billing · prod", "billing · staging"], detail.Environments.Select(row => row.Label).Order(StringComparer.Ordinal));
        Assert.All(detail.Environments, row => Assert.Equal("approval required", row.Status));
    }

    [Fact]
    public void Tags_are_chips_a_project_tag_shows_its_environment_and_protection_and_each_change_is_a_revision()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var revisions = Revisions();

        detail.DraftTag = "env:billing:prod";
        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);
        detail.DraftTag = "env:billing:Prod";
        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);

        var project = detail.Tags.Single(chip => chip.Tag == "env:billing:prod");
        Assert.Equal(("billing · prod", true, true), (project.Display, project.IsProject, project.IsProtected));
        var malformed = detail.Tags.Single(chip => chip.Tag == "env:billing:Prod");
        Assert.Equal((false, true, true), (malformed.IsProject, malformed.IsMalformed, malformed.IsProtected));
        var plain = detail.Tags.Single(chip => chip.Tag == "finance");
        Assert.Equal("finance", plain.Display);
        Assert.False(plain.IsProtected);
        Assert.Null(plain.Tip);

        project.RemoveCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);

        Assert.Equal(["env:billing:Prod", "finance"], Tags().Order(StringComparer.Ordinal));
        Assert.Equal(revisions + 3, Revisions());
        Assert.Empty(detail.DraftTag);
    }

    [Fact]
    public void A_project_tag_change_names_its_environment_and_fields_first_and_cancelled_writes_nothing()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var before = File.ReadAllBytes(_vaultPath);

        detail.DraftTag = "env:billing:staging";
        detail.AddTagCommand.Execute(null);

        Assert.True(detail.IsConfirmingTagChange);
        Assert.Equal(
            ["env:billing:staging puts Banking/Checking in billing/staging.", "Joining billing/staging: PIN."],
            detail.TagChangeLines);
        Assert.Equal("Add tag", detail.TagChangeAction);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        detail.CancelTagChangeCommand.Execute(null);
        Assert.False(detail.IsConfirmingTagChange);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
        Assert.DoesNotContain("env:billing:staging", Tags());

        detail.AddTagCommand.Execute(null);
        detail.ConfirmTagChangeCommand.Execute(null);
        Assert.Contains("env:billing:staging", Tags());
        Assert.Empty(detail.DraftTag);

        before = File.ReadAllBytes(_vaultPath);
        detail.Tags.Single(chip => chip.Tag == "env:billing:staging").RemoveCommand.Execute(null);
        Assert.Equal(
            ["Removing env:billing:staging takes Banking/Checking out of billing/staging.", "Leaving billing/staging: PIN."],
            detail.TagChangeLines);
        Assert.Equal("Remove tag", detail.TagChangeAction);
        detail.CancelTagChangeCommand.Execute(null);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        detail.DraftTag = "work";
        detail.AddTagCommand.Execute(null);
        Assert.False(detail.IsConfirmingTagChange);
        Assert.Contains("work", Tags());
    }

    [Fact]
    public void A_refused_tag_writes_nothing_and_says_why()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var before = File.ReadAllBytes(_vaultPath);

        detail.DraftTag = "a,b";
        detail.AddTagCommand.Execute(null);
        Assert.NotNull(context.Entries.Error);

        detail.DraftTag = "finance";
        detail.AddTagCommand.Execute(null);
        Assert.Contains("already has that tag", context.Entries.Error, StringComparison.Ordinal);

        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public void A_change_over_a_file_something_else_saved_is_refused_and_says_so()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);

        using (var elsewhere = Vault.Open(_vaultPath, _master))
        {
            elsewhere.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "x" });
            elsewhere.Save();
        }

        Add(detail, "LATE", _secret, protect: true);

        Assert.Contains("changed this vault", context.Entries.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(Fields(), field => field.Name == "LATE");
    }

    /// <summary>A form announces the lines and buttons that read it, or a binding keeps a stale prompt or a live button.</summary>
    [Fact]
    public void Opening_a_form_announces_the_lines_and_buttons_that_read_it()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var raised = new List<string>();
        detail.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        var asked = new List<string>();
        detail.RotateCommand.CanExecuteChanged += (_, _) => asked.Add("rotate");
        detail.SaveCommand.CanExecuteChanged += (_, _) => asked.Add("save");
        detail.ConfirmReplaceFieldCommand.CanExecuteChanged += (_, _) => asked.Add("replace");

        detail.EditCommand.Execute(null);
        detail.Fields.Single(field => field.Name == "PIN").ReplaceCommand.Execute(null);

        Assert.Equal(["replace", "rotate", "save"], asked.Order(StringComparer.Ordinal));
        Assert.False(detail.RotateCommand.CanExecute(null));
        Assert.Contains(nameof(EntryDetailViewModel.HasOwnPrimary), raised);
        Assert.Contains(nameof(EntryDetailViewModel.TakesWholeView), raised);
        Assert.Contains(nameof(EntryDetailViewModel.IsReplacingField), raised);
        Assert.Contains(nameof(EntryDetailViewModel.ReplaceFieldPrompt), raised);
    }

    [Fact]
    public async Task Copying_a_field_goes_through_the_countdown_under_its_name()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);

        await detail.Fields.Single(field => field.Name == "PIN").CopyValueCommand.ExecuteAsync();

        Assert.Equal("4321", context.Clipboard.Content);
        Assert.True(context.Countdown.IsCounting);
    }

    [Fact]
    public void Letting_go_of_the_pane_empties_its_fields_tags_and_forms()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        detail.BeginAddFieldCommand.Execute(null);
        detail.NewFieldValue.Type('x');

        detail.Dispose();

        Assert.Empty(detail.Fields);
        Assert.Empty(detail.Tags);
        Assert.Equal(0, detail.NewFieldValue.MaskedLength);
    }

    private static void Add(EntryDetailViewModel detail, string name, string value, bool protect)
    {
        detail.BeginAddFieldCommand.Execute(null);
        detail.DraftFieldName = name;
        detail.NewFieldProtected = protect;

        foreach (var c in value)
        {
            detail.NewFieldValue.Type(c);
        }

        detail.ConfirmAddFieldCommand.Execute(null);
    }

    private int Revisions()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.ReadHistory(_checking)!.Count;
    }

    private IReadOnlyList<EntryField> Fields()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.Fields(_checking)!;
    }

    private string? Read(string field)
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.ReadField(_checking, field);
    }

    private IReadOnlyList<string> Tags()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.Tags(_checking)!;
    }

    /// <summary>
    /// A tag change that would let an agent set to Automatic there get the entry without asking says so
    /// on the change, among what it reaches; it is confirmed like any other. With this computer's
    /// <c>clients.toml</c> unreadable it names the agent the vault's settings would release it to once
    /// the file reads again.
    /// </summary>
    [Theory]
    [InlineData(null, "Automatic: claude-code would get Banking/Checking without asking, each release recorded in history.")]
    [InlineData("this is not a clients file\n", "Automatic: by the vault's settings, claude-code would get Banking/Checking without asking, each release recorded in history, once this computer's clients.toml can be read again; until then every agent request is refused.")]
    public void A_project_tag_change_names_an_agent_it_would_let_get_the_entry_without_asking(string? clients, string said)
    {
        if (clients is not null)
        {
            File.WriteAllText(KeypasteHome.ClientsPath(_directory), clients);
        }

        using (var vault = Vault.Open(_vaultPath, _master))
        {
            Assert.True(ProjectScope.TryCreate("billing", "staging", out var staging, out var error), error);
            Assert.Equal(
                AgentAccessWrite.Written,
                new AgentAccessStore(vault).Write(
                    "claude-code",
                    AgentAccess.Defaults with { Projects = [new ProjectAccess(staging, AgentMode.Automatic)] }));
            vault.Save();
        }

        using var context = new Context(_vaultPath, _directory);
        var detail = context.Open(_checking);

        detail.DraftTag = "env:billing:staging";
        detail.AddTagCommand.Execute(null);

        Assert.True(detail.IsConfirmingTagChange);
        Assert.Equal(said, detail.TagChangeLines[^1]);

        detail.ConfirmTagChangeCommand.Execute(null);
        Assert.Contains("env:billing:staging", Tags());
    }

    private sealed class Context : IDisposable
    {
        /// <param name="vaultPath">The vault to unlock.</param>
        /// <param name="home">The keypaste home whose <c>clients.toml</c> it reads, or null for the process's.</param>
        internal Context(string vaultPath, string? home = null)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start), home: home);

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal FakeClipboard Clipboard { get; }

        internal EntryDetailViewModel Open(EntryName name)
        {
            Entries.Selected = Entries.Rows.Single(row => row.Title == name.Title && row.GroupPath == name.GroupPath);
            return Entries.Detail!;
        }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
