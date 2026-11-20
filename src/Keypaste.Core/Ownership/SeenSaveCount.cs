using System.Globalization;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ownership;

/// <summary>The save count of the copy of one vault this home last opened or saved, so an unlock can tell that the file was put back to an older copy.</summary>
/// <remarks>
/// <para>
/// Each keypaste save counts up a count the vault carries and other programs' saves leave it as it is, so only a copy older than
/// one this home saw reads lower. A vault without the count, or a home without the file, warns of nothing.
/// </para>
/// <para>
/// Read before the vault's file, so a save another process of this home makes meanwhile can only make the file read higher.
/// A file that cannot be read or written is no count and costs nothing more: a warning is advice, and an unlock is never refused for it.
/// </para>
/// </remarks>
internal sealed class SeenSaveCount
{
    private readonly string _path;

    private SeenSaveCount(string path, long? count)
    {
        _path = path;
        Count = count;
    }

    /// <summary>The count remembered, or null when this home remembers none.</summary>
    internal long? Count { get; }

    /// <summary>What this home remembers of a vault.</summary>
    /// <param name="home">keypaste's home.</param>
    /// <param name="vault">The vault.</param>
    /// <returns>Its remembered count, which may be none.</returns>
    internal static SeenSaveCount Read(string home, VaultIdentity vault)
    {
        var path = KeypasteHome.SaveCountPath(home, vault);

        try
        {
            return new SeenSaveCount(
                path,
                long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SeenSaveCount(path, null);
        }
    }

    /// <summary>Whether a vault just opened carries a lower count than this home remembers, remembering its count when it differs so one older copy is warned of once.</summary>
    /// <param name="opened">The count the opened file carries, or null when it carries none.</param>
    /// <returns><see langword="true"/> when the file is older than a copy this home saw.</returns>
    internal bool IsOlder(long? opened)
    {
        if (opened is not { } count || count == Count)
        {
            return false;
        }

        Write(_path, count);
        return count < Count;
    }

    /// <summary>Remembers the count of a copy this home just saved or restored.</summary>
    /// <param name="home">keypaste's home.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="count">Its count, or null for a copy carrying none, which changes nothing.</param>
    internal static void Remember(string home, VaultIdentity vault, long? count)
    {
        if (count is { } known)
        {
            Write(KeypasteHome.SaveCountPath(home, vault), known);
        }
    }

    private static void Write(string path, long count)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, Encoding.UTF8.GetBytes(count.ToString(CultureInfo.InvariantCulture) + "\n"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
