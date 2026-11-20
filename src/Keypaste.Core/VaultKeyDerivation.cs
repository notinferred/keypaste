using Keypaste.Core.Internal;

namespace Keypaste.Core;

/// <summary>The function a vault turns its password, key file and hardware key into the key that opens it with.</summary>
public enum KeyDerivationFunction
{
    /// <summary>One keypaste does not know.</summary>
    Unknown,

    /// <summary>AES-KDF, which KeePass used before Argon2 and which costs a guesser no memory.</summary>
    AesKdf,

    /// <summary>Argon2d, which keypaste writes.</summary>
    Argon2d,

    /// <summary>Argon2id.</summary>
    Argon2id,
}

/// <summary>How a vault derives the key that opens it, as its header states it.</summary>
/// <param name="Function">The function.</param>
/// <param name="Iterations">Argon2's passes over memory, or AES-KDF's rounds.</param>
/// <param name="Memory">Argon2's memory in bytes; 0 for AES-KDF.</param>
/// <param name="Parallelism">Argon2's lanes; 0 for AES-KDF.</param>
public sealed record VaultKeyDerivation(KeyDerivationFunction Function, ulong Iterations, ulong Memory, uint Parallelism)
{
    /// <summary>What keypaste writes in a vault it creates: <see cref="KdbxFormat"/>'s Argon2d, or the token cost in a test that asks for it.</summary>
    public static VaultKeyDerivation Keypastes => new(
        KeyDerivationFunction.Argon2d,
        TestKeyDerivation.Applies ? TestKeyDerivation.Iterations : KdbxFormat.Argon2Iterations,
        TestKeyDerivation.Applies ? TestKeyDerivation.Memory : KdbxFormat.Argon2Memory,
        KdbxFormat.Argon2Parallelism);

    /// <summary>
    /// Whether it is weaker than <see cref="Keypastes"/>: AES-KDF at any rounds, or Argon2 with fewer passes, less memory or
    /// fewer lanes. Argon2id is not weaker than Argon2d at the same cost.
    /// </summary>
    public bool IsWeakerThanKeypastes
    {
        get
        {
            var keypastes = Keypastes;

            return Function == KeyDerivationFunction.AesKdf
                || (IsArgon2 && (Iterations < keypastes.Iterations || Memory < keypastes.Memory || Parallelism < keypastes.Parallelism));
        }
    }

    /// <summary>What strengthening makes of it: <see cref="Keypastes"/>, as Argon2id where it is Argon2id, keeping any Argon2 cost it already exceeds.</summary>
    public VaultKeyDerivation Strengthened
    {
        get
        {
            var keypastes = Keypastes;

            return IsArgon2
                ? new(Function, Math.Max(Iterations, keypastes.Iterations), Math.Max(Memory, keypastes.Memory), Math.Max(Parallelism, keypastes.Parallelism))
                : keypastes;
        }
    }

    private bool IsArgon2 => Function is KeyDerivationFunction.Argon2d or KeyDerivationFunction.Argon2id;

    /// <summary><c>AES-KDF</c>, <c>Argon2d</c>, <c>Argon2id</c> or <c>unknown</c>.</summary>
    public string Name => Function switch
    {
        KeyDerivationFunction.AesKdf => "AES-KDF",
        KeyDerivationFunction.Argon2d => "Argon2d",
        KeyDerivationFunction.Argon2id => "Argon2id",
        _ => "unknown",
    };

    /// <summary>Reads it from a vault's unencrypted outer header, which needs no key.</summary>
    /// <param name="path">The vault.</param>
    /// <returns>What the header states, or null when the file is not a KDBX 3 or 4 file whose header is whole.</returns>
    public static VaultKeyDerivation? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return KeePassInterop.Probe(path, out _)?.KeyDerivation;
    }
}
