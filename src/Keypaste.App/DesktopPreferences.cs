using Keypaste.Core.Audit;
using Keypaste.Core.Settings;

namespace Keypaste.App;

/// <summary>
/// The one copy of <c>app.toml</c> this process works from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read once, at composition, and shared from there.</b> Before this existed the file was read
/// in exactly one place — the Settings screen's constructor, built lazily on first navigation —
/// while the session was armed from a constant. Somebody who chose one minute was shown "1 minute"
/// and kept an unlocked vault for five (D-0095).
/// </para>
/// <para>
/// The screen and the session now hold the same object, so what is displayed is what is in force
/// rather than a second read that happens to agree with the first. Nothing here reads the file
/// again: an edit made by hand while the app is running takes effect at the next launch.
/// </para>
/// <para>
/// <b>Loading never writes.</b> <see cref="AppSettings.Load"/> answers a file it cannot parse with
/// <see cref="AppSettings.Default"/> and leaves the bytes exactly as they are (D-0028); saving the
/// defaults back would destroy a hand edit at the moment its author was most likely to be part-way
/// through making it.
/// </para>
/// </remarks>
internal sealed class DesktopPreferences
{
    private readonly string _path;

    /// <summary>Reads the preferences for a <c>KEYPASTE_HOME</c>.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null for the default location.</param>
    internal DesktopPreferences(string? home)
    {
        _path = KeypasteHome.SettingsPath(home);
        Current = AppSettings.Load(_path);
    }

    /// <summary>What the app is running as.</summary>
    internal AppSettings Current { get; private set; }

    /// <summary>The idle timeout in the shape the session takes it.</summary>
    /// <remarks>
    /// Always locks: <see cref="AppSettings.IdleTimeoutSeconds"/> clamps into its range on the way
    /// in, so there is no file that produces a session which stays open (docs/PRODUCT.md law 3.7).
    /// </remarks>
    internal TimeSpan IdleTimeout => TimeSpan.FromSeconds(Current.IdleTimeoutSeconds);

    /// <summary>Takes a change and writes it.</summary>
    /// <param name="settings">The new preferences.</param>
    /// <returns><see langword="false"/> when the file could not be written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <remarks>
    /// The in-memory copy moves whether or not the write landed: a preference that cannot reach the
    /// disk still holds for this run, and the caller says so rather than pretending it did not
    /// happen.
    /// </remarks>
    internal bool Update(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Current = settings;
        return AppSettings.Save(_path, settings);
    }
}
