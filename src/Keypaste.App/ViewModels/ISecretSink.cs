namespace Keypaste.App.ViewModels;

/// <summary>
/// Something that takes the characters of a secret somebody is entering.
/// </summary>
/// <remarks>
/// <para>
/// The seam between <see cref="Controls.MaskedInput"/> and whatever is behind it, and the sibling
/// of <see cref="IRevealSource"/> in both shape and purpose: the control knows how many dots to
/// draw and where to send a keystroke, and nothing about vaults.
/// </para>
/// <para>
/// <b>Why a bound property rather than <c>UnlockView</c>'s four event subscriptions.</b> That view
/// reaches its fields with <c>FindControl</c>, which works because they are its own children. Three
/// of the four fields 4.9 adds are inside <c>DataTemplate</c>s — the entry detail pane and the env
/// project are both drawn through <c>ContentControl.DataTemplates</c> — where <c>FindControl</c>
/// cannot reach and a view would need code-behind to go looking. <see cref="IRevealSource"/> solved
/// this once already, bound as <c>Source="{Binding}"</c> inside the env row template, and both
/// screens keep the "no code behind the XAML beyond loading it" claim their class comments make.
/// </para>
/// <para>
/// <b><see cref="Paste"/> returns a task, and callers must not drop it.</b> Reading the clipboard
/// is asynchronous, so a control that fired it and forgot would leave a test asserting against a
/// buffer that has not filled yet — which is the shape of defect F.16 recorded in the headless
/// harness, and there is no reason to write it again here.
/// </para>
/// </remarks>
internal interface ISecretSink
{
    /// <summary>Appends one typed character.</summary>
    /// <param name="value">The character.</param>
    void Type(char value);

    /// <summary>Removes the last character, if any.</summary>
    void Backspace();

    /// <summary>Discards everything entered so far.</summary>
    void Clear();

    /// <summary>Appends what is on the clipboard, if it is something a person could have typed.</summary>
    /// <returns>A task that completes when the clipboard has answered and the buffer reflects it.</returns>
    Task Paste();
}
