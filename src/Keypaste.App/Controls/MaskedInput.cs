using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Keypaste.App.Controls;

/// <summary>
/// A password field that holds no password.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists instead of <c>TextBox</c> with a <c>PasswordChar</c>.</b> Avalonia's
/// <c>TextBoxAutomationPeer</c> implements <c>IValueProvider</c> and returns <c>Owner.Text</c> with
/// no check for <c>PasswordChar</c>, and <c>TextBox</c> does not override
/// <c>OnCreateAutomationPeer</c> to suppress it. <c>Avalonia.FreeDesktop.AtSpi</c> is in this app's
/// dependency closure and AT-SPI is a session-bus service, so a master password typed into a
/// <c>TextBox</c> is readable by another process on the machine. <c>TextBox</c> also keeps an
/// undo stack whose states each hold a <c>string</c>, which is a retained history of partial
/// passwords that cannot be zeroed.
/// </para>
/// <para>
/// <b>This control is stateless about the secret.</b> It raises one event per keystroke and never
/// accumulates. The characters go into a <see cref="Core.SecretBuffer"/> owned by the view model,
/// which is <see cref="IDisposable"/> and disposed on every path out of the unlock screen. Keeping
/// the buffer out of the visual tree is what makes the automation problem go away rather than be
/// mitigated: there is nothing here for a peer to expose, and
/// <see cref="OnCreateAutomationPeer"/> hands back a plain control peer with no value pattern.
/// </para>
/// <para>
/// <b>The honest limit.</b> <see cref="TextInputEventArgs.Text"/> is a <c>string</c>, one character
/// long when typed and the whole password in one piece when pasted — measured, not assumed. Neither
/// can be zeroed. That is narrower than a field that holds the entire password for its lifetime, and
/// it is not nothing; SECURITY.md says so rather than implying the GUI matches
/// <c>ConsoleSecretPrompt</c>.
/// </para>
/// </remarks>
internal sealed class MaskedInput : TemplatedControl
{
    /// <summary>How many characters the buffer holds, which is all this control knows.</summary>
    internal static readonly StyledProperty<int> MaskedLengthProperty =
        AvaloniaProperty.Register<MaskedInput, int>(nameof(MaskedLength));

    /// <summary>What to show when nothing has been typed.</summary>
    internal static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<MaskedInput, string>(nameof(Placeholder), string.Empty);

    /// <summary>The dots the template draws. Derived; never the password.</summary>
    internal static readonly StyledProperty<string> DisplayProperty =
        AvaloniaProperty.Register<MaskedInput, string>(nameof(Display), string.Empty);

    /// <summary>Whether the placeholder should be visible.</summary>
    internal static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<MaskedInput, bool>(nameof(IsEmpty), true);

    static MaskedInput() => FocusableProperty.OverrideDefaultValue<MaskedInput>(true);

    /// <summary>Raised once per character the user typed or pasted.</summary>
    internal event EventHandler<char>? CharacterTyped;

    /// <summary>Raised on backspace.</summary>
    internal event EventHandler? BackspacePressed;

    /// <summary>Raised on escape.</summary>
    internal event EventHandler? ClearRequested;

    /// <summary>Raised on enter.</summary>
    internal event EventHandler? Submitted;

    internal int MaskedLength
    {
        get => GetValue(MaskedLengthProperty);
        set => SetValue(MaskedLengthProperty, value);
    }

    internal string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    internal string Display
    {
        get => GetValue(DisplayProperty);
        private set => SetValue(DisplayProperty, value);
    }

    internal bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        private set => SetValue(IsEmptyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MaskedLengthProperty)
        {
            var length = Math.Max(0, MaskedLength);
            Display = new string('•', length);
            IsEmpty = length == 0;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Text is { Length: > 0 } text)
        {
            foreach (var c in text)
            {
                // Control characters arrive here on some platforms; they are keystrokes, not
                // password characters, and a tab that became part of a master password would be
                // impossible to reproduce on another machine.
                if (!char.IsControl(c))
                {
                    CharacterTyped?.Invoke(this, c);
                }
            }
        }

        e.Handled = true;
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.Key)
        {
            case Key.Back:
                BackspacePressed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Escape:
                ClearRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Enter:
                Submitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            default:
                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// A peer with no value pattern, which is the entire point of this control.
    /// </summary>
    /// <remarks>
    /// <see cref="ControlAutomationPeer"/> reports a control that can be focused and named. It has
    /// no <c>IValueProvider</c>, so there is no accessibility path that returns text — and since
    /// this control never holds the password, there would be nothing to return in any case.
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ControlAutomationPeer(this);
}
