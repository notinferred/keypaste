using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The claims about the organize controls that are only true of a real visual tree.
/// </summary>
/// <remarks>
/// <para>
/// A compiled binding to a property that does not exist is already a build error, so this is not
/// here to check binding paths — with one exception. The pane's "Rename" button reaches
/// across data contexts to the screen's own command, and an ancestor selector resolves at runtime:
/// it fails as a button that does nothing, which nothing else would notice.
/// </para>
/// <para>
/// <b>It has already earned its place.</b> The button was first written with
/// <c>$parent[ContentControl]</c>, copied from <c>EnvSetsView</c>. A <c>ScrollViewer</c> is a
/// <c>ContentControl</c>, so the selector stopped at the pane's own scroll viewer, the cast to the
/// screen's view model produced null, and the button was inert. It names
/// <c>$parent[views:EntriesView]</c> now, which has exactly one match.
/// </para>
/// </remarks>
public sealed class EntriesViewOrganizeTests : IDisposable
{
    internal const string Master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public EntriesViewOrganizeTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-organize-view-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = ClaimedVault.Create(_vaultPath, Master);
        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            Username = "dba@example.test",
            Password = "p",
            GroupPath = "servers",
        });
        vault.Save();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public Task The_organize_panel_is_drawn_when_it_opens() => HeadlessSession.On(() =>
    {
        using var context = New();
        var window = Shown(context);

        // The form is in the pane once an entry is open, hidden until asked for. A collapsed Border
        // keeps its children in the tree, so this checks effective visibility.
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "production");
        window.UpdateLayout();

        var title = Named<TextBox>(window, "OrganizeTitle");
        Assert.NotNull(title);
        Assert.False(title!.IsEffectivelyVisible);

        context.Entries.OrganizeCommand.Press();
        window.UpdateLayout();

        Assert.True(title.IsEffectivelyVisible);
        Assert.Equal("production", title.Text, StringComparer.Ordinal);
        Assert.Null(Named<ComboBox>(window, "OrganizeGroup"));
    });

    /// <summary>
    /// The pane's button reaches the screen's command across two data contexts.
    /// </summary>
    /// <remarks>
    /// The one binding here that a build cannot check. Executing it is the only way to find out
    /// whether the ancestor selector resolved to the screen rather than to something nearer.
    /// </remarks>
    [Fact]
    public Task The_pane_button_opens_the_screens_organize_form() => HeadlessSession.On(() =>
    {
        using var context = New();
        var window = Shown(context);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "production");
        window.UpdateLayout();

        Assert.False(context.Entries.IsOrganizing);

        PressInPaneMenu(window, "OrganizeFromPane");

        Assert.True(context.Entries.IsOrganizing);
    });

    /// <summary>The pane's Duplicate reaches the screen's command across the same two data contexts, and the copy is selected.</summary>
    [Fact]
    public Task The_pane_button_duplicates_the_item() => HeadlessSession.On(() =>
    {
        using var context = New();
        var window = Shown(context);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "production");
        window.UpdateLayout();

        PressInPaneMenu(window, "DuplicateEntry");

        Assert.Null(context.Entries.Error);
        Assert.Equal(new EntryName("servers", "production (copy)"), context.Entries.Selected?.Name);
    });

    private static void PressInPaneMenu(Window window, string name)
    {
        var (menu, _) = Menus.Open(Named<Button>(window, "EntryMenu")!);
        var item = Menus.Item(menu, name);
        item.Command!.Press(item.CommandParameter);
        menu.Hide();
    }

    private static T? Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private static Window Shown(UnlockedScreens context)
    {
        var window = new Window { Content = new EntriesView { DataContext = context.Entries } };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private UnlockedScreens New() => new(_vaultPath, Master);
}
