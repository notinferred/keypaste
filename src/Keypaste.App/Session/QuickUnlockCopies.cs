using System.Security.Cryptography;
using System.Text;
using Keypaste.Core;
using Keypaste.Core.Ownership;

namespace Keypaste.App.Session;

/// <summary>What keeping or opening a quick unlock's copy came to.</summary>
internal enum QuickUnlockOutcome
{
    /// <summary>The copy is kept, or opened.</summary>
    Done = 0,

    /// <summary>The person cancelled; a kept copy stays.</summary>
    Cancelled = 1,

    /// <summary>The signer cannot be asked now; a kept copy stays.</summary>
    Unavailable = 2,

    /// <summary>The signer was asked and gave no signature; a kept copy stays.</summary>
    Failed = 3,

    /// <summary>The signature no longer opens the copy, so it was dropped.</summary>
    Unreadable = 4,

    /// <summary>No copy is kept for the vault, or a lock forgot it while the person was asked.</summary>
    None = 5,
}

/// <summary>
/// The key parts of vaults unlocked with their password, each sealed with AES-256-GCM under the SHA-256 of a signature
/// only the person's Windows Hello gives, and kept in this process's memory alone, by the vault's identity (D-0550).
/// </summary>
/// <remarks>
/// <para>
/// As KeePassXC 2.8's quick unlock keeps them: each copy has a fresh random challenge, signed again to open it, and a
/// random nonce. The vault's identity is the associated data, so a copy opens only for the vault it was made for.
/// </para>
/// <para>
/// <b>Forgetting a vault counts.</b> A copy is stored only if its vault was not forgotten since its parts were read,
/// so a lock that forces the full unlock while the person is still being asked leaves nothing behind.
/// </para>
/// </remarks>
internal sealed class QuickUnlockCopies : IDisposable
{
    private const int _challengeLength = 32;
    private const int _nonceLength = 12;
    private const int _tagLength = 16;

    private readonly IQuickUnlockSigner _signer;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Sealed> _copies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _forgotten = new(StringComparer.Ordinal);
    private bool _disposed;

    internal QuickUnlockCopies(IQuickUnlockSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        _signer = signer;
    }

    /// <summary>What the person knows the signer as, such as "Windows Hello".</summary>
    internal string Name => _signer.Name;

    /// <summary>How many vaults have a copy kept.</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _copies.Count;
            }
        }
    }

    /// <summary>Whether a copy is kept for <paramref name="vault"/>.</summary>
    internal bool Has(VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        lock (_gate)
        {
            return _copies.ContainsKey(vault.Key);
        }
    }

    /// <summary>How many times <paramref name="vault"/> has been forgotten, read before its parts are, for <see cref="Keep"/>.</summary>
    internal long Generation(VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        lock (_gate)
        {
            return _forgotten.GetValueOrDefault(vault.Key);
        }
    }

    /// <summary>Asks the person, then keeps <paramref name="parts"/> sealed for <paramref name="vault"/>, replacing any copy it had.</summary>
    /// <param name="vault">The vault the parts open.</param>
    /// <param name="parts">The parts, which stay the caller's.</param>
    /// <param name="generation">What <see cref="Generation"/> said before the parts were read.</param>
    /// <returns>What came of it; <see cref="QuickUnlockOutcome.None"/> when the vault was forgotten meanwhile.</returns>
    internal QuickUnlockOutcome Keep(VaultIdentity vault, VaultKeyParts parts, long generation)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(parts);

        if (!_signer.IsAvailable())
        {
            return QuickUnlockOutcome.Unavailable;
        }

        var challenge = RandomNumberGenerator.GetBytes(_challengeLength);

        if (Sign(challenge, out var key) is not QuickUnlockOutcome.Done and var refused)
        {
            return refused;
        }

        var nonce = RandomNumberGenerator.GetBytes(_nonceLength);
        var plain = parts.ToBytes();
        var cipher = new byte[plain.Length];
        var tag = new byte[_tagLength];

        try
        {
            using var aes = new AesGcm(key, _tagLength);
            aes.Encrypt(nonce, plain, cipher, tag, Bound(vault));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }

        lock (_gate)
        {
            if (_disposed || _forgotten.GetValueOrDefault(vault.Key) != generation)
            {
                return QuickUnlockOutcome.None;
            }

            _copies[vault.Key] = new Sealed(challenge, nonce, cipher, tag);
        }

        return QuickUnlockOutcome.Done;
    }

    /// <summary>Asks the person, then opens the copy kept for <paramref name="vault"/>.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="parts">The parts, which the caller disposes, when it is opened; otherwise null.</param>
    /// <returns>What came of it; a copy the signature no longer opens is dropped, and any other refusal keeps it.</returns>
    internal QuickUnlockOutcome Open(VaultIdentity vault, out VaultKeyParts? parts)
    {
        ArgumentNullException.ThrowIfNull(vault);
        parts = null;

        Sealed? copy;
        lock (_gate)
        {
            _copies.TryGetValue(vault.Key, out copy);
        }

        if (copy is null)
        {
            return QuickUnlockOutcome.None;
        }

        if (!_signer.IsAvailable())
        {
            return QuickUnlockOutcome.Unavailable;
        }

        if (Sign(copy.Challenge, out var key) is not QuickUnlockOutcome.Done and var refused)
        {
            return refused;
        }

        var plain = new byte[copy.Cipher.Length];

        try
        {
            using var aes = new AesGcm(key, _tagLength);
            aes.Decrypt(copy.Nonce, copy.Cipher, copy.Tag, plain, Bound(vault));
            parts = VaultKeyParts.FromBytes(plain);
            return QuickUnlockOutcome.Done;
        }
        catch (Exception e) when (e is AuthenticationTagMismatchException or VaultException)
        {
            Drop(vault, copy);
            return QuickUnlockOutcome.Unreadable;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Drops the copy kept for <paramref name="vault"/>, and any being made for it, so only a full unlock opens it next.</summary>
    internal void Forget(VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        lock (_gate)
        {
            _forgotten[vault.Key] = _forgotten.GetValueOrDefault(vault.Key) + 1;
            _copies.Remove(vault.Key, out var copy);
            copy?.Clear();
        }
    }

    /// <summary>What is kept for <paramref name="vault"/>, as stored, for a test to look at; null when nothing is.</summary>
    internal byte[]? SealedFor(VaultIdentity vault)
    {
        lock (_gate)
        {
            return _copies.TryGetValue(vault.Key, out var copy) ? [.. copy.Challenge, .. copy.Nonce, .. copy.Cipher, .. copy.Tag] : null;
        }
    }

    /// <summary>Drops every copy, and refuses any still being made: the app is quitting.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;

            foreach (var copy in _copies.Values)
            {
                copy.Clear();
            }

            _copies.Clear();
        }
    }

    private void Drop(VaultIdentity vault, Sealed copy)
    {
        lock (_gate)
        {
            if (_copies.TryGetValue(vault.Key, out var kept) && ReferenceEquals(kept, copy))
            {
                _copies.Remove(vault.Key);
                copy.Clear();
            }
        }
    }

    /// <summary>Asks the signer and turns its signature into a 32-byte key, zeroing the signature.</summary>
    private QuickUnlockOutcome Sign(byte[] challenge, out byte[] key)
    {
        key = [];
        var outcome = _signer.Sign(challenge, out var signature);

        if (signature is null || outcome != SignOutcome.Signed)
        {
            CryptographicOperations.ZeroMemory(signature);

            return outcome switch
            {
                SignOutcome.Cancelled => QuickUnlockOutcome.Cancelled,
                SignOutcome.Unavailable => QuickUnlockOutcome.Unavailable,
                _ => QuickUnlockOutcome.Failed,
            };
        }

        try
        {
            key = SHA256.HashData(signature);
            return QuickUnlockOutcome.Done;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static byte[] Bound(VaultIdentity vault) => Encoding.UTF8.GetBytes(vault.Key);

    private sealed record Sealed(byte[] Challenge, byte[] Nonce, byte[] Cipher, byte[] Tag)
    {
        internal void Clear()
        {
            CryptographicOperations.ZeroMemory(Cipher);
            CryptographicOperations.ZeroMemory(Tag);
        }
    }
}
