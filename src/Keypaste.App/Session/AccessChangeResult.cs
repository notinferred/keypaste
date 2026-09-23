using Keypaste.Core;

namespace Keypaste.App.Session;

/// <summary>What happened when the open vault's access was asked to change.</summary>
internal enum AccessChangeOutcome
{
    /// <summary>The vault opens with the new factors, and the session is open on it.</summary>
    Changed = 0,

    /// <summary>The core refused the change and wrote nothing; see <see cref="AccessChangeResult.Result"/>.</summary>
    Refused = 1,

    /// <summary>The current password did not open the vault. Nothing was written.</summary>
    WrongCurrentSecret = 2,

    /// <summary>No vault was open.</summary>
    Locked = 3,

    /// <summary>The vault changed, and the session locked rather than reopen it.</summary>
    ChangedAndLocked = 4,
}

/// <summary>The result of <see cref="AppVaultSession.ChangeAccess"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Result">The core's answer, when the change reached it.</param>
internal sealed record AccessChangeResult(AccessChangeOutcome Outcome, VaultAccessResult? Result = null);
