using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What a screen reader gets: every field and row named, headings levelled and each message said once (Q.7).</summary>
public sealed class AssistiveTechnologyTests
{
    public static TheoryData<int> Places => new()
    {
        (int)DestinationKind.Entries,
        (int)DestinationKind.EnvSets,
        (int)DestinationKind.AgentActivity,
        (int)DestinationKind.Log,
        (int)DestinationKind.Settings,
        (int)DestinationKind.Trash,
        (int)DestinationKind.Tokens,
        (int)DestinationKind.Sharing,
        (int)DestinationKind.Advanced,
    };

    [Theory]
    [MemberData(nameof(Places))]
    public Task Every_field_and_row_in_a_place_is_named_and_holds_no_secret(int place) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-named-");
        shell.Shell.Current = Destinations.All.Single(destination => destination.Kind == (DestinationKind)place);
        RenderedShell.Drain();

        AssertNamed(shell.Window);

        foreach (var secret in RenderedShell.Secrets)
        {
            AutomationSurface.AssertNothingExposes(shell.Window, secret);
        }
    });

    [Fact]
    public Task The_new_items_fields_are_named() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-form-named-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        Assert.NotNull(entries.NewItem);
        AssertNamed(shell.Window);
    });

    [Fact]
    public Task An_open_items_pane_and_an_open_project_are_named() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-open-named-");

        shell.OpenPane();
        RenderedShell.Drain();
        AssertNamed(shell.Window);

        shell.OpenProject();
        RenderedShell.Drain();
        AssertNamed(shell.Window);
    });

    [Fact]
    public Task The_unlock_screen_is_named() => HeadlessSession.On(() =>
    {
        using var unlock = new RenderedUnlock();

        AssertNamed(unlock.Window);
    });

    [Fact]
    public Task An_items_row_says_its_columns_and_never_its_notes() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-row-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var row = shell.Named<ListBox>("Rows").GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is EntryRow { Title: "github" });

        var name = ControlAutomationPeer.CreatePeerForElement(row).GetName();

        Assert.StartsWith("github, me, https://github.com/", name);
        Assert.Contains(((EntryRow)row.DataContext!).KindLabel, name);
        Assert.DoesNotContain("API_TOKEN", name);
    });

    [Fact]
    public Task The_sidebars_rows_say_their_level_and_its_fold_says_what_it_will_do() => HeadlessSession.On(() =>
    {
        var folded = new GroupRow("work", "work", 1, HasChildren: true, IsExpanded: false);
        var open = new NavItem(Destinations.All.Single(destination => destination.Kind == DestinationKind.Entries)) { IsExpandable = true };

        Assert.Equal("work, level 2", folded.ToString());
        Assert.Equal("Show the groups in work", folded.FoldLabel);
        Assert.StartsWith("Items, level 1", open.ToString());
        Assert.Equal("Hide the groups", open.FoldLabel);

        open.IsExpanded = false;

        Assert.Equal("Show the groups", open.FoldLabel);
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_Agents_dot_and_a_tag_say_their_state_in_words() => HeadlessSession.On(() =>
    {
        var agents = new NavItem(Destinations.All.Single(destination => destination.Kind == DestinationKind.AgentActivity)) { HasDot = true };

        Assert.Empty(agents.DotWords);
        agents.DotLive = true;
        Assert.Equal("live", agents.DotWords);
        agents.DotDanger = true;
        Assert.Equal("refusing", agents.DotWords);

        Assert.Equal("billing · prod, protected", new EntryTagChip("env:billing:prod", _ => { }).Spoken);
        Assert.EndsWith(", malformed", new EntryTagChip("env:", _ => { }).Spoken);
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_heading_carries_its_level_from_its_type_class() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-heading-");
        shell.Show<SettingsViewModel>(DestinationKind.Settings);

        var heading = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("h1") && text.Text == "Settings");
        var section = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("dialog-title") && text.IsEffectivelyVisible);

        Assert.Equal(1, ControlAutomationPeer.CreatePeerForElement(heading).GetHeadingLevel());
        Assert.Equal(2, ControlAutomationPeer.CreatePeerForElement(section).GetHeadingLevel());
    });

    [Fact]
    public Task Each_toast_is_said_once() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-toast-said-");
        var said = Listen(shell.Window);

        shell.Shell.ShowDone("Moved github.");
        RenderedShell.Drain();

        Assert.Equal(["Moved github."], said);

        shell.Shell.ShowDone("Deleted github.");
        RenderedShell.Drain();

        Assert.Equal(["Moved github.", "Deleted github."], said);
    });

    [Fact]
    public Task A_refused_unlock_is_said_and_said_again_on_the_next_refusal() => HeadlessSession.On(async () =>
    {
        using var unlock = new RenderedUnlock();
        var said = Listen(unlock.Window);

        foreach (var attempt in Enumerable.Range(1, 2))
        {
            await Refuse(unlock);
            WindowInput.Drain();
            Assert.Equal(attempt, said.Count);
        }

        Assert.All(said, line => Assert.Equal(unlock.Model.Message, line));
    });

    [Fact]
    public Task A_prompt_says_what_it_asks_and_its_time_limit_once_while_its_countdown_ticks() => HeadlessSession.On(() =>
    {
        var model = new ApprovalViewModel(ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploy", 3600, "label"));
        model.Tick(TimeSpan.FromMinutes(2));
        var window = new ApprovalWindow(model);
        window.Show();
        var said = Listen(window);
        WindowInput.Drain();

        model.Tick(TimeSpan.FromSeconds(119));
        WindowInput.Drain();

        var line = Assert.Single(said);
        Assert.Contains("claude-code wants 1 secret", line);
        Assert.EndsWith("2 minutes to answer.", line);
        window.Close();
    });

    private static List<string> Listen(Window window)
    {
        var live = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("live"));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(live));
        var said = new List<string>();
        ControlAutomationPeer.CreatePeerForElement(live).PropertyChanged += (_, change) =>
        {
            if (change.Property == AutomationElementIdentifiers.NameProperty && change.NewValue is string { Length: > 0 } line)
            {
                said.Add(line);
            }
        };

        return said;
    }

    private static void AssertNamed(Window window)
    {
        var unnamed = new List<string>();

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            var isField = control is TextBox or MaskedInput or ComboBox or ListBox or NumericUpDown or CalendarDatePicker && control.TemplatedParent is null;
            var isRow = control is ListBoxItem;

            if (!isField && !isRow)
            {
                continue;
            }

            var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();

            if (string.IsNullOrWhiteSpace(name) || name.Contains('{', StringComparison.Ordinal) || name.StartsWith("Keypaste.", StringComparison.Ordinal))
            {
                unnamed.Add($"{control.GetType().Name} {control.Name} ({control.DataContext?.GetType().Name}): '{name}'");
            }
        }

        Assert.True(unnamed.Count == 0, string.Join("\n", unnamed));
    }

    private static async Task Refuse(RenderedUnlock unlock)
    {
        unlock.Model.Type('x');
        await unlock.Model.UnlockAsync();
    }
}
