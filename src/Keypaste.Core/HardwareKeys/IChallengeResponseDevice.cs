namespace Keypaste.Core.HardwareKeys;

/// <summary>A hardware key that answers HMAC-SHA1 challenges from its OTP slots, as a YubiKey does.</summary>
/// <remarks>
/// The device sees exactly the 64 bytes KeePassXC sends a YubiKey, already padded by
/// <see cref="HardwareKey"/>, and answers as the key's slot is programmed to: a fixed-length slot
/// hashes all 64, a variable-length one first drops the trailing bytes equal to the last.
/// </remarks>
public interface IChallengeResponseDevice
{
    /// <summary>The keys connected now, with the slots each has programmed.</summary>
    /// <exception cref="HardwareKeyException">Keys cannot be reached on this system.</exception>
    IReadOnlyList<HardwareKeyInfo> Find();

    /// <summary>Sends one challenge to a slot of the one connected key and returns its 20-byte response.</summary>
    /// <param name="slot">1 or 2.</param>
    /// <param name="challenge">The 64 bytes the key hashes.</param>
    /// <param name="touchNeeded">Called, on any thread, when the key waits to be touched.</param>
    /// <param name="cancellationToken">Stops waiting for the key.</param>
    /// <exception cref="HardwareKeyException">No response came back; <see cref="HardwareKeyException.Failure"/> says why.</exception>
    byte[] Respond(int slot, byte[] challenge, Action touchNeeded, CancellationToken cancellationToken);
}

/// <summary>A connected key.</summary>
/// <param name="Name">What to call it, such as "YubiKey 5".</param>
/// <param name="Serial">Its serial number, when it reports one.</param>
/// <param name="Slots">The slots that hold a configuration, 1 or 2.</param>
public sealed record HardwareKeyInfo(string Name, int? Serial, IReadOnlyList<int> Slots);
