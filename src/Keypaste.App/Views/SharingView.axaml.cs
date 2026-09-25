using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The Sharing screen. The one secret control is the passphrase's <c>MaskedInput</c>; the link it
/// makes goes to the clipboard, never to a control.
/// </summary>
internal sealed partial class SharingView : UserControl
{
    public SharingView()
    {
        AvaloniaXamlLoader.Load(this);

        // A select, as the design draws it: a click opens the list, and typing narrows it.
        var what = this.FindControl<AutoCompleteBox>("What")!;
        what.AddHandler(PointerReleasedEvent, (_, _) => what.IsDropDownOpen = true, handledEventsToo: true);
    }
}
