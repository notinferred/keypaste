using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Keypaste.App.Controls;

/// <summary>
/// A ListBox that tells screen readers its rows changed whenever a row is shown or taken away.
/// </summary>
/// <remarks>
/// Avalonia 12.1's AT-SPI bridge lists a ListBox's rows as the list's own children but reads them again only when the
/// ListBox's peer says its children changed, which Avalonia's peer says only for the ListBox's template, so on Linux a
/// row added during a session never reached a screen reader (F.68).
/// </remarks>
internal sealed class AccessibleListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new RowsAutomationPeer(this);

    private sealed class RowsAutomationPeer : ListBoxAutomationPeer
    {
        private bool _pending;

        public RowsAutomationPeer(AccessibleListBox owner)
            : base(owner)
        {
            owner.ContainerPrepared += (_, _) => RowsChanged();
            owner.ContainerIndexChanged += (_, _) => RowsChanged();
            owner.ContainerClearing += (_, _) => RowsChanged();
        }

        // Once per change, after the panel has shown or hidden every row it touched.
        private void RowsChanged()
        {
            if (_pending)
            {
                return;
            }

            _pending = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _pending = false;
                    InvalidateChildren();
                },
                DispatcherPriority.Background);
        }
    }
}
