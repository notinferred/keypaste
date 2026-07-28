namespace Keypaste.App.ViewModels;

/// <summary>
/// A row that will hand over its value for as long as somebody holds it.
/// </summary>
/// <remarks>
/// <para>
/// The seam between <see cref="Controls.RevealedValue"/> and whatever is behind it. The control is
/// deliberately ignorant of vaults: it knows how many dots to draw, and it knows to ask this for a
/// value on press and to say so on release.
/// </para>
/// <para>
/// <b><see cref="Reveal"/> reads, it does not fetch something already held.</b> An implementation
/// that returned a value it had been carrying since the list was built would put every variable's
/// value in memory, in a view model, for as long as the screen is open — which is exactly what the
/// hygiene sweep exists to catch. The value should come out of the open vault at the moment of the
/// press and go nowhere else.
/// </para>
/// </remarks>
internal interface IRevealSource
{
    /// <summary>How many characters the hidden value has, for the mask.</summary>
    int MaskedLength { get; }

    /// <summary>The value, for the duration of a hold.</summary>
    /// <returns>The value, or null when it can no longer be read — a locked vault, for one.</returns>
    string? Reveal();

    /// <summary>The hold ended.</summary>
    /// <remarks>
    /// Called on release, on the pointer leaving, and on capture being lost. A view model that
    /// tracks which row is revealed clears it here, so "one at a time" is a property of the view
    /// model and assertable without a display.
    /// </remarks>
    void Conceal();
}
