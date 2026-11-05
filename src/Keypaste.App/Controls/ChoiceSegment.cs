using Avalonia.Controls.Primitives;

namespace Keypaste.App.Controls;

/// <summary>
/// A segment of a segmented control whose choice is decided elsewhere: it is checked only when its binding
/// says so, never by being pressed, so a change waiting on the person's confirmation shows what still holds.
/// </summary>
internal sealed class ChoiceSegment : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    protected override void Toggle()
    {
    }
}
