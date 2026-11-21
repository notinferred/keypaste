using Keypaste.Core.Agents;

namespace Keypaste.Core.Ownership;

/// <summary>
/// The vault <c>keypaste approve</c> holds for its one unlock: the copy it serves and saves, which a merge of another program's save
/// replaces in the same session.
/// </summary>
/// <remarks>
/// Every save and merge runs under one edit gate, so a save waits for a merge and applies to the merged copy. A save refused because
/// another program saved the file merges that save at once and is answered as saved once the merged copy holds the change.
/// </remarks>
public sealed class HeldVault : IWatchedVault, IDisposable
{
    private readonly SessionLifetime _lifetime;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Lock _editGate = new();
    private Vault _vault;
    private SessionAuthority? _authority;
    private string _surface = string.Empty;
    private byte[]? _refusedDigest;
    private bool _disposed;

    /// <param name="vault">The vault as the unlock opened it; this holds it from now on and disposes whichever copy it ends with.</param>
    /// <param name="lifetime">The unlock, whose end locks the vault.</param>
    /// <param name="clock">The clock the file watch's poll runs on.</param>
    public HeldVault(Vault vault, SessionLifetime lifetime, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(lifetime);

        _vault = vault;
        _lifetime = lifetime;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Raised after each merge of another program's save, on the thread that merged, once the merged copy is in place.</summary>
    public event EventHandler<VaultMergeSave>? Merged;

    /// <summary>
    /// Raised after a save of the open copy could not carry something over from the file it replaced (<see cref="Vault.TakeLeftBehind"/>),
    /// naming the copy kept of the file before it; a merged copy's save says so in <see cref="VaultMergeSave.LeftBehind"/> instead.
    /// </summary>
    public event EventHandler<VaultLeftBehind>? LeftBehind;

    /// <summary>
    /// Raised after a save of the open copy replaced the file without the system flushing its folder (<see cref="Vault.UnflushedText"/>);
    /// a merged copy's save says so in <see cref="VaultMergeSave.Unflushed"/> instead.
    /// </summary>
    public event EventHandler? Unflushed;

    /// <summary>The open copy while the unlock is live; null once it has ended.</summary>
    public Vault? Open
    {
        get
        {
            lock (_gate)
            {
                return _lifetime.IsLive && !_disposed ? _vault : null;
            }
        }
    }

    /// <inheritdoc/>
    public TimeProvider Clock => _clock;

    /// <inheritdoc/>
    public ConflictedCopies ConflictedCopies { get; } = new();

    /// <inheritdoc/>
    public bool MergesAnotherSave
    {
        get
        {
            byte[]? refused;
            string path;

            lock (_gate)
            {
                if (_authority is null || _disposed)
                {
                    return false;
                }

                (refused, path) = (_refusedDigest, _vault.Path);
            }

            return refused is null || !Same(refused, SourceSnapshot.Digest(path));
        }
    }

    // One unlock, opened before this was made and never opened again.
    event EventHandler? IWatchedVault.Opened
    {
        add { }
        remove { }
    }

    event EventHandler? IWatchedVault.Locked
    {
        add { }
        remove { }
    }

    /// <summary>The open copy, while <paramref name="asked"/> is this unlock and live.</summary>
    /// <param name="asked">The lifetime a request belongs to.</param>
    public Vault? UnlockedFor(SessionLifetime asked) => ReferenceEquals(asked, _lifetime) ? Open : null;

    /// <summary>Has <paramref name="authority"/> merge another program's save for this unlock and record it as <paramref name="surface"/>.</summary>
    /// <param name="authority">The owner serving this unlock.</param>
    /// <param name="surface">The owner, as the merge's line records it.</param>
    public void MergeThrough(SessionAuthority authority, string surface)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(surface);

        lock (_gate)
        {
            (_authority, _surface) = (authority, surface);
        }
    }

    /// <summary>Applies <paramref name="edit"/> to the open copy and saves it, merging another program's save when that refused the save.</summary>
    /// <param name="edit">The change, answering false when it changed nothing.</param>
    /// <returns>What became of it; saved once the file holds the change, the merged copy's save included.</returns>
    public AgentSave Save(Func<Vault, bool> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        AgentSave saved;
        VaultMergeSave? merged = null;
        VaultLeftBehind? leftBehind = null;
        var unflushed = false;

        lock (_editGate)
        {
            saved = SaveNow(edit);

            if (saved.Outcome == AgentSaveOutcome.Saved)
            {
                leftBehind = Open?.TakeLeftBehind();
                unflushed = Open?.TakeUnflushedSave() == true;
            }

            if (saved.Outcome == AgentSaveOutcome.ChangedOnDisk)
            {
                merged = MergeNow(again: true);
                saved = merged switch
                {
                    { Outcome: VaultMergeSaveOutcome.Saved or VaultMergeSaveOutcome.Updated } => AgentSave.Saved,
                    { } refused => new AgentSave(AgentSaveOutcome.ChangedOnDisk, $"another program changed the vault file, and keypaste could not merge it: {VaultMergeSave.Refusal(refused.Outcome, refused.Reason)}"),
                    null => saved,
                };
            }
        }

        if (leftBehind is not null)
        {
            LeftBehind?.Invoke(this, leftBehind);
        }

        if (unflushed)
        {
            Unflushed?.Invoke(this, EventArgs.Empty);
        }

        Finish(merged);
        return saved;
    }

    /// <summary>Merges another program's save with the open copy, as the file watch asks.</summary>
    /// <returns>What became of it, or null when nothing merges for this unlock, it has ended, the file still holds the bytes the last merge refused, or it holds what the open copy last read or wrote.</returns>
    public VaultMergeSave? MergeAnotherSave()
    {
        VaultMergeSave? merged;

        lock (_editGate)
        {
            merged = MergeNow(again: false);
        }

        Finish(merged);
        return merged;
    }

    void IWatchedVault.MergeAnotherSave() => MergeAnotherSave();

    private AgentSave SaveNow(Func<Vault, bool> edit)
    {
        if (Open is not { } vault)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }

        try
        {
            if (!edit(vault))
            {
                return AgentSave.Unchanged;
            }

            vault.Save();
            return AgentSave.Saved;
        }
        catch (VaultChangedOnDiskException)
        {
            return new AgentSave(AgentSaveOutcome.ChangedOnDisk, "another program changed the vault file");
        }
        catch (VaultException ex)
        {
            return new AgentSave(AgentSaveOutcome.Failed, ex.Message);
        }
        catch (ObjectDisposedException)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }
    }

    /// <summary>Merges through the owner serving this unlock. Called under the edit gate.</summary>
    /// <param name="again">Whether to merge a file whose bytes the last merge refused, as a refused save does.</param>
    private VaultMergeSave? MergeNow(bool again)
    {
        Vault? vault;
        SessionAuthority? authority;
        string surface;

        lock (_gate)
        {
            vault = _lifetime.IsLive && !_disposed ? _vault : null;
            authority = _authority;
            surface = _surface;
        }

        if (vault is null || authority is null)
        {
            return null;
        }

        var digest = SourceSnapshot.Digest(vault.Path);

        lock (_gate)
        {
            if (!again && Same(_refusedDigest, digest))
            {
                return null;
            }
        }

        // A merge withdraws every prompt before it reads the file, so one asked twice for one save must find nothing to do.
        try
        {
            if (vault.SaveState().Status is not (VaultSaveStatus.ChangedOnDisk or VaultSaveStatus.Unreadable))
            {
                return null;
            }
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        VaultMergeSave merged;

        try
        {
            merged = authority.Merge(vault, copy => Swap(vault, copy), surface);
        }
        catch (VaultException e)
        {
            merged = new VaultMergeSave(VaultMergeSaveOutcome.SaveFailed, e.Message);
        }
        catch (ObjectDisposedException)
        {
            merged = new VaultMergeSave(VaultMergeSaveOutcome.Locked, "The vault was locked during the merge.");
        }

        lock (_gate)
        {
            if (merged.Outcome is VaultMergeSaveOutcome.Saved or VaultMergeSaveOutcome.Updated or VaultMergeSaveOutcome.Unchanged)
            {
                _refusedDigest = null;
            }
            else if (merged.Outcome != VaultMergeSaveOutcome.Locked)
            {
                _refusedDigest = digest;
            }
        }

        return merged;
    }

    /// <summary>Says what a merge came to. Called outside the edit gate, so whoever is told may save again.</summary>
    private void Finish(VaultMergeSave? merged)
    {
        if (merged is not null)
        {
            Merged?.Invoke(this, merged);
        }
    }

    /// <summary>Puts <paramref name="merged"/> in the place of <paramref name="expected"/>.</summary>
    /// <returns>False, having disposed <paramref name="merged"/>, when the unlock ended or another copy took its place meanwhile.</returns>
    private bool Swap(Vault expected, Vault merged)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsLive || !ReferenceEquals(_vault, expected))
            {
                merged.Dispose();
                return false;
            }

            _vault = merged;
        }

        expected.Dispose();
        return true;
    }

    private static bool Same(byte[]? one, byte[]? other) =>
        one is not null && other is not null && CryptographicOperations.FixedTimeEquals(one, other);

    /// <inheritdoc/>
    public void Dispose()
    {
        Vault vault;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            vault = _vault;
        }

        vault.Dispose();
    }
}
