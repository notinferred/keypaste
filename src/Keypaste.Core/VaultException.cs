namespace Keypaste.Core;

/// <summary>
/// Raised when a vault cannot be created, opened, or saved.
/// </summary>
/// <remarks>
/// Every failure on the vault path surfaces as this type or a derived one. Nothing on that
/// path returns a degraded result — an empty vault, a null entry, a silently unencrypted
/// file — because a fail-open error path is how a credential leaks (CORE.md law 3.7).
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
