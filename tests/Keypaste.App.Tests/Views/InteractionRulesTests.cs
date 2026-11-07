using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>DESIGN's interaction rules as the screens now keep them (Q.19 stage 3): a field's label and error, a wait, the amber, and the menus.</summary>
public sealed partial class InteractionRulesTests
{
    private const string _github = "github";

    private static IEnumerable<T> Shown<T>(RenderedShell shell)
        where T : Control =>
        shell.Window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

    private static EntriesViewModel OpenGithub(RenderedShell shell)
    {
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == _github);
        RenderedShell.Drain();
        return entries;
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "keypaste.app.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("the repository is not above the tests");
    }

    /// <summary>A secret field's accessible name can never follow what is typed, because every view names it with a fixed label.</summary>
    [Fact]
    public void Every_secret_field_is_named_by_a_fixed_label()
    {
        var views = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Keypaste.App", "Views"), "*.axaml");
        var fields = views.SelectMany(view => MaskedField().Matches(File.ReadAllText(view)).Select(field => (View: Path.GetFileName(view), field.Value))).ToList();

        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.Matches(FixedName(), field.Value));
    }

    [Fact]
    public Task A_key_left_empty_says_so_under_its_field_and_not_at_the_top_of_the_page() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-empty-key-");
        var project = shell.OpenProject();
        var page = Assert.IsType<EnvSetsViewModel>(shell.Shell.Content);

        project.BeginAddCommand.Press();
        RenderedShell.Drain();
        Assert.False(project.HasNewKeyProblem);

        project.ConfirmAddCommand.Press();
        RenderedShell.Drain();

        Assert.Equal("The key needs a name. Type one, such as STRIPE_KEY.", shell.Named<TextBlock>("NewKeyProblem").Text);
        var drawn = (Avalonia.Media.ISolidColorBrush)shell.Named<TextBlock>("NewKeyProblem").Foreground!;
        Assert.True(shell.Window.TryFindResource("KpDanger", shell.Window.ActualThemeVariant, out var danger), "no danger colour");
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)danger!).Color, drawn.Color);
        Assert.Null(page.Error);

        project.NewKey = "STRIPE_KEY";
        Assert.False(project.HasNewKeyProblem);

        project.CancelAddCommand.Press();
        Assert.False(project.HasNewKeyProblem);
    });

    [Fact]
    public Task A_replacement_the_generator_cannot_make_says_so_under_its_field_and_not_at_the_top_of_the_page() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-replace-problem-");
        var project = shell.OpenProject();
        var page = Assert.IsType<EnvSetsViewModel>(shell.Shell.Content);
        project.BeginReplace(project.Variables[0]);
        RenderedShell.Drain();

        project.Generator.UseWords = true;
        project.Generator.Separator = "too long";
        project.GenerateIntoCommand.Press(project.ReplacementValue);
        RenderedShell.Drain();

        Assert.Equal(project.Generator.Error, shell.Named<TextBlock>("ReplaceProblem").Text);
        Assert.Null(page.Error);

        project.CancelReplaceCommand.Press();
        Assert.False(project.HasReplaceProblem);
    });

    [Fact]
    public Task Item_commands_are_off_while_the_pane_or_the_section_they_act_on_is_hidden() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-hidden-pane-");
        var entries = OpenGithub(shell);
        AppCommand Command(string header) => Flat(shell.Commands.Menus.SelectMany(menu => menu.Entries)).First(command => command.Header == header);
        string[] pane = ["Rotate", "Edit fields…", "Add field…", "Show history", "Copy password", "Copy URL", "Copy the reference", "Rename or move…", "Duplicate", "Share…"];

        Assert.All(pane, header => Assert.True(Command(header).CanRun(), $"{header} is off on an open item"));

        entries.BeginAddCommand.Press();
        RenderedShell.Drain();
        Assert.All(pane, header => Assert.False(Command(header).CanRun(), $"{header} is on behind New item's form"));
        entries.CancelAddCommand.Press();
        RenderedShell.Drain();

        entries.Detail!.EditCommand.Press();
        RenderedShell.Drain();
        Assert.All(new[] { "Add field…", "Add attachment…", "Show history" }, header => Assert.False(Command(header).CanRun(), $"{header} is on behind an edit"));
        entries.Detail.CancelCommand.Press();
    });

    private static IEnumerable<AppCommand> Flat(IEnumerable<AppMenuEntry> entries) =>
        entries.SelectMany(entry => entry switch
        {
            AppCommand command => [command],
            AppSubmenu submenu => Flat(submenu.Entries()),
            _ => Enumerable.Empty<AppCommand>(),
        });

    [Fact]
    public Task A_group_name_says_what_is_wrong_under_its_field_as_it_is_typed() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-group-name-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        entries.BeginCreateGroupCommand.Press();
        RenderedShell.Drain();
        entries.ConfirmCreateGroupCommand.Press();
        RenderedShell.Drain();

        Assert.Equal("A group needs a name. Type one.", shell.Named<TextBlock>("NewGroupProblem").Text);
        Assert.Null(entries.Error);

        entries.DraftGroupName = "bad‮name";
        Assert.Contains("Try", entries.GroupProblem, StringComparison.Ordinal);

        entries.DraftGroupName = "servers";
        Assert.False(entries.HasGroupProblem);

        entries.CancelGroupCommand.Press();
        Assert.False(entries.HasGroupProblem);
    });

    [Fact]
    public Task A_new_items_kind_is_a_segmented_control_with_a_label_and_its_refusals_sit_under_their_fields() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Press();
        RenderedShell.Drain();
        var form = entries.NewItem!;

        var kind = shell.Named<ListBox>("NewItemTemplate");
        Assert.Contains("segmented", kind.Classes);
        Assert.DoesNotContain(kind.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("selected-bar"));

        form.SelectedTemplate = form.Templates.Single(choice => choice.Template == ItemTemplate.ApiKey);
        form.Title = "stripe";
        RenderedShell.Drain();

        Assert.Null(form.Create());
        Assert.True(form.HasKeyNameProblem);
        Assert.False(form.HasError);

        form.KeyName = "STRIPE_KEY";
        Assert.False(form.HasKeyNameProblem);

        Assert.Null(form.Create());
        RenderedShell.Drain();
        Assert.Equal("Type or paste the key's value.", shell.Named<TextBlock>("NewItemKeyValueProblem").Text);
        Assert.False(form.HasError);
    });

    [Fact]
    public Task A_note_draws_on_ink_and_a_problem_on_the_danger_tint_never_amber() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-notes-");
        var entries = OpenGithub(shell);

        entries.DeleteCommand.Press();
        RenderedShell.Drain();

        var notes = shell.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("note")).ToList();
        Assert.All(notes, note => Assert.False(note.Background is Avalonia.Media.ISolidColorBrush { Color: { R: 0xF2, G: 0xB5, B: 0x44 } }, "a note is tinted amber"));
        Assert.Contains(notes, note => note.Classes.Contains("problem"));
        Assert.Contains(notes, note => note.IsEffectivelyVisible && !note.Classes.Contains("problem"));
    });

    [Fact]
    public Task The_lock_screens_password_has_a_label_above_it_and_is_named_for_its_purpose() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-lock-label-");
        shell.PressLock();
        RenderedShell.Drain();

        var password = shell.Named<MaskedInput>("Password");

        Assert.Equal("Master password", shell.Named<TextBlock>("PasswordLabel").Text);
        Assert.Equal("Master password", AutomationProperties.GetName(password));
        Assert.Empty(LabelledFields.Problems(shell.Window));
    });

    [GeneratedRegex(@"<ctl:MaskedInput\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex MaskedField();

    [GeneratedRegex(@"AutomationProperties\.Name=""\{x:Static res:Strings\.\w+\}""")]
    private static partial Regex FixedName();
}
