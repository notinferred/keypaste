namespace Keypaste.App.ViewModels;

/// <summary>
/// A row that will hand over its value for as long as somebody has it revealed.
/// </summary>
/// <remarks>
/// <para>
/// The seam between <see cref="Controls.RevealedValue"/> and whatever is behind it. The control is
/// deliberately ignorant of vaults: it knows how many dots to draw, and it knows to ask this for a
/// value when revealed and to say so when hidden.
/// </para>
/// <para>
/// <b><see cref="Reveal"/> reads, it does not fetch something already held.</b> An implementation
/// that returned a value it had been carrying since the list was built would put every variable's
/// value in memory, in a view model, for as long as the screen is open, which is exactly what the
/// hygiene sweep exists to catch. The value should come out of the open vault at the moment it is
/// asked for and go nowhere else.
/// </para>
/// </remarks>
internal interface IRevealSource
{
    /// <summary>How many characters the hidden value has, for the mask.</summary>
    int MaskedLength { get; }

    /// <summary>The value, for as long as it is revealed.</summary>
    /// <returns>The value, or null when it can no longer be read: a locked vault, for one.</returns>
    string? Reveal();

    /// <summary>The value was hidden, or could not be read.</summary>
    /// <remarks>
    /// A view model that tracks which row is revealed clears it here, so "one at a time" is a property
    /// of the view model and assertable without a display.
    /// </remarks>
    void Conceal();

    /// <summary>The value has moved on, so a control showing it reads it again.</summary>
    /// <remarks>
    /// Raised by a source whose value changes under it, such as a TOTP code at the end of its period or a
    /// value just saved, and carrying nothing: the control asks <see cref="Reveal"/> again, so the value
    /// still comes out of the open vault at the moment it is drawn. A source whose value never moves on
    /// raises nothing.
    /// </remarks>
    event EventHandler? Changed
    {
        add
        {
        }

        remove
        {
        }
    }
}
