namespace Keypaste.App.GlobalShortcuts;

/// <summary>One chord that reaches keypaste from any app, through the operating system's own call for it.</summary>
/// <remarks>
/// A registration takes the chord from every other app while it lasts, which is why only the person's choice in
/// Settings makes one. None needs a permission: macOS's hot keys and Windows's and X11's grabs read no other key.
/// </remarks>
internal interface IGlobalShortcut : IDisposable
{
    /// <summary>Why this desktop gives an app no system-wide chord, in words for Settings, or null where it gives one.</summary>
    string? Unavailable { get; }

    /// <summary>Registers <paramref name="chord"/> in place of any chord registered before; called on the UI thread.</summary>
    /// <returns>False, with nothing registered, when another app or the system holds the chord or refuses it.</returns>
    bool TryRegister(GlobalChord chord);

    /// <summary>Gives the registered chord back to other apps; doing it with none registered is not an error.</summary>
    void Unregister();

    /// <summary>Raised on the UI thread each time the registered chord is pressed, in whatever app has the keyboard.</summary>
    event EventHandler? Pressed;
}

/// <summary>A desktop that gives an app no system-wide chord.</summary>
/// <param name="reason">Why, in words for Settings.</param>
internal sealed class NoGlobalShortcut(string reason) : IGlobalShortcut
{
    public string? Unavailable => reason;

    public bool TryRegister(GlobalChord chord) => false;

    public void Unregister()
    {
    }

    public event EventHandler? Pressed
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}
