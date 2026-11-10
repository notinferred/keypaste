using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// Each row's context menu (Q.3) opens from a right-click and from Shift+F10 or the menu key, offers the row's
/// commands from the menus, and runs them on that row.
/// </summary>
public sealed class ContextMenuTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    [Fact]
    public Task An_items_menu_offers_its_commands_and_duplicates_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-item-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var row = Container(shell.Named<ListBox>("Rows"), entries.Rows.Single(entry => entry.Title == "github"));

        var menu = RightClick(shell.Window, row);
        Assert.Equal("github", entries.Selected?.Title);
        Assert.Equal(
            ["Copy username", "Copy password", "Copy the TOTP code", "Copy URL", "Copy the reference", "Open URL", "-", "Edit fields…", "Rename or move…", "Duplicate", "Share…", "Rotate", "Generate a new password…", "Show history", "-", "Delete"],
            Headers(menu));
        Assert.All(menu.Items.OfType<MenuItem>().Where(item => item.Header as string != "Copy the TOTP code"), item => Assert.True(item.IsEffectivelyEnabled, $"{item.Header} is off"));
        menu.Hide();

        menu = FromTheKeyboard(shell.Window, row, PhysicalKey.F10, RawInputModifiers.Shift);
        Choose(menu, "Duplicate");

        Assert.Equal(2, entries.Rows.Count(entry => entry.Title.StartsWith("github", StringComparison.Ordinal)));
    });

    [Fact]
    public Task A_variables_menu_copies_through_the_clipboards_countdown_and_names_no_value() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-variable-");
        var project = shell.OpenProject();
        var row = shell.Window.GetVisualDescendants().OfType<Border>().First(border => border.DataContext is EnvKeyRow && border.Classes.Contains("matrix-row"));

        var menu = RightClick(shell.Window, row);
        Assert.Equal(["Copy value", "Replace value…", "-", "Remove key"], Headers(menu));
        Choose(menu, "Copy value");
        WindowInput.Drain();
        Assert.True(shell.Clipboard.ContentWasSetAsASecret);

        Choose(RightClick(shell.Window, row), "Replace value…");
        Assert.True(project.IsReplacing);
    });

    [Fact]
    public Task A_key_rows_menu_acts_on_the_row_it_opened_on_and_not_the_one_holding_the_keyboard() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-key-rows-", vault => ProjectVariables.Set(vault, "billing", "OTHER_KEY", "other"));
        var project = shell.OpenProject();
        var rows = shell.Window.GetVisualDescendants().OfType<Border>().Where(border => border.DataContext is EnvKeyRow && border.Classes.Contains("matrix-row")).ToList();
        Assert.True(rows.Count >= 2, "the project needs two keys");

        rows[0].GetVisualDescendants().OfType<Button>().First(button => button.DataContext is EnvVariableRow && button.IsEffectivelyVisible).Focus();
        RenderedShell.Drain();

        Choose(RightClick(shell.Window, rows[1]), "Replace value…");

        Assert.Equal(((EnvKeyRow)rows[1].DataContext!).Variable, project.Replacing);
        Assert.True(shell.Named<MaskedInput>("ReplacementEnvValue").IsFocused, "the form did not take the keyboard");
        project.CancelReplaceCommand.Press();
    });

    [Fact]
    public Task A_rows_menu_does_not_open_over_the_question_about_a_draft_and_the_menus_go_off_behind_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-discard-", vault => vault.AddEntry(new VaultEntry { Title = "second", Username = "me", Password = "pw" }));
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(entry => entry.Title == "github");
        RenderedShell.Drain();
        entries.Detail!.EditCommand.Press();
        entries.Detail.DraftUsername = "someone-else";
        RenderedShell.Drain();

        var other = entries.Rows.Single(entry => entry.Title == "second");
        Assert.Null(shell.Commands.ContextMenu(other));

        Assert.True(shell.Shell.HasDiscardPrompt);
        Assert.Equal("github", entries.Selected?.Title);
        Assert.Null(shell.Commands.ForChord(shell.Commands.Mac ? Avalonia.Input.Key.Back : Avalonia.Input.Key.Delete, shell.Commands.Mac ? Avalonia.Input.KeyModifiers.Meta : Avalonia.Input.KeyModifiers.None));
        Assert.Null(shell.Commands.ContextMenu(entries.Rows.Single(entry => entry.Title == "github")));

        shell.Shell.KeepDraftCommand.Press();
        RenderedShell.Drain();
        entries.Detail.CancelCommand.Press();
    });

    [Fact]
    public Task Forms_opened_from_a_rows_menu_take_the_keyboard() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-forms-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var row = Container(shell.Named<ListBox>("Rows"), entries.Rows.Single(entry => entry.Title == "github"));
        row.Focus();
        RenderedShell.Drain();

        Choose(RightClick(shell.Window, row), "Rotate");

        Assert.True(entries.Detail!.IsConfirmingRotate);
        Assert.IsType<Button>(shell.Window.FocusManager!.GetFocusedElement());
        Assert.NotSame(row, shell.Window.FocusManager!.GetFocusedElement());
        entries.Detail.CancelRotateCommand.Press();
    });

    [Fact]
    public Task A_tokens_menu_asks_before_it_revokes() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-token-");
        var tokens = shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
        Assert.True(tokens.Create("ci", "read:acme-api/staging/*", TimeSpan.FromDays(7), allowProd: false).Ok);
        WindowInput.Drain();
        var row = tokens.Rows.Single();
        var drawn = shell.Window.GetVisualDescendants().OfType<Control>().First(control => Equals(control.DataContext, row));

        Choose(RightClick(shell.Window, drawn), "Revoke token");

        Assert.True(Assert.Single(tokens.Rows).IsConfirming);
        Assert.Contains("ghost", Assert.IsType<Button>(shell.Window.FocusManager!.GetFocusedElement()).Classes);
    });

    [Fact]
    public Task A_recommendations_rows_menu_dismisses_it_and_reviews_it_again() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(
            "keypaste-context-finding-",
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "p", Notes = "STRIPE_SECRET_KEY=sk_live_SENTINEL4f6a" }));
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);
        var recommendations = shell.Show<RecommendationsViewModel>(DestinationKind.Recommendations);
        var row = recommendations.Rows.Single();
        var drawn = shell.Window.GetVisualDescendants().OfType<Control>().First(control => Equals(control.DataContext, row));

        var menu = RightClick(shell.Window, drawn);
        Assert.Equal(["Move to a field", "-", "Dismiss", "Review again"], Headers(menu));
        Assert.False(Item(menu, "Review again").IsEffectivelyEnabled);
        Choose(menu, "Dismiss");
        RenderedShell.Drain();

        var dismissed = recommendations.Rows.Single();
        Assert.True(dismissed.IsDismissed);

        var again = RightClick(shell.Window, shell.Window.GetVisualDescendants().OfType<Control>().First(control => Equals(control.DataContext, dismissed)));
        Assert.False(Item(again, "Move to a field").IsEffectivelyEnabled);
        Choose(again, "Review again");
        Assert.False(recommendations.Rows.Single().IsDismissed);
    });

    [Fact]
    public Task A_tags_menu_removes_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(
            "keypaste-context-tag-",
            seed: vault =>
            {
                vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Tagged", Password = "p" });
                vault.AddTag(new EntryName("services", "Tagged"), "blue");
            });
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "Tagged");
        RenderedShell.Drain();
        var chip = entries.Detail!.Tags.Single();
        var drawn = shell.Window.GetVisualDescendants().OfType<Control>().First(control => Equals(control.DataContext, chip) && control.IsEffectivelyVisible);

        Choose(RightClick(shell.Window, drawn), chip.RemoveLabel);

        Assert.Empty(entries.Detail!.Tags);
    });

    [Fact]
    public Task A_Trash_rows_menu_restores_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-trash-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(entry => entry.Title == "github");
        entries.DeleteCommand.Press();
        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        var row = Container(shell.Window.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == trash.Rows), trash.Rows.Single());

        var menu = RightClick(shell.Window, row);
        Assert.Equal(["Restore", "-", "Delete for good", "Empty Trash"], Headers(menu));
        menu.Hide();

        Choose(FromTheKeyboard(shell.Window, row, PhysicalKey.ContextMenu, RawInputModifiers.None), "Restore");

        Assert.Empty(trash.Rows);
    });

    [Fact]
    public Task A_revisions_menu_copies_its_password() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-revision-");
        shell.Open("revision");
        var history = Assert.IsType<EntriesViewModel>(shell.Shell.Content).Detail!.History;
        var row = Container(shell.Named<ListBox>("Revisions"), history.Rows[0]);

        var menu = RightClick(shell.Window, row);
        Assert.Equal(["Copy this revision's password", "Restore this revision"], Headers(menu));
        menu.Hide();

        Choose(FromTheKeyboard(shell.Window, row, PhysicalKey.F10, RawInputModifiers.Shift), "Copy this revision's password");

        Assert.Equal(RenderedShell.Superseded, shell.Clipboard.Content);
    });

    /// <summary>A sidebar group's menu shows the group in Items, and a choice opens Items' form for it with the keyboard in it.</summary>
    [Fact]
    public Task A_groups_menu_opens_its_form_with_the_keyboard_in_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-group-");
        var nav = shell.Named<ListBox>("Nav");
        var env = shell.Shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env");

        var menu = RightClick(shell.Window, Container(nav, env));
        Assert.Equal(["New group…", "Rename group…", "Move group…", "-", "Delete group"], Headers(menu));
        Assert.Equal("env", Assert.IsType<EntriesViewModel>(shell.Shell.Content).SelectedGroup?.Path);
        Choose(menu, "Rename group…");

        Assert.True(Assert.IsType<EntriesViewModel>(shell.Shell.Content).IsRenamingGroup);
        Assert.True(shell.Named<TextBox>("RenameGroupName").IsFocused);

        Assert.IsType<EntriesViewModel>(shell.Shell.Content).CancelGroupCommand.Press();
        Choose(FromTheKeyboard(shell.Window, Container(nav, env), PhysicalKey.F10, RawInputModifiers.Shift), "New group…");

        Assert.True(Assert.IsType<EntriesViewModel>(shell.Shell.Content).IsCreatingGroup);
        Assert.True(shell.Named<TextBox>("NewGroupName").IsFocused);
    });

    [Fact]
    public Task A_projects_menu_opens_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-project-");
        var nav = shell.Named<ListBox>("Nav");
        var billing = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");

        var menu = RightClick(shell.Window, Container(nav, billing));
        Assert.Equal(["Open project", "-", "Add a key…", "Add environment…", "Add item…", "-", "Run in a terminal", "Open terminal", "Copy the command", "Export .env.keypaste…"], Headers(menu));
        Choose(menu, "Open project");

        Assert.Equal("billing", Assert.IsType<EnvSetsViewModel>(shell.Shell.Content).OpenProject?.Name);
        Assert.Same(billing, shell.Shell.SelectedSidebarRow);

        var open = FromTheKeyboard(shell.Window, Container(nav, billing), PhysicalKey.ContextMenu, RawInputModifiers.None);
        Assert.False(Item(open, "Open project").IsEffectivelyEnabled);
    });

    /// <summary>An agent's menu opens its settings, and the menu bar's agent commands act on the agent the keyboard is in.</summary>
    [Fact]
    public Task An_agents_menu_opens_its_settings_and_the_menu_bar_reaches_the_focused_agent() => HeadlessSession.On(async () =>
    {
        await using var agents = await AgentsInShell.StartAsync();
        var claude = agents.Model.Rows.Single(row => row.Key == "claude-code");
        var card = agents.Card(claude);

        var menu = RightClick(agents.Window, card);
        Assert.Equal(["Agent settings…", "Connect Claude Code…", "Connect Claude Code again…", "-", "Disconnect…"], Headers(menu));
        Choose(menu, "Agent settings…");
        Assert.True(claude.IsOpen);

        var inside = card.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Focusable);
        inside.Focus(NavigationMethod.Tab);
        WindowInput.Drain();
        var open = FromTheKeyboard(agents.Window, inside, PhysicalKey.F10, RawInputModifiers.Shift);
        Assert.False(Item(open, "Agent settings…").IsEffectivelyEnabled);
        open.Hide();

        var disconnect = agents.Commands.Menus.Single(group => group.Header == "Agents").Entries.OfType<AppCommand>().Single(command => command.Header == "Disconnect…");
        Assert.True(disconnect.CanRun());
        agents.Window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "Nav").ContainerFromIndex(0)!.Focus();
        Assert.False(disconnect.CanRun());

        agents.Model.DisconnectCommand.Press(claude);
        await agents.Model.Opening;
        WindowInput.Drain();

        // On its name, since the middle of an open card is one of the pane's own controls.
        var whilePaneIsOpen = RightClick(agents.Window, card.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == claude.Title));
        Assert.False(Item(whilePaneIsOpen, "Disconnect…").IsEffectivelyEnabled);
        whilePaneIsOpen.Hide();
    });

    [Fact]
    public Task A_Control_click_opens_the_menu_on_macOS() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-context-control-");
        var host = new AppCommandHost { Session = shell.Session, Shell = () => shell.Shell, Unlock = () => null, Window = () => shell.Window };
        shell.Root.Content = new ShellView { DataContext = shell.Shell, Commands = new AppCommands(host, mac: true) };
        RenderedShell.Drain();
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var row = Container(shell.Named<ListBox>("Rows"), entries.Rows.Single(entry => entry.Title == "github"));

        var centre = Centre(shell.Window, row);
        shell.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Control);
        shell.Window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Control);
        RenderedShell.Drain();

        Assert.True(Open(row).IsOpen);
    });

    private static Control Container(ItemsControl list, object item)
    {
        RenderedShell.Drain();
        list.ScrollIntoView(item);
        RenderedShell.Drain();
        var container = Assert.IsAssignableFrom<Control>(list.ContainerFromItem(item));
        container.BringIntoView();
        RenderedShell.Drain();
        return container;
    }

    private static Point Centre(TopLevel window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static MenuFlyout RightClick(Window window, Control row)
    {
        window.UpdateLayout();
        var centre = Centre(window, row);
        window.MouseDown(centre, MouseButton.Right);
        window.MouseUp(centre, MouseButton.Right);
        RenderedShell.Drain();
        return Open(row);
    }

    private static MenuFlyout FromTheKeyboard(Window window, Control row, PhysicalKey key, RawInputModifiers modifiers)
    {
        if (!row.IsKeyboardFocusWithin)
        {
            row.Focus(NavigationMethod.Tab);
            RenderedShell.Drain();
        }

        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        RenderedShell.Drain();
        return Open(row);
    }

    // The menu is attached to the row it was opened on, or to a row's container the request rose through.
    private static MenuFlyout Open(Control row)
    {
        var menu = row.GetSelfAndVisualAncestors().OfType<Control>().Select(FlyoutBase.GetAttachedFlyout).OfType<MenuFlyout>().FirstOrDefault()
            ?? row.GetVisualDescendants().OfType<Control>().Select(FlyoutBase.GetAttachedFlyout).OfType<MenuFlyout>().FirstOrDefault();
        Assert.NotNull(menu);
        Assert.True(menu.IsOpen, "the context menu is not open");
        return menu;
    }

    private static MenuItem Item(MenuFlyout menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Header as string == header);

    private static void Choose(MenuFlyout menu, string header)
    {
        Item(menu, header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        RenderedShell.Drain();
    }

    private static IEnumerable<string> Headers(MenuFlyout menu) =>
        menu.Items.Select(item => item is MenuItem { Header: string header } ? header : "-");

    /// <summary>The unlocked shell on Agents, with an authority serving agents and the AI tools a test's computer has.</summary>
    private sealed class AgentsInShell : IAsyncDisposable
    {
        private readonly TempVault _fixture;
        private readonly AppAuthority _authority;
        private readonly ShellViewModel _shell;

        private AgentsInShell(TempVault fixture, AppAuthority authority, ShellViewModel shell, Window window, AppCommands commands)
        {
            _fixture = fixture;
            _authority = authority;
            _shell = shell;
            Window = window;
            Commands = commands;
        }

        internal Window Window { get; }

        internal AppCommands Commands { get; }

        internal AgentActivityViewModel Model => Assert.IsType<AgentActivityViewModel>(_shell.Content);

        internal static async Task<AgentsInShell> StartAsync()
        {
            var fixture = new TempVault();
            fixture.Sees();
            var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000 // The authority owns and disposes its session; the shell and window are disposed with the fixture.
            var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new AgentsFirstLevelTests.Person());

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var tools = new FakeTools(Path.Combine(fixture.Home, "computer")).ClaudeCode(connected: true);
            var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock, post: action => Dispatcher.UIThread.Post(action))
            {
                Tools = () => tools.Connector(),
            };
            var window = new Window { Width = 1280, Height = 900 };
            var commands = new AppCommands(new AppCommandHost { Session = authority.Session, Shell = () => shell, Unlock = () => null, Window = () => window });
#pragma warning restore CA2000
            window.Content = new ShellView { DataContext = shell, Commands = commands };
            window.Show();
            shell.Current = Destinations.Of(DestinationKind.AgentActivity);
            await Assert.IsType<AgentActivityViewModel>(shell.Content).Looking.WaitAsync(_wait, TestContext.Current.CancellationToken);
            Assert.IsType<AgentActivityViewModel>(shell.Content).Refresh();
            WindowInput.Drain();
            window.UpdateLayout();
            return new AgentsInShell(fixture, authority, shell, window, commands);
        }

        /// <summary>The agent's row on the screen.</summary>
        internal Control Card(AgentRow row) =>
            Container(Window.GetVisualDescendants().OfType<ItemsControl>().Single(list => list.Name == "AgentRows"), row);

        public ValueTask DisposeAsync()
        {
            Window.Close();
            _shell.Dispose();
            _authority.Dispose();
            _fixture.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
