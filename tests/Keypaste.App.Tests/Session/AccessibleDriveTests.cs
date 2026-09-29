using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Primitives;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The acts the installed-app exercise drives from outside the process (F.25), found by accessible
/// name and done through the automation tree UI Automation and AT-SPI read, never by calling a command.
/// </summary>
/// <remarks>
/// Each find takes the first visible element with that name and control type in tree order, as
/// <c>drive-desktop-windows.ps1</c> and <c>drive-desktop-linux.py</c> do.
/// </remarks>
public sealed class AccessibleDriveTests
{
    [Fact]
    public Task An_entry_is_edited_a_key_added_and_the_vault_locked_by_name() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-drive-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);

        Select(shell, "github");
        Named<IToggleProvider>(shell, "More actions", AutomationControlType.Button).Toggle();
        Named<IInvokeProvider>(shell, "Edit fields", AutomationControlType.Button).Invoke();
        After(shell, "Username").SetValue("edited-by-name");
        Named<IInvokeProvider>(shell, "Save", AutomationControlType.Button).Invoke();
        RenderedShell.Drain();

        Assert.Equal("edited-by-name", shell.Session.Unlocked!.Find(new EntryName(string.Empty, "github"))!.Username);

        Select(shell, "billing");
        Named<IInvokeProvider>(shell, "Add a key to dev", AutomationControlType.Button).Invoke();
        Named<IValueProvider>(shell, "New key", AutomationControlType.Edit).SetValue("APP_ADDED");
        Named<IInvokeProvider>(shell, "Add", AutomationControlType.Button).Invoke();
        RenderedShell.Drain();

        Assert.Contains(new EnvStore(shell.Session.Unlocked!).Read("billing", "dev"), variable => variable.Key == "APP_ADDED");

        Named<IInvokeProvider>(shell, "Lock now", AutomationControlType.Button).Invoke();
        RenderedShell.Drain();

        Assert.True(shell.IsLocked);
    });

    [Fact]
    public Task No_control_on_the_main_screens_is_announced_by_its_type() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-names-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        shell.Named<ToggleButton>("EntryMenu").IsChecked = true;
        shell.Named<ToggleButton>("AddEntry").IsChecked = true;
        AssertNamedByText(shell, "Items");

        foreach (var kind in new[] { DestinationKind.AgentActivity, DestinationKind.AgentHistory, DestinationKind.Trash, DestinationKind.Settings, DestinationKind.Log, DestinationKind.Sharing, DestinationKind.Tokens, DestinationKind.Diagnostics })
        {
            shell.Shell.Current = Destinations.Of(kind);
            AssertNamedByText(shell, kind.ToString());
        }

        shell.Shell.OpenProjectCommand.Execute("billing");
        AssertNamedByText(shell, "a project");

        shell.PressLock();
        AssertNamedByText(shell, "the lock screen");
    });

    /// <summary>
    /// Fails on a name a reader would say that is a type rather than words: a control whose content is
    /// an icon and text, or a row whose content is a record, is otherwise announced by its type's name.
    /// </summary>
    private static void AssertNamedByText(RenderedShell shell, string where)
    {
        RenderedShell.Drain();
        var window = ControlAutomationPeer.CreatePeerForElement(shell.Window);
        var types = Visible(window)
            .Select(peer => peer.GetName() ?? string.Empty)
            .Where(name => name.StartsWith("Avalonia.", StringComparison.Ordinal)
                || name.StartsWith("Keypaste.", StringComparison.Ordinal)
                || name.Contains(" { ", StringComparison.Ordinal))
            .ToList();

        Assert.True(types.Count == 0, $"on {where}, controls are announced as {string.Join(", ", types)}");
    }

    private static IEnumerable<AutomationPeer> Visible(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            if (child is ControlAutomationPeer { Owner.IsEffectivelyVisible: true })
            {
                yield return child;

                foreach (var descendant in Visible(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static AutomationPeer Find(RenderedShell shell, string name, AutomationControlType type)
    {
        RenderedShell.Drain();
        var window = ControlAutomationPeer.CreatePeerForElement(shell.Window);
        var peer = Visible(window).FirstOrDefault(peer => peer.GetName() == name && peer.GetAutomationControlType() == type);
        return peer ?? throw new Xunit.Sdk.XunitException($"nothing visible is a {type} named '{name}'");
    }

    private static T Named<T>(RenderedShell shell, string name, AutomationControlType type)
        where T : class =>
        Find(shell, name, type) as T ?? throw new Xunit.Sdk.XunitException($"the {type} named '{name}' is not {typeof(T).Name}");

    /// <summary>A list item holding the text, selected as the drivers do.</summary>
    private static void Select(RenderedShell shell, string text)
    {
        var peer = Find(shell, text, AutomationControlType.Text);

        while (peer is not null && peer.GetAutomationControlType() != AutomationControlType.ListItem)
        {
            peer = peer.GetParent();
        }

        Assert.NotNull(peer);
        Assert.IsAssignableFrom<ISelectionItemProvider>(peer).Select();
        RenderedShell.Drain();
    }

    /// <summary>The first field after the label, among the label's siblings.</summary>
    private static IValueProvider After(RenderedShell shell, string label)
    {
        var text = Find(shell, label, AutomationControlType.Text);
        var siblings = text.GetParent()!.GetChildren();
        var field = siblings.Skip(siblings.ToList().IndexOf(text) + 1)
            .FirstOrDefault(peer => peer.GetAutomationControlType() == AutomationControlType.Edit);

        return field as IValueProvider ?? throw new Xunit.Sdk.XunitException($"no field after '{label}'");
    }
}
