namespace Keypaste.App.Session;

/// <summary>How asking for a signature ended.</summary>
internal enum SignOutcome
{
    /// <summary>The person answered and the challenge is signed.</summary>
    Signed = 0,

    /// <summary>The person cancelled, or chose to type a password instead.</summary>
    Cancelled = 1,

    /// <summary>Nothing on this machine can be asked now, such as Windows Hello that is not set up.</summary>
    Unavailable = 2,

    /// <summary>It was asked and gave no signature.</summary>
    Failed = 3,
}

/// <summary>Signs a challenge with a key of keypaste's own once the person proves they are at the computer.</summary>
/// <remarks>
/// One challenge must always give one signature, because its SHA-256 is the key a quick unlock's copy is sealed under
/// (D-0550); Windows Hello's key signs with RSA PKCS #1 v1.5, which does.
/// </remarks>
internal interface IQuickUnlockSigner
{
    /// <summary>What the person knows it as, such as "Windows Hello".</summary>
    string Name { get; }

    /// <summary>Whether it can be asked now, asking the person nothing.</summary>
    bool IsAvailable();

    /// <summary>Asks the person, then signs <paramref name="challenge"/>, making keypaste's key on first use.</summary>
    /// <param name="challenge">The bytes to sign.</param>
    /// <param name="signature">The signature, which the caller zeroes, when it is signed; otherwise null.</param>
    /// <returns>How it ended.</returns>
    /// <remarks>Blocks until the person answers, so it is never called on the UI thread, and throws nothing for a failure.</remarks>
    SignOutcome Sign(byte[] challenge, out byte[]? signature);
}
