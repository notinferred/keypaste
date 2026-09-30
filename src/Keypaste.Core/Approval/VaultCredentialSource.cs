using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Approval;

/// <summary>
/// Reads one field out of an unlocked vault, and refuses everything it cannot do unambiguously.
/// </summary>
/// <remarks>
/// <para>
/// The vault arrives through a delegate rather than as a constructor argument because the approver
/// auto-locks on idle: the session disposes its <see cref="Vault"/> and returns
/// <see langword="null"/> here until the human unlocks again, which turns an expired session into
/// <see cref="CredentialFailure.VaultLocked"/> rather than into a disposed-object exception.
/// </para>
/// <para>
/// Every read is of the vault as its file holds it (<see cref="Vault.ReadSaved(out IReadOnlyList{VaultEntry}?)"/>), so an edit not yet
/// saved and a file somebody else has written are refused rather than answered from memory (D-0317).
/// </para>
/// <para>
/// Every failure path returns <see langword="false"/>. Nothing here throws for a request it cannot
/// satisfy, and nothing here guesses (docs/PRODUCT.md law 3.7).
/// </para>
/// </remarks>
/// <param name="unlockedVault">
/// The vault currently unlocked in this process, or <see langword="null"/> when none is.
/// </param>
public sealed class VaultCredentialSource(Func<Vault?> unlockedVault) : ICredentialSource
{
    private readonly Func<Vault?> _unlockedVault =
        unlockedVault ?? throw new ArgumentNullException(nameof(unlockedVault));

    /// <inheritdoc/>
    /// <remarks>
    /// The entry's path or any of its own tags, read from the open vault; a request it answers is
    /// released only after a read of the saved file, which refuses a tag not yet saved. It fails
    /// closed: with no vault, no single such entry or no way to read its tags, the answer is yes.
    /// </remarks>
    public bool RequiresLiveApproval(EntryName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        try
        {
            return _unlockedVault()?.Tags(name) is not { } tags || EnvProfileNames.RequiresLiveApproval(name, tags);
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return true;
        }
    }

    /// <inheritdoc/>
    public bool TryResolve(
        string entryArgument,
        [NotNullWhen(true)] out EntryName? name,
        out CredentialFailure failure)
    {
        ArgumentNullException.ThrowIfNull(entryArgument);

        name = null;

        if (!TryReadEntries(out var entries, out failure))
        {
            return false;
        }

        if (entryArgument.Length == 0)
        {
            failure = CredentialFailure.NotFound;
            return false;
        }

        // Handles first, an exact path second. An entry can legitimately be titled something
        // handle-shaped (EntryHandle.Classify says so), so a handle matching nothing must still be
        // tried as a path or that entry becomes permanently unreachable.
        if (EntryHandle.LooksLikeHandle(entryArgument))
        {
            var byHandle = Single(
                entries,
                entry => string.Equals(EntryHandle.For(EntryName.Of(entry)), entryArgument, StringComparison.Ordinal),
                out failure);

            if (byHandle is not null)
            {
                name = EntryName.Of(byHandle);
                return true;
            }

            if (failure == CredentialFailure.Ambiguous)
            {
                return false;
            }
        }

        var byPath = Single(
            entries,
            entry => string.Equals(entry.Path, entryArgument, StringComparison.Ordinal),
            out failure);

        if (byPath is null)
        {
            return false;
        }

        name = EntryName.Of(byPath);
        return true;
    }

    /// <inheritdoc/>
    public bool TryRead(
        EntryName name,
        string field,
        [NotNullWhen(true)] out ReleasedField? value,
        out CredentialFailure failure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        value = null;

        if (!CredentialFields.IsReleasable(field))
        {
            failure = CredentialFailure.NoSuchField;
            return false;
        }

        if (!TryReadEntries(out var entries, out failure))
        {
            return false;
        }

        // Looked up by name rather than carried over from TryResolve, because the vault can be
        // edited while the human is deciding, and the entry they approved is the one identified by
        // name — not a position in a list read before they answered.
        var entry = Single(
            entries,
            candidate => string.Equals(candidate.GroupPath, name.GroupPath, StringComparison.Ordinal)
                && string.Equals(candidate.Title, name.Title, StringComparison.Ordinal),
            out failure);

        if (entry is null)
        {
            return false;
        }

        if (!TrySelect(entry, name, field, out var text, out failure))
        {
            return false;
        }

        if (text.Length == 0)
        {
            failure = CredentialFailure.Empty;
            return false;
        }

        value = new ReleasedField(field, text);
        failure = CredentialFailure.None;
        return true;
    }

    /// <summary>Why a vault that does not match its file answered nothing.</summary>
    internal static CredentialFailure Failure(SavedRead state) => state switch
    {
        SavedRead.Current => CredentialFailure.None,
        SavedRead.ChangedOnDisk => CredentialFailure.ChangedOnDisk,
        SavedRead.Unsaved => CredentialFailure.Unsaved,
        _ => CredentialFailure.Failed,
    };

    /// <summary>The field's text: a standard one from the entry read, a custom one from the saved file, read only now.</summary>
    private bool TrySelect(VaultEntry entry, EntryName name, string field, out string text, out CredentialFailure failure)
    {
        text = string.Empty;
        failure = CredentialFailure.None;

        if (CredentialFields.IsStandard(field))
        {
            text = field switch
            {
                "password" => entry.Password,
                "username" => entry.Username,
                "url" => entry.Url,
                _ => entry.Notes,
            };

            return true;
        }

        if (_unlockedVault() is not { } vault)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        try
        {
            failure = Failure(vault.ReadSavedField(name, field, out var custom));
            text = custom ?? string.Empty;
        }
        catch (Exception)
        {
            // As in TryReadEntries: whatever the vault throws, the answer is a refusal (law 3.7).
            failure = CredentialFailure.Failed;
        }

        return failure == CredentialFailure.None;
    }

    /// <summary>The one entry matching a predicate, or null with the reason there is not exactly one.</summary>
    private static VaultEntry? Single(
        IReadOnlyList<VaultEntry> entries,
        Func<VaultEntry, bool> matches,
        out CredentialFailure failure)
    {
        VaultEntry? found = null;

        for (var i = 0; i < entries.Count; i++)
        {
            if (!matches(entries[i]))
            {
                continue;
            }

            if (found is not null)
            {
                // Two entries answer to one name. Denying both is the only fail-closed answer,
                // and the handle form is what keeps each of them individually addressable.
                failure = CredentialFailure.Ambiguous;
                return null;
            }

            found = entries[i];
        }

        failure = found is null ? CredentialFailure.NotFound : CredentialFailure.None;
        return found;
    }

    private bool TryReadEntries(
        [NotNullWhen(true)] out IReadOnlyList<VaultEntry>? entries,
        out CredentialFailure failure)
    {
        entries = null;

        var vault = _unlockedVault();

        if (vault is null)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        SavedRead state;

        try
        {
            state = vault.ReadSaved(out entries);
        }
        catch (Exception)
        {
            // Anything at all. A narrower filter lets an IOException or a cryptographic failure out
            // of the vault escape the approver and reach the bridge as an unlogged failure; failing
            // closed here is what makes the caller's refusal a decision (law 3.7).
            failure = CredentialFailure.Failed;
            return false;
        }

        failure = Failure(state);
        return entries is not null;
    }
}
