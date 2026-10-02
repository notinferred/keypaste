namespace Keypaste.App.Session;

/// <summary>What happened when the open vault was asked to reload from its file.</summary>
internal enum ReloadOutcome
{
    /// <summary>The session holds what the file holds now.</summary>
    Reloaded = 0,

    /// <summary>The file could not be read or opened, so the session holds what it held.</summary>
    Failed = 1,

    /// <summary>No vault was open, or what unlocks it changed and the session locked.</summary>
    Locked = 2,
}

/// <summary>The result of <see cref="AppVaultSession.Reload"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Reason">Why it failed, for the person, when it did.</param>
internal sealed record ReloadResult(ReloadOutcome Outcome, string? Reason = null);
