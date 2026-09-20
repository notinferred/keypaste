namespace Keypaste.Core;

/// <summary>
/// Raised when a vault cannot be created, opened, or saved.
/// </summary>
/// <remarks>
/// Every failure on the vault path surfaces as this type or a derived one. Nothing on that
/// path returns a degraded result — an empty vault, a null entry, a silently unencrypted
/// file — because a fail-open error path is how a credential leaks (docs/PRODUCT.md law 3.7).
/// </remarks>
public class VaultException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public VaultException()
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of the failure.</param>
    public VaultException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when something else wrote to the vault file after it was opened.
/// </summary>
/// <remarks>
/// <para>
/// A vault is held in memory and written back whole, so a save made from a stale copy does not
/// merge — it reverts. The reverted write leaves no history item either, because the entry it
/// carried never existed in the saving process's tree, so there is nothing for KeePass to snapshot
/// and nothing for KeePassXC's History tab to show. That is unrecoverable, and docs/PRODUCT.md law 3.7 is
/// why it is an exception rather than a paragraph in SECURITY.md.
/// </para>
/// <para>
/// Distinct from <see cref="VaultException"/> so a caller can offer to reload rather than reporting
/// a write failure. <b>Nothing was written</b> when this is thrown.
/// </para>
/// </remarks>
public sealed class VaultChangedOnDiskException : VaultException
{
    /// <summary>Creates an exception with a default message.</summary>
    public VaultChangedOnDiskException()
        : base("The vault file changed since it was opened, so saving would discard that change.")
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of the failure.</param>
    public VaultChangedOnDiskException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VaultChangedOnDiskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the supplied master password does not open the vault.
/// </summary>
/// <remarks>
/// Distinct from <see cref="VaultException"/> so a caller can tell "you typed it wrong" from
/// "the file is damaged" without parsing a message. The distinction is deliberately the only
/// one exposed: anything finer would describe the contents of a vault we failed to open.
/// </remarks>
public sealed class InvalidMasterPasswordException : VaultException
{
    /// <summary>Creates an exception with a default message.</summary>
    public InvalidMasterPasswordException()
        : base("The master password is incorrect, or the vault is not a readable KDBX file.")
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of the failure.</param>
    public InvalidMasterPasswordException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public InvalidMasterPasswordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the vault's previous bytes could not be preserved before a save or a restore replaced
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing was written</b> when this is thrown: not the backup, not the vault, and nothing was
/// pruned. A save that cannot keep the copy it is about to make unnecessary does not proceed, and
/// there is no flag, setting or environment variable that lets it — the whole value of a backup is
/// that it is there on the one occasion nobody planned for.
/// </para>
/// <para>
/// Distinct from <see cref="VaultException"/> so the save's retry cannot absorb it. A failed copy is
/// a full disk, a read-only folder or a wrong permission, and
/// <c>KeePassInterop.IsTransient</c> deliberately admits <see cref="IOException"/> and
/// <see cref="UnauthorizedAccessException"/>; arriving as those would spend the whole retry budget
/// before saying so.
/// </para>
/// </remarks>
public sealed class VaultBackupException : VaultException
{
    /// <summary>Creates an exception with no message.</summary>
    public VaultBackupException()
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of the failure.</param>
    public VaultBackupException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VaultBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a backup could not be validated or put in the vault's place.</summary>
/// <remarks>
/// <b>The live vault was not replaced</b> when this is thrown. Replacing it is the last thing a
/// restore does and nothing after it can fail, so any refusal leaves the file holding what it held. A
/// copy of that file may already have been kept, which costs a backup slot and nothing else.
/// </remarks>
public sealed class VaultRestoreException : VaultException
{
    /// <summary>Creates an exception with no message.</summary>
    public VaultRestoreException()
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">A description of the failure.</param>
    public VaultRestoreException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VaultRestoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
