namespace Keypaste.Core.HardwareKeys;

/// <summary>Why a hardware key gave no response.</summary>
public enum HardwareKeyFailure
{
    /// <summary>Something else went wrong talking to the key.</summary>
    Failed = 0,

    /// <summary>No key is connected.</summary>
    NotFound = 1,

    /// <summary>More than one key is connected, and keypaste will not guess which.</summary>
    MoreThanOne = 2,

    /// <summary>The slot is empty or not programmed for HMAC-SHA1 challenge-response.</summary>
    SlotNotConfigured = 3,

    /// <summary>The key waited to be touched and was not.</summary>
    TimedOut = 4,

    /// <summary>The person stopped waiting.</summary>
    Cancelled = 5,

    /// <summary>Hardware keys cannot be reached on this system, or by this build.</summary>
    Unavailable = 6,
}

/// <summary>Raised when a hardware key the vault needs did not answer, so nothing was opened or written.</summary>
public sealed class HardwareKeyException : VaultException
{
    /// <summary>Creates an exception for <paramref name="failure"/>.</summary>
    /// <param name="failure">Why there was no response.</param>
    /// <param name="message">What to tell the person.</param>
    public HardwareKeyException(HardwareKeyFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Creates an exception for <paramref name="failure"/> with its cause.</summary>
    /// <param name="failure">Why there was no response.</param>
    /// <param name="message">What to tell the person.</param>
    /// <param name="innerException">The driver's own failure.</param>
    public HardwareKeyException(HardwareKeyFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>Why there was no response.</summary>
    public HardwareKeyFailure Failure { get; }
}
