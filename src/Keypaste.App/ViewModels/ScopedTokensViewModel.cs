using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Tokens;

namespace Keypaste.App.ViewModels;

/// <summary>One scoped token as Agents › Scoped tokens lists it: never its secret or its verifier.</summary>
/// <param name="Id">The token's id, which a revoke names.</param>
/// <param name="Name">The name, scrubbed.</param>
/// <param name="Prefix">The token's id as it is shown, <c>kpt_7d2e91c0…</c>.</param>
/// <param name="Scope">Its scopes, joined.</param>
/// <param name="Mode">What it can do with what it reads.</param>
/// <param name="Expires">How long it has left, such as <c>29 days</c>, or <c>expired</c>.</param>
/// <param name="IsExpired">Whether it has stopped working.</param>
internal sealed record ScopedTokenRow(string Id, string Name, string Prefix, string Scope, string Mode, string Expires, bool IsExpired);

/// <summary>
/// Agents › Scoped tokens: the tokens the session's vault holds, minting one and revoking one.
/// </summary>
/// <remarks>
/// The desktop owns the vault while it is unlocked, so this is where tokens are made and revoked
/// then; the CLI verbs that save are refused while it holds it. A minted token is handed back once
/// from <see cref="Create"/> and kept nowhere here.
/// </remarks>
internal sealed class ScopedTokensViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private IReadOnlyList<ScopedTokenRow> _rows = [];

    internal ScopedTokensViewModel(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Refresh();
    }

    /// <summary>Every token, by name.</summary>
    internal IReadOnlyList<ScopedTokenRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>Reads the vault's tokens again.</summary>
    internal void Refresh()
    {
        if (_session.Unlocked is not { } vault)
        {
            Rows = [];
            return;
        }

        var now = _session.Clock.GetUtcNow();

        Rows = [.. new TokenStore(vault).List().Select(info => new ScopedTokenRow(
            info.Id,
            EntryNameSanitizer.Sanitize(info.Name).Text,
            info.Prefix,
            string.Join(", ", info.Scopes),
            TokenInfo.Mode,
            Remaining(info, now),
            info.IsExpired(now)))];
    }

    /// <summary>Mints a token and saves the vault.</summary>
    /// <param name="name">What to call it.</param>
    /// <param name="scopes">Its scopes, comma-separated, as <c>--scope</c> takes them.</param>
    /// <param name="ttl">How long it lasts.</param>
    /// <param name="allowProd">Whether a protected profile may be named, which then asks live.</param>
    /// <returns>The token to show once, or why none was made.</returns>
    internal (bool Ok, string? Token, string Message) Create(string name, string scopes, TimeSpan ttl, bool allowProd)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(scopes);

        if (_session.Unlocked is not { } vault)
        {
            return (false, null, "The vault is locked.");
        }

        if (!TokenScope.TryParseList(scopes, out var parsed, out var scopeError))
        {
            return (false, null, scopeError);
        }

        var store = new TokenStore(vault);

        if (!store.TryCreate(name, parsed, ttl, allowProd, _session.Clock.GetUtcNow(), out var info, out var token, out var error))
        {
            return (false, null, error);
        }

        if (!TrySave(vault, out var saveError))
        {
            store.RevokeId(info.Id);
            return (false, null, saveError);
        }

        Refresh();
        return (true, token, $"Copy {info.Prefix} now: it is shown once and keypaste keeps only a verifier.");
    }

    /// <summary>Deletes a token for good and saves the vault.</summary>
    /// <param name="row">The token, as <see cref="Rows"/> listed it.</param>
    /// <returns>Why nothing was revoked, or null when it was.</returns>
    internal string? Revoke(ScopedTokenRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_session.Unlocked is not { } vault)
        {
            return "The vault is locked.";
        }

        if (!new TokenStore(vault).RevokeId(row.Id))
        {
            Refresh();
            return "That token is no longer in the vault.";
        }

        var failure = TrySave(vault, out var error) ? null : error;
        Refresh();
        return failure;
    }

    public void Dispose() => Rows = [];

    private static bool TrySave(Vault vault, out string error)
    {
        try
        {
            vault.Save();
            error = string.Empty;
            return true;
        }
        catch (VaultChangedOnDiskException)
        {
            error = "Something else changed this vault since you opened it. Lock and unlock to see it, then try again.";
            return false;
        }
        catch (VaultException e)
        {
            error = e.Message;
            return false;
        }
    }

    private static string Remaining(TokenInfo info, DateTimeOffset now)
    {
        if (info.IsExpired(now))
        {
            return "expired";
        }

        var left = info.Expires - now;

        return left.TotalDays >= 1 ? Plural((int)left.TotalDays, "day")
            : left.TotalHours >= 1 ? Plural((int)left.TotalHours, "hour")
            : Plural(Math.Max(1, (int)left.TotalMinutes), "minute");

        static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? string.Empty : "s")}";
    }
}
