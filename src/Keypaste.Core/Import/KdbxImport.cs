using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Internal;

namespace Keypaste.Core.Import;

/// <summary>What another KDBX file's unencrypted header says, read without its key.</summary>
/// <param name="Path">The file, as a full path.</param>
/// <param name="FileName">Its file name.</param>
/// <param name="Version"><c>KDBX 4.1</c>, <c>KDBX 4.0</c> or <c>KDBX 3.1</c>.</param>
/// <param name="Cipher"><c>AES-256</c>, <c>ChaCha20</c>, <c>Twofish</c> or <c>unknown</c>.</param>
/// <param name="Kdf"><c>Argon2id</c>, <c>Argon2d</c>, <c>AES-KDF</c> or <c>unknown</c>.</param>
/// <param name="SiblingKeyfile">A <c>&lt;stem&gt;.keyx</c> or <c>&lt;stem&gt;.key</c> beside the file, or null.</param>
public sealed record KdbxProbe(string Path, string FileName, string Version, string Cipher, string Kdf, string? SiblingKeyfile);

/// <summary>One group of the source file and where it lands in the vault imported into.</summary>
/// <param name="Index">Which group of the source this row copies; fixed by the source.</param>
/// <param name="SourceGroup">The group's path in the source; empty for the entries directly in its root.</param>
/// <param name="EntryCount">How many entries the row copies.</param>
/// <param name="Destination">The group path it lands at, which the person may change.</param>
/// <param name="Include">Whether the row is copied.</param>
/// <param name="IsRootEntries">Whether the row is the entries directly in the source's root group.</param>
public sealed record ImportRow(int Index, string SourceGroup, int EntryCount, string Destination, bool Include, bool IsRootEntries)
{
    /// <summary>Why a default plan put an env set under the import group instead of at its own path, or renamed a group, or null.</summary>
    public string? Rerouted { get; init; }
}

/// <summary>Something about one row, or about the whole import when <see cref="Index"/> is -1.</summary>
/// <param name="Index">The row's <see cref="ImportRow.Index"/>, or -1.</param>
/// <param name="Message">What is wrong, naming groups and titles but never a value.</param>
/// <param name="Blocks">Whether it stops the import; a row that does not block is only reported.</param>
public sealed record ImportProblem(int Index, string Message, bool Blocks);

/// <summary>Where every group of a source file lands.</summary>
/// <param name="Rows">One row per group the source offers.</param>
/// <param name="Into">The group a default plan collects everything under.</param>
public sealed record ImportPlan(IReadOnlyList<ImportRow> Rows, string Into);

/// <summary>What an import copied.</summary>
/// <param name="Entries">How many entries were copied.</param>
/// <param name="Groups">How many groups the vault gained.</param>
/// <param name="DuplicateTitles">How many copied entries share their group and title with another entry.</param>
/// <param name="Edit">The names the copy added, as <see cref="Vault.Edited"/> reported them.</param>
public sealed record ImportResult(int Entries, int Groups, int DuplicateTitles, VaultEdit Edit);

/// <summary>A group of the source that is never copied: its recycle bin or keypaste's own records.</summary>
/// <param name="SourceGroup">The group's path in the source.</param>
/// <param name="EntryCount">How many entries it holds.</param>
public sealed record ImportSkip(string SourceGroup, int EntryCount);

/// <summary>Reading another KDBX file to copy it into the vault in use.</summary>
/// <remarks>
/// The source is opened read-only and never saved. Keeping a file in place instead is opening it as
/// the vault, which needs nothing here beyond checking that it unlocks.
/// </remarks>
public static class KdbxImport
{
    /// <summary>Reads a file's outer header, which needs no key.</summary>
    /// <param name="path">The file.</param>
    /// <param name="probe">What the header says, when it is a KDBX file.</param>
    /// <param name="error">Why not, otherwise.</param>
    /// <returns><see langword="true"/> for a KDBX 3 or 4 file whose header is whole.</returns>
    public static bool TryProbe(string path, [NotNullWhen(true)] out KdbxProbe? probe, out string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var full = Path.GetFullPath(path);
        probe = KeePassInterop.Probe(full, out error);
        if (probe is null)
        {
            return false;
        }

        probe = probe with { SiblingKeyfile = SiblingKeyfile(full) };
        return true;
    }

    /// <summary>Unlocks a file to copy from.</summary>
    /// <param name="path">The file.</param>
    /// <param name="password">Its password; empty with a keyfile is a keyfile-only file.</param>
    /// <param name="keyfilePath">Its keyfile, or null.</param>
    /// <returns>The unlocked source, which holds its key until disposed.</returns>
    /// <exception cref="InvalidMasterPasswordException">The factors do not open it.</exception>
    /// <exception cref="VaultException">It is not a readable KDBX file.</exception>
    public static ImportSource Open(string path, ReadOnlySpan<char> password, string? keyfilePath)
    {
        if (!TryProbe(path, out var probe, out var error))
        {
            throw new VaultException(error);
        }

        if (keyfilePath is not null && VaultKeyfile.Inspect(keyfilePath).Outcome == KeyfileOutcome.XmlUnreadable)
        {
            throw new UnreadableKeyfileException(keyfilePath);
        }

        var utf8 = new byte[Encoding.UTF8.GetByteCount(password)];
        try
        {
            Encoding.UTF8.GetBytes(password, utf8);
            return new ImportSource(KeePassInterop.OpenReadOnly(probe.Path, utf8, keyfilePath), probe);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static string? SiblingKeyfile(string path)
    {
        var stem = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path));
        return File.Exists(stem + ".keyx") ? stem + ".keyx"
            : File.Exists(stem + ".key") ? stem + ".key"
            : null;
    }
}
