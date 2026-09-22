namespace Keypaste.Core;

/// <summary>What an access change does to a vault's keyfile.</summary>
public enum AccessKeyfileChange
{
    /// <summary>The keyfile the vault opened with, or none, stays as it is.</summary>
    Keep = 0,

    /// <summary>An existing keyfile is attached, adding one or replacing the current one.</summary>
    Attach = 1,

    /// <summary>The current keyfile stops being needed.</summary>
    Remove = 2,
}

/// <summary>What an access change asks for.</summary>
/// <param name="SetPassword">Whether the master password is set to a new one.</param>
/// <param name="Keyfile">What happens to the keyfile.</param>
/// <param name="KeyfilePath">The keyfile to attach; required for <see cref="AccessKeyfileChange.Attach"/> alone.</param>
public sealed record VaultAccessChange(bool SetPassword, AccessKeyfileChange Keyfile, string? KeyfilePath = null);

/// <summary>What happened when a vault's access was asked to change.</summary>
/// <remarks>
/// Every value but <see cref="Changed"/> is a refusal that wrote nothing, not even a backup. Failures
/// that happen once writing has begun are exceptions, as they are for every other save.
/// </remarks>
public enum VaultAccessOutcome
{
    /// <summary>The vault now opens with the new credentials and no longer with the old ones.</summary>
    Changed = 0,

    /// <summary>Nothing would change.</summary>
    NothingToChange = 1,

    /// <summary>The new password is empty.</summary>
    EmptyPassword = 2,

    /// <summary>The confirmation did not match the new password.</summary>
    PasswordsDoNotMatch = 3,

    /// <summary>The keyfile to remove is all that unlocks the vault, and no password was set in its place.</summary>
    WouldLeaveNoPassword = 4,

    /// <summary>A keyfile was to be removed from a vault that has none.</summary>
    NoKeyfileToRemove = 5,

    /// <summary>The file to attach is missing, unreadable, empty or a vault; see <see cref="VaultAccessResult.Keyfile"/>.</summary>
    KeyfileUnusable = 6,

    /// <summary>The file to attach would be keyed by its hash, which one edit to it destroys.</summary>
    KeyfileIsFragile = 7,

    /// <summary>The file to attach is this vault or lies in its backup directory.</summary>
    KeyfileIsThisVault = 8,
}

/// <summary>The result of <see cref="Vault.ChangeAccess(VaultAccessChange, ReadOnlySpan{char}, ReadOnlySpan{char})"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Kept">The copy of the replaced file, which opens with the old credentials; set when the vault changed.</param>
/// <param name="Keyfile">What the file to attach turned out to be; default when none was inspected.</param>
public sealed record VaultAccessResult(VaultAccessOutcome Outcome, VaultBackup? Kept, KeyfileInspection Keyfile);

/// <summary>Raised when a keyfile is one this build would key with the wrong material.</summary>
/// <remarks>Nothing was opened or written. See <see cref="KeyfileOutcome.XmlUnreadable"/>.</remarks>
public sealed class UnreadableKeyfileException : VaultException
{
    /// <summary>Creates an exception naming the keyfile.</summary>
    /// <param name="keyfilePath">The keyfile that was refused.</param>
    public UnreadableKeyfileException(string keyfilePath)
        : base(UnreadableKeyfile.Explain(keyfilePath))
    {
        KeyfilePath = keyfilePath;
    }

    /// <summary>The keyfile that was refused.</summary>
    public string KeyfilePath { get; }
}

/// <summary>The one explanation every refusal of an unreadable XML keyfile gives.</summary>
public static class UnreadableKeyfile
{
    /// <summary>Why <paramref name="keyfilePath"/> was refused.</summary>
    public static string Explain(string keyfilePath) =>
        $"'{keyfilePath}' is a KeePass XML keyfile, and this build of keypaste cannot read one: it " +
        "would use the wrong key. Nothing was opened or written. The password is not the problem.";
}

/// <summary>
/// Raised when an access change replaced the vault and the file then did not open with the new
/// credentials.
/// </summary>
/// <remarks>
/// The bytes written were opened with the new credentials before they replaced the vault, so this
/// means the file changed or became unreadable afterwards. Nothing is rolled back: <see cref="Kept"/>
/// holds the vault as it was and opens with the old credentials.
/// </remarks>
public sealed class VaultAccessUnconfirmedException : VaultException
{
    /// <summary>Creates an exception naming the copy that still opens.</summary>
    /// <param name="kept">The copy of the file the change replaced.</param>
    /// <param name="innerException">Why the vault did not open.</param>
    public VaultAccessUnconfirmedException(VaultBackup kept, Exception innerException)
        : base(
            $"The vault was changed but did not open again with the new credentials. Its previous version is kept at '{kept.Path}' and opens with the old ones.",
            innerException)
    {
        Kept = kept;
    }

    /// <summary>The copy of the file the change replaced.</summary>
    public VaultBackup Kept { get; }
}
