using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The claims about the organize controls that are only true of a real visual tree.
/// </summary>
/// <remarks>
/// <para>
/// A compiled binding to a property that does not exist is already a build error, so this is not
/// here to check binding paths — with one exception. The pane's "Rename or move" button reaches
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

        using var vault = Vault.Create(_vaultPath, Master);
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

        // The form is the open entry's, so it is in the pane once an entry is. Hidden until asked
        // for, which is what the panels share with the add form. A collapsed Border keeps its
        // children in the tree, so this is effective visibility.
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "production");
        window.UpdateLayout();

        var title = Named<TextBox>(window, "OrganizeTitle");
        Assert.NotNull(title);
        Assert.False(title!.IsEffectivelyVisible);

        context.Entries.OrganizeCommand.Execute(null);
        window.UpdateLayout();

        Assert.True(title.IsEffectivelyVisible);
        Assert.Equal("production", title.Text, StringComparer.Ordinal);

        var group = Named<ComboBox>(window, "OrganizeGroup");
        Assert.NotNull(group);
        Assert.NotEmpty(group!.ItemsSource!.Cast<object>());
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

        var button = Named<Button>(window, "OrganizeFromPane");
        Assert.NotNull(button);
        Assert.False(context.Entries.IsOrganizing);

        button!.Command!.Execute(button.CommandParameter);

        Assert.True(context.Entries.IsOrganizing);
    });

    [Fact]
    public Task The_group_forms_are_drawn_when_they_open() => HeadlessSession.On(() =>
    {
        using var context = New();
        var window = Shown(context);

        context.Entries.BeginCreateGroupCommand.Execute(null);
        window.UpdateLayout();
        Assert.True(Named<TextBox>(window, "NewGroupName")!.IsEffectivelyVisible);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(node => node.Path == "servers");
        context.Entries.BeginRenameGroupCommand.Execute(null);
        window.UpdateLayout();

        Assert.True(Named<TextBox>(window, "RenameGroupName")!.IsEffectivelyVisible);

        // An ordinary group is not a project, so the line about `keypaste run` stays off.
        Assert.False(Named<TextBlock>(window, "ProjectRenameNote")!.IsEffectivelyVisible);
    });

    private static T? Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private static Window Shown(Context context)
    {
        var window = new Window { Content = new EntriesView { DataContext = context.Entries } };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private Context New() => new(_vaultPath);

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(Master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
