namespace Keypaste.Core.HardwareKeys;

/// <summary>
/// A vault factor answered by one slot of a hardware key, compatible with KeePassXC's YubiKey
/// challenge-response.
/// </summary>
/// <remarks>
/// <para>
/// Every open and every save of a vault that uses it asks the key once, because the challenge is
/// the vault's KDF seed and a save draws a new one. The last answer is held until
/// <see cref="Dispose"/>, so checking the bytes just written, and opening them again after an
/// access change, ask nothing more of the key: a slot that needs a touch is touched once per save.
/// </para>
/// <para>
/// The caller owns this and disposes it after the vaults that use it.
/// </para>
/// </remarks>
public sealed class HardwareKey : IDisposable
{
    /// <summary>The length of the challenge a YubiKey hashes.</summary>
    internal const int ChallengeLength = 64;

    /// <summary>The length of an HMAC-SHA1 response.</summary>
    internal const int ResponseLength = 20;

    private readonly IChallengeResponseDevice _device;
    private readonly Lock _asking = new();
    private readonly Lock _gate = new();
    private byte[]? _lastChallenge;
    private byte[]? _lastResponse;
    private CancellationTokenSource? _waiting;
    private bool _touchShown;
    private bool _disposed;

    /// <summary>A factor answered by <paramref name="slot"/> of the key <paramref name="device"/> reaches.</summary>
    /// <param name="device">Reaches the key.</param>
    /// <param name="slot">1 or 2.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is neither 1 nor 2.</exception>
    public HardwareKey(IChallengeResponseDevice device, int slot)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 2);

        _device = device;
        Slot = slot;
    }

    /// <summary>The key's slot, 1 or 2.</summary>
    public int Slot { get; }

    /// <summary>Raised with <see langword="true"/> when the key waits to be touched, and <see langword="false"/> when it stops.</summary>
    /// <remarks>Raised on whichever thread asked or answered, so a front end marshals it.</remarks>
    public event EventHandler<bool>? WaitingForTouch;

    /// <summary>Stops waiting for the key, so the open or save that asked fails and writes nothing.</summary>
    public void CancelWait()
    {
        lock (_gate)
        {
            _waiting?.Cancel();
        }
    }

    /// <summary>The 64 bytes KeePassXC sends for <paramref name="challenge"/>: PKCS#7-style padding with the pad length.</summary>
    /// <remarks>
    /// A variable-length slot drops every trailing byte equal to the last before hashing, including
    /// any at the end of the challenge itself that happen to equal the pad, and KeePassXC's key
    /// derivation is defined by what the key hashes, so this is reproduced rather than corrected.
    /// </remarks>
    internal static byte[] Pad(ReadOnlySpan<byte> challenge)
    {
        if (challenge.Length is 0 or > ChallengeLength)
        {
            throw new ArgumentException("A challenge is 1 to 64 bytes.", nameof(challenge));
        }

        var padded = new byte[ChallengeLength];
        challenge.CopyTo(padded);
        padded.AsSpan(challenge.Length).Fill((byte)(ChallengeLength - challenge.Length));
        return padded;
    }

    /// <summary>Whether a connected key has this slot programmed. Asks the key nothing that needs a touch.</summary>
    internal bool IsConnected()
    {
        try
        {
            return _device.Find().Any(key => key.Slots.Contains(Slot));
        }
        catch (HardwareKeyException)
        {
            return false;
        }
    }

    /// <summary>The key's raw response to <paramref name="challenge"/>, which the caller zeroes.</summary>
    /// <exception cref="HardwareKeyException">The key did not answer.</exception>
    internal byte[] Respond(byte[] challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        lock (_asking)
        {
            CancellationTokenSource waiting;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_lastChallenge is { } last && _lastResponse is { } answered && last.AsSpan().SequenceEqual(challenge))
                {
                    return [.. answered];
                }

                waiting = new CancellationTokenSource();
                _waiting = waiting;
            }

            var response = Ask(challenge, waiting);

            lock (_gate)
            {
                if (_disposed)
                {
                    CryptographicOperations.ZeroMemory(response);
                    throw new ObjectDisposedException(nameof(HardwareKey));
                }

                Forget();
                _lastChallenge = [.. challenge];
                _lastResponse = [.. response];
            }

            return response;
        }
    }

    private byte[] Ask(byte[] challenge, CancellationTokenSource waiting)
    {
        byte[] response;
        try
        {
            response = _device.Respond(Slot, Pad(challenge), () => ShowTouch(waiting), waiting.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new HardwareKeyException(HardwareKeyFailure.Cancelled, "Stopped waiting for the hardware key. Nothing was opened or saved.", ex);
        }
        catch (Exception ex) when (ex is not HardwareKeyException)
        {
            throw new HardwareKeyException(HardwareKeyFailure.Failed, $"The hardware key did not answer: {ex.Message}", ex);
        }
        finally
        {
            lock (_gate)
            {
                _waiting = null;

                if (_touchShown)
                {
                    _touchShown = false;
                    WaitingForTouch?.Invoke(this, false);
                }
            }

            waiting.Dispose();
        }

        if (response.Length != ResponseLength)
        {
            CryptographicOperations.ZeroMemory(response);
            throw new HardwareKeyException(HardwareKeyFailure.Failed, "The hardware key's response was not an HMAC-SHA1 response.");
        }

        return response;
    }

    /// <summary>Zeroes the last response.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _waiting?.Cancel();
            Forget();
        }
    }

    // Under the gate, and only while the ask it belongs to is still running: a driver may report
    // the touch on another thread after it has already answered.
    private void ShowTouch(CancellationTokenSource asking)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_waiting, asking) && !_touchShown)
            {
                _touchShown = true;
                WaitingForTouch?.Invoke(this, true);
            }
        }
    }

    private void Forget()
    {
        if (_lastResponse is { } response)
        {
            CryptographicOperations.ZeroMemory(response);
        }

        _lastResponse = null;
        _lastChallenge = null;
    }
}
