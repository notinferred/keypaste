using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// A TextBlock that a screen reader names by its <see cref="AutomationProperties.NameProperty"/> when it has one, and
/// by its text otherwise.
/// </summary>
/// <remarks>
/// Avalonia 12.1's <c>TextBlockAutomationPeer</c> names a TextBlock by its text alone and reports every change of the
/// text as a change of name, which a live region announces (F.68).
/// </remarks>
internal sealed class NamedTextBlock : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override AutomationPeer OnCreateAutomationPeer() => new NamedTextAutomationPeer(this);

    private sealed class NamedTextAutomationPeer : ControlAutomationPeer
    {
        private readonly NamedTextBlock _owner;
        private string? _name;

        public NamedTextAutomationPeer(NamedTextBlock owner)
            : base(owner)
        {
            _owner = owner;
            _name = GetNameCore();
            owner.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBlock.TextProperty && e.Property != AutomationProperties.NameProperty)
                {
                    return;
                }

                var name = GetNameCore();

                if (name != _name)
                {
                    var old = _name;
                    _name = name;
                    RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, name);
                }
            };
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string? GetNameCore() =>
            base.GetNameCore() is { Length: > 0 } named ? named : _owner.Inlines?.Text ?? _owner.Text;
    }
}
