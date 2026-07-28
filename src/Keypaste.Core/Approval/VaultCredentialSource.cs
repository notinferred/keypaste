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
/// <see cref="CredentialFailure.VaultLocked"/> rather than into a disposed-object exception. It
/// also means live grants outlive the session, since the grant cache holds values rather than a
/// capability to re-read one.
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

        var text = Select(entry, field);

        if (text.Length == 0)
        {
            failure = CredentialFailure.Empty;
            return false;
        }

        value = new ReleasedField(field, text);
        failure = CredentialFailure.None;
        return true;
    }

    private static string Select(VaultEntry entry, string field) => field switch
    {
        "password" => entry.Password,
        "username" => entry.Username,
        "url" => entry.Url,
        "notes" => entry.Notes,
        _ => string.Empty,
    };

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

        try
        {
            entries = vault.ReadEntries();
        }
        catch (Exception ex) when (ex is VaultException or ObjectDisposedException)
        {
            failure = CredentialFailure.Failed;
            return false;
        }

        failure = CredentialFailure.None;
        return true;
    }
}
