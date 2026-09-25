using System.Security.Cryptography;
using Keypaste.Core.HardwareKeys;

namespace Keypaste.Core.Tests.HardwareKeys;

/// <summary>
/// A YubiKey OTP slot in software: HMAC-SHA1 under a known secret, answering the 64-byte challenge
/// as a programmed slot does. Shared with the desktop tests by a linked source file.
/// </summary>
/// <param name="secret">The slot's 20-byte HMAC secret.</param>
/// <param name="variableLength">
/// Whether the slot is programmed for challenges shorter than 64 bytes (<c>HMAC_LT64</c>, what
/// <c>ykman otp chalresp</c> and KeePassXC's instructions set): it then drops every trailing byte
/// equal to the last before hashing, as python-yubico's padding shows the firmware does.
/// </param>
internal sealed class SoftwareYubiKey(byte[] secret, bool variableLength = true) : IChallengeResponseDevice
{
    /// <summary>The secret KeePassXC's own YubiKey tests program into a slot.</summary>
    internal static readonly byte[] KeePassXcTestSecret = Convert.FromHexString("1ce30fd78d20dcfa40b50c18779afb0f02288db7");

    private readonly Lock _gate = new();
    private readonly List<byte[]> _challenges = [];

    /// <summary>The slots programmed with <c>secret</c>.</summary>
    internal IReadOnlyList<int> Slots { get; init; } = [1, 2];

    /// <summary>Whether a key is plugged in.</summary>
    internal bool Connected { get; set; } = true;

    /// <summary>Whether the slot waits for a touch before answering.</summary>
    internal bool RequiresTouch { get; init; }

    /// <summary>When set, the key waits for a touch that never comes until the ask is cancelled.</summary>
    internal bool NeverTouched { get; set; }

    /// <summary>Every 64-byte challenge the key has hashed.</summary>
    internal IReadOnlyList<byte[]> Challenges
    {
        get
        {
            lock (_gate)
            {
                return [.. _challenges];
            }
        }
    }

    public IReadOnlyList<HardwareKeyInfo> Find() =>
        Connected ? [new HardwareKeyInfo("YubiKey 5", 12345678, Slots)] : [];

    public byte[] Respond(int slot, byte[] challenge, Action touchNeeded, CancellationToken cancellationToken)
    {
        if (!Connected)
        {
            throw new HardwareKeyException(HardwareKeyFailure.NotFound, "No hardware key is connected.");
        }

        if (!Slots.Contains(slot))
        {
            throw new HardwareKeyException(HardwareKeyFailure.SlotNotConfigured, $"Slot {slot} is not programmed.");
        }

        if (RequiresTouch || NeverTouched)
        {
            touchNeeded();
        }

        if (NeverTouched)
        {
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            cancellationToken.ThrowIfCancellationRequested();
            throw new HardwareKeyException(HardwareKeyFailure.TimedOut, "The hardware key was not touched.");
        }

        lock (_gate)
        {
            _challenges.Add([.. challenge]);
        }

        return Answer(secret, challenge, variableLength);
    }

    /// <summary>What a slot programmed with <paramref name="key"/> answers to a 64-byte challenge.</summary>
    internal static byte[] Answer(byte[] key, byte[] challenge, bool variableLength)
    {
        var length = challenge.Length;
        if (variableLength)
        {
            var pad = challenge[^1];
            while (length > 0 && challenge[length - 1] == pad)
            {
                length--;
            }
        }

#pragma warning disable CA5350 // HMAC-SHA1 is what the key computes; this emulates it and chooses nothing.
        return HMACSHA1.HashData(key, challenge.AsSpan(0, length));
#pragma warning restore CA5350
    }
}
