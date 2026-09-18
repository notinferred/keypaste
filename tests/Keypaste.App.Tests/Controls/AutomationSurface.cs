using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// Everything a process on the accessibility bus, or a reader of a control's own state, can get
/// back from it.
/// </summary>
/// <remarks>
/// <para>
/// Shared because D-0099 is a claim about every field a secret reaches, and there are seven of them
/// now: the three master-password fields <see cref="MaskedInputAutomationTests"/> covers and the
/// four <see cref="SecretFieldAutomationTests"/> added in 4.9. A second copy of this sweep would be
/// a second thing to weaken, and the differentials it backs would go on passing against it.
/// </para>
/// <para>
/// <b>It is proved by the tests that require it to fail.</b> Those live beside the master-password
/// tests, where the controls that leak on purpose already are.
/// </para>
/// </remarks>
internal static class AutomationSurface
{
    /// <summary>Fails if anything the sweep reaches contains <paramref name="secret"/>.</summary>
    internal static void AssertNothingExposes(Control control, string secret)
    {
        foreach (var (source, text) in Of(control))
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                Assert.Fail($"{source} exposes the fixture password");
            }
        }
    }

    /// <summary>
    /// Peer by peer down the tree, then every attached automation property and every registered
    /// styled property on every element in it.
    /// </summary>
    internal static List<(string Source, string Text)> Of(Control control)
    {
        var found = new List<(string Source, string Text)>();

        Walk(ControlAutomationPeer.CreatePeerForElement(control), found);

        foreach (var element in Descendants(control))
        {
            var what = element.GetType().Name;

            Add(found, $"AutomationProperties.Name on {what}", AutomationProperties.GetName(element));
            Add(found, $"AutomationProperties.HelpText on {what}", AutomationProperties.GetHelpText(element));
            Add(found, $"AutomationProperties.ItemStatus on {what}", AutomationProperties.GetItemStatus(element));
            Add(found, $"AutomationProperties.ItemType on {what}", AutomationProperties.GetItemType(element));
            Add(found, $"AutomationProperties.AutomationId on {what}", AutomationProperties.GetAutomationId(element));
            Add(found, $"AutomationProperties.AcceleratorKey on {what}", AutomationProperties.GetAcceleratorKey(element));
            Add(found, $"AutomationProperties.AccessKey on {what}", AutomationProperties.GetAccessKey(element));

            foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(element))
            {
                Add(found, $"the {property.Name} styled property on {what}", element.GetValue(property)?.ToString());
            }
        }

        return found;
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        new[] { control }.Concat(control.GetVisualDescendants().OfType<Control>());

    private static void Walk(AutomationPeer peer, List<(string Source, string Text)> found)
    {
        var what = peer.GetType().Name;

        Add(found, $"{what}.GetName", peer.GetName());
        Add(found, $"{what}.GetHelpText", peer.GetHelpText());
        Add(found, $"{what}.GetItemStatus", peer.GetItemStatus());
        Add(found, $"{what}.GetItemType", peer.GetItemType());
        Add(found, $"{what}.GetAutomationId", peer.GetAutomationId());
        Add(found, $"{what}.GetClassName", peer.GetClassName());
        Add(found, $"{what}.GetLocalizedControlType", peer.GetLocalizedControlType());
        Add(found, $"{what}.GetAcceleratorKey", peer.GetAcceleratorKey());
        Add(found, $"{what}.GetAccessKey", peer.GetAccessKey());
        Add(found, "IValueProvider.Value", peer.GetProvider<IValueProvider>()?.Value);

        foreach (var child in peer.GetChildren())
        {
            Walk(child, found);
        }
    }

    private static void Add(List<(string Source, string Text)> found, string source, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            found.Add((source, text));
        }
    }
}
