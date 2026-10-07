namespace Keypaste.App.Session;

/// <summary>What happened when an edit was written through <see cref="AppVaultSession.Write{T}"/>.</summary>
internal enum WriteOutcome
{
    /// <summary>The edit changed the vault and the file holds it.</summary>
    Saved = 0,

    /// <summary>The edit said it changed nothing, so nothing was saved; <see cref="WriteResult{T}.Value"/> says why.</summary>
    NothingToSave = 1,

    /// <summary>No vault was open, or the one the edit began on closed before it finished. Nothing was written.</summary>
    Locked = 2,

    /// <summary>Another program saved the file since this session read it, so nothing was written.</summary>
    ChangedOnDisk = 3,

    /// <summary>The core refused the edit or the save; <see cref="WriteResult{T}.Reason"/> says why.</summary>
    Failed = 4,
}

/// <summary>The result of <see cref="AppVaultSession.Write{T}"/>.</summary>
/// <typeparam name="T">What the edit returns.</typeparam>
/// <param name="Outcome">What happened.</param>
/// <param name="Value">What the edit returned, or the default when it never returned.</param>
/// <param name="Unwritten">Whether the edit is in the open vault although no save wrote it, so reloading discards it.</param>
/// <param name="Reason">The core's message, when <paramref name="Outcome"/> is <see cref="WriteOutcome.Failed"/>.</param>
internal sealed record WriteResult<T>(WriteOutcome Outcome, T? Value = default, bool Unwritten = false, string? Reason = null)
{
    /// <summary>What a screen says when the write did not go through, or null when it did.</summary>
    /// <param name="redo">What to do again once the vault is reloaded, as the screen words it, such as "delete this again".</param>
    internal string? Problem(string redo) => Outcome switch
    {
        WriteOutcome.Locked => "The vault is locked.",
        WriteOutcome.ChangedOnDisk => $"Something else changed this vault since you opened it. Reload to see it, then {redo}.",
        WriteOutcome.Failed => Reason,
        _ => null,
    };
}
