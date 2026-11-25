namespace Keypaste.App.Session;

/// <summary>How asking for a quick unlock's key ended.</summary>
internal enum KeyOutcome
{
    /// <summary>The person answered and the key is released.</summary>
    Released = 0,

    /// <summary>The person cancelled, or chose to type a password instead.</summary>
    Cancelled = 1,

    /// <summary>Nothing on this machine can be asked now, such as Windows Hello that is not set up.</summary>
    Unavailable = 2,

    /// <summary>It was asked and released no key.</summary>
    Failed = 3,

    /// <summary>The key no longer exists, such as Touch ID's after a fingerprint was added or removed.</summary>
    Gone = 4,
}

/// <summary>Releases a key of keypaste's own for a challenge once the person proves they are at the computer.</summary>
/// <remarks>
/// One challenge must always give one key, because its SHA-256 is the key a quick unlock's copy is sealed under:
/// Windows Hello's key signs with RSA PKCS #1 v1.5, which gives one signature per challenge, and Touch ID
/// releases the random key it was given for that challenge.
/// </remarks>
internal interface IQuickUnlockKeys
{
    /// <summary>What the person knows it as, such as "Windows Hello".</summary>
    string Name { get; }

    /// <summary>Whether it can be asked now, asking the person nothing.</summary>
    bool IsAvailable();

    /// <summary>Asks the person, then makes the key for a new copy's <paramref name="challenge"/>.</summary>
    /// <param name="challenge">The new copy's challenge.</param>
    /// <param name="key">The key, which the caller zeroes, when it is released; otherwise null.</param>
    /// <returns>How it ended.</returns>
    /// <remarks>Blocks until the person answers, so it is never called on the UI thread, and throws nothing for a failure.</remarks>
    KeyOutcome Make(byte[] challenge, out byte[]? key);

    /// <summary>Asks the person, then releases the key <see cref="Make"/> made for <paramref name="challenge"/>.</summary>
    /// <param name="challenge">A kept copy's challenge.</param>
    /// <param name="key">The key, which the caller zeroes, when it is released; otherwise null.</param>
    /// <returns>How it ended.</returns>
    /// <remarks>Blocks until the person answers, so it is never called on the UI thread, and throws nothing for a failure.</remarks>
    KeyOutcome Answer(byte[] challenge, out byte[]? key);

    /// <summary>Deletes what was kept for <paramref name="challenge"/>, if anything was, asking the person nothing.</summary>
    void Forget(byte[] challenge);
}
