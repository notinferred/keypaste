namespace Keypaste.Core;

/// <summary>The copies a sync tool left beside an open vault when two devices saved it at once, as its owner last found them.</summary>
/// <remarks>
/// Found by the names Syncthing, Dropbox, Nextcloud, OneDrive and iCloud Drive give such a file. OneDrive's device name and
/// iCloud Drive's number make names a person also gives another vault or an old copy, so a file named only that way counts once it
/// opens with the vault's key as the same database and merging it would bring something in.
/// </remarks>
public sealed class ConflictedCopies
{
    private readonly Lock _gate = new();
    private readonly Lock _looking = new();
    private readonly Dictionary<string, Verdict> _verdicts = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _found = [];
    private int _generation;

    /// <summary>Their full paths in ordinal order, none before the first look or after <see cref="Clear"/>.</summary>
    public IReadOnlyList<string> Found
    {
        get
        {
            lock (_gate)
            {
                return _found;
            }
        }
    }

    /// <summary>Raised on the thread that looked, when what was found changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Looks beside <paramref name="vault"/> again, unless a look already runs.</summary>
    /// <param name="vault">The open vault.</param>
    /// <remarks>
    /// Blocks for two key derivations for each file named only the way another vault might be, once for each version of it and of
    /// the vault's file, and never for a vault with a hardware key, whose touch that would ask for.
    /// </remarks>
    public void Look(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        if (!_looking.TryEnter())
        {
            return;
        }

        try
        {
            int generation;

            lock (_gate)
            {
                generation = _generation;
            }

            Show(Find(vault), generation);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or VaultException)
        {
            // Locked or rekeyed while looking; the next look starts from what is open then.
        }
        finally
        {
            _looking.Exit();
        }
    }

    /// <summary>Forgets what was found, as the vault locks.</summary>
    public void Clear()
    {
        int generation;

        lock (_gate)
        {
            generation = ++_generation;
        }

        Show([], generation);
    }

    private List<string> Find(Vault vault)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(vault.Path));
        var stem = Path.GetFileNameWithoutExtension(vault.Path);
        List<string> files;

        try
        {
            files = folder is null ? [] : [.. Directory.EnumerateFiles(folder, "*.kdbx", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var gone in _verdicts.Keys.Except(files, StringComparer.Ordinal).ToList())
        {
            _verdicts.Remove(gone);
        }

        byte[]? vaultDigest = null;
        List<string> found = [];

        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (IsNamedAsConflict(stem, name)
                || (MayBeNamedAsConflict(stem, name) && vault.HardwareKey is null && BringsSomethingIn(vault, path, vaultDigest ??= SourceSnapshot.Digest(vault.Path))))
            {
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>Syncthing's <c>name.sync-conflict-…</c>, and Dropbox's and Nextcloud's <c>name (… conflicted copy …)</c>.</summary>
    private static bool IsNamedAsConflict(string stem, string name) =>
        name.StartsWith(stem + ".sync-conflict-", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(')')
            && name.Contains("conflict", StringComparison.OrdinalIgnoreCase));

    /// <summary>OneDrive's <c>name-DEVICE</c>, and iCloud Drive's <c>name 2</c> for the second version kept, <c>name 3</c> for the third.</summary>
    private static bool MayBeNamedAsConflict(string stem, string name)
    {
        if (name.Length <= stem.Length + 1 || !name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = name[(stem.Length + 1)..];

        return name[stem.Length] switch
        {
            '-' => true,
            ' ' => suffix.All(char.IsAsciiDigit) && suffix[0] != '0' && suffix != "1",
            _ => false,
        };
    }

    private bool BringsSomethingIn(Vault vault, string path, byte[]? vaultDigest)
    {
        var file = new FileInfo(path);

        if (vaultDigest is null || !file.Exists)
        {
            return false;
        }

        if (_verdicts.TryGetValue(path, out var known)
            && known.Length == file.Length
            && known.Written == file.LastWriteTimeUtc
            && known.VaultDigest.AsSpan().SequenceEqual(vaultDigest))
        {
            return known.IsCopy;
        }

        var review = vault.MergeConflictedCopy(path, save: false);
        var isCopy = review is { Outcome: VaultMergeSaveOutcome.Unchanged, Report.TookFromOpenCopy: true };
        _verdicts[path] = new Verdict(file.Length, file.LastWriteTimeUtc, vaultDigest, isCopy);
        return isCopy;
    }

    private void Show(IReadOnlyList<string> found, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || found.SequenceEqual(_found, StringComparer.Ordinal))
            {
                return;
            }

            _found = found;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Verdict(long Length, DateTime Written, byte[] VaultDigest, bool IsCopy);
}
