using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// The menu a view's button opens, a <see cref="MenuFlyout"/> as every menu is: it hides a shown value as it opens, and a form a choice opens takes the
/// keyboard once it closes.
/// </summary>
internal sealed class ButtonMenu : MenuFlyout
{
    public ButtonMenu()
    {
        Opening += (_, _) => RevealedValue.HideShown();
        AppMenu.GiveTheKeyboardToWhatItOpens(this);
    }
}
