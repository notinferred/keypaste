namespace Keypaste.App.Session;

/// <summary>Why a vault stopped being unlocked.</summary>
/// <remarks>
/// The reason reaches the user interface, because "you were away" and "you pressed Ctrl+L" deserve
/// different words on the unlock screen, and neither should look like a failure.
/// </remarks>
internal enum VaultLockReason
{
    /// <summary>Nobody touched the app for <see cref="AppVaultSession.IdleTimeout"/>.</summary>
    Idle = 0,

    /// <summary>The user asked for it.</summary>
    Manual = 1,

    /// <summary>The app is closing.</summary>
    Shutdown = 2,

    /// <summary>A different vault was opened, so this one was closed first.</summary>
    Replaced = 3,

    /// <summary>The window was minimized and the user asked for that to lock.</summary>
    Minimized = 4,
}
