using System.Collections.Concurrent;
using Keypaste.Core.Clients;

namespace Keypaste.Core.Ipc;

/// <summary>The <c>keypaste</c> program this owner ships beside itself, against which a pipe's caller is checked.</summary>
/// <remarks>
/// A caller is keypaste's own when it runs that file, or a file of the same bytes: a Linux AppImage mounts each run at a
/// new path, so the bridge a client starts runs from another mount of the same image.
/// </remarks>
internal sealed class KeypasteBinaries
{
    private readonly IReadOnlyList<string> _known;
    private readonly ConcurrentDictionary<(string Path, long Length, DateTime Written), string> _hashes = new();

    /// <summary>Checks callers against <paramref name="known"/>, each an existing keypaste executable.</summary>
    internal KeypasteBinaries(IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);

        _known = [.. known.Where(File.Exists).Select(PathIdentity.Canonical).Distinct(StringComparer.FromComparison(PathIdentity.Comparison))];
    }

    /// <summary>The <c>keypaste</c> beside this program, or this program when it is <c>keypaste</c> itself.</summary>
    internal static KeypasteBinaries ForThisProcess { get; } = new(
        new[]
        {
            Path.Combine(AppContext.BaseDirectory, McpServerLocator.ExecutableName),
            Environment.ProcessPath is { } self && string.Equals(Path.GetFileName(self), McpServerLocator.ExecutableName, PathIdentity.Comparison) ? self : null,
        }.OfType<string>());

    /// <summary>Whether <paramref name="executable"/> is keypaste's own program.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    internal bool Holds(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        if (_known.Count == 0 || !File.Exists(executable))
        {
            return false;
        }

        var canonical = PathIdentity.Canonical(executable);

        if (_known.Any(known => string.Equals(known, canonical, PathIdentity.Comparison)))
        {
            return true;
        }

        var length = new FileInfo(canonical).Length;

        return _known.Any(known => new FileInfo(known).Length == length && string.Equals(HashOf(known), HashOf(canonical), StringComparison.Ordinal));
    }

    private string HashOf(string path)
    {
        var file = new FileInfo(path);

        return _hashes.GetOrAdd((path, file.Length, file.LastWriteTimeUtc), static key =>
        {
            using var stream = File.OpenRead(key.Path);
            return Convert.ToHexString(SHA256.HashData(stream));
        });
    }
}
