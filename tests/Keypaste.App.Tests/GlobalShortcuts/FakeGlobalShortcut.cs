using Keypaste.App.GlobalShortcuts;

namespace Keypaste.App.Tests.GlobalShortcuts;

/// <summary>The system's chord without the system: it records what is registered, refuses what a test says another app holds, and is pressed when a test says.</summary>
internal sealed class FakeGlobalShortcut(string? unavailable = null) : IGlobalShortcut
{
    public string? Unavailable => unavailable;

    /// <summary>The chord registered now, or null.</summary>
    internal GlobalChord? Registered { get; private set; }

    /// <summary>Every registration asked for, in order, refused or not.</summary>
    internal List<GlobalChord> Asked { get; } = [];

    /// <summary>The chords another app holds, which a registration is refused.</summary>
    internal HashSet<GlobalChord> Taken { get; } = [];

    internal bool Disposed { get; private set; }

    public event EventHandler? Pressed;

    public bool TryRegister(GlobalChord chord)
    {
        Asked.Add(chord);
        Registered = Taken.Contains(chord) || unavailable is not null ? null : chord;
        return Registered is not null;
    }

    public void Unregister() => Registered = null;

    /// <summary>The person presses the registered chord in whatever app has the keyboard.</summary>
    internal void Press()
    {
        if (Registered is not null)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Disposed = true;
}
