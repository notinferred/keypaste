namespace Keypaste.App.GlobalShortcuts;

/// <summary>Where the search shortcut stands, as Settings says it.</summary>
internal enum SearchShortcutState
{
    /// <summary>The person turned it off, or <c>app.toml</c> names a chord this platform does not offer.</summary>
    Off,

    /// <summary>Its chord is registered and brings keypaste forward from any app.</summary>
    Registered,

    /// <summary>Another app or the system holds its chord, so it does nothing.</summary>
    Taken,

    /// <summary>This desktop gives an app no system-wide chord.</summary>
    Unavailable,
}

/// <summary>
/// The chord <c>app.toml</c> chooses for searching keypaste from any app, kept registered as the preference changes: a
/// change in Settings registers the new chord, Off gives the old one back, and a chord another app holds is told to
/// Settings rather than thrown.
/// </summary>
internal sealed class SearchShortcut : IDisposable
{
    private readonly IGlobalShortcut _system;
    private readonly DesktopPreferences _preferences;
    private readonly Action _search;
    private readonly bool _mac;
    private GlobalChord? _registered;

    /// <param name="system">The platform's chord.</param>
    /// <param name="preferences">Where the chosen chord is kept, followed as it changes.</param>
    /// <param name="search">What a press does.</param>
    /// <param name="mac">Whether the chords are macOS's; this platform's unless a test says.</param>
    internal SearchShortcut(IGlobalShortcut system, DesktopPreferences preferences, Action search, bool? mac = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(search);

        _system = system;
        _preferences = preferences;
        _search = search;
        _mac = mac ?? OperatingSystem.IsMacOS();
        _system.Pressed += OnPressed;
        _preferences.Changed += OnPreferencesChanged;
        Apply();
    }

    /// <summary>Where it stands now.</summary>
    internal SearchShortcutState State { get; private set; }

    /// <summary>The chord <c>app.toml</c> chooses, registered or not, or null for none.</summary>
    internal GlobalChord? Chosen => GlobalChord.Chosen(_preferences.Current.SearchShortcut, _mac);

    /// <summary>Why this desktop has no shortcut, or null where it has one.</summary>
    internal string? Unavailable => _system.Unavailable;

    /// <summary>Whether its chords are macOS's.</summary>
    internal bool Mac => _mac;

    public void Dispose()
    {
        _preferences.Changed -= OnPreferencesChanged;
        _system.Pressed -= OnPressed;
        _system.Dispose();
    }

    private void OnPressed(object? sender, EventArgs e) => _search();

    private void OnPreferencesChanged(object? sender, EventArgs e) => Apply();

    // A chord already registered stays so; any other change, or a chord taken before, is asked of the system again.
    private void Apply()
    {
        if (_system.Unavailable is not null)
        {
            State = SearchShortcutState.Unavailable;
            return;
        }

        var chosen = Chosen;

        if (chosen is null)
        {
            _system.Unregister();
            _registered = null;
            State = SearchShortcutState.Off;
            return;
        }

        if (chosen == _registered)
        {
            return;
        }

        _registered = _system.TryRegister(chosen) ? chosen : null;
        State = _registered is null ? SearchShortcutState.Taken : SearchShortcutState.Registered;
    }
}
