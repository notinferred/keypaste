using System.Globalization;
using Keypaste.App.Clipboard;
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
internal sealed record ScopedTokenRow(string Id, string Name, string Prefix, string Scope, string Mode, string Expires, bool IsExpired)
{
    /// <summary>Whether the row is asking to confirm its revoke, which cannot be undone.</summary>
    internal bool IsConfirming { get; init; }

    internal string ConfirmText => $"Revoke {Name}? Anything using it stops working.";
}

/// <summary>
/// Agents › Scoped tokens: the tokens the session's vault holds, minting one and revoking one.
/// </summary>
/// <remarks>
/// The desktop owns the vault while it is unlocked, so this is where tokens are made and revoked
/// then; the CLI verbs that save are refused while it holds it. <see cref="Create"/> hands a minted
/// token back once. The form keeps it in <see cref="Minted"/> only until it is dismissed or the
/// screen closes, drawn while held and copied through the clipboard countdown, never in a property.
/// </remarks>
internal sealed class ScopedTokensViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown? _clipboard;
    private readonly Action<string> _toast;
    private IReadOnlyList<ScopedTokenRow> _rows = [];
    private bool _isFormOpen;
    private string _name = string.Empty;
    private string _scope = string.Empty;
    private string _expiry = ExpiryOptions[1];
    private string _formError = string.Empty;
    private string _message = string.Empty;
    private MintedToken? _minted;

    internal ScopedTokensViewModel(AppVaultSession session, ClipboardCountdown? clipboard = null, Action<string>? toast = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _clipboard = clipboard;
        _toast = toast ?? (_ => { });

        OpenFormCommand = new RelayCommand(() => IsFormOpen = true, () => CanOpenForm);
        CancelFormCommand = new RelayCommand(CloseForm);
        CreateCommand = new RelayCommand(CreateFromForm);
        CopyMintedCommand = new AsyncRelayCommand(CopyMintedAsync, () => _minted is not null && _clipboard is not null);
        DoneMintedCommand = new RelayCommand(() => SetMinted(null));
        RevokeCommand = new RelayCommand<ScopedTokenRow>(RevokeRow, row => row is not null);
        AskRevokeCommand = new RelayCommand<ScopedTokenRow>(row => Confirm(row?.Id), row => row is not null);
        CancelRevokeCommand = new RelayCommand(() => Confirm(null));
        SetExpiryCommand = new RelayCommand<string>(chosen => Expiry = chosen!);
        Refresh();
    }

    /// <summary>The lifetimes the form offers.</summary>
    internal static IReadOnlyList<string> ExpiryOptions { get; } = ["7d", "30d", "90d"];

    /// <summary>Chooses one of <see cref="ExpiryOptions"/>.</summary>
    internal RelayCommand<string> SetExpiryCommand { get; }

    internal bool IsFormOpen
    {
        get => _isFormOpen;
        private set
        {
            if (Set(ref _isFormOpen, value))
            {
                FormError = string.Empty;
                RaiseCanOpenForm();
            }
        }
    }

    /// <summary>What the new token is called: lowercase letters, digits and dashes.</summary>
    internal string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>The new token's scopes, comma-separated, as <c>--scope</c> takes them.</summary>
    internal string Scope
    {
        get => _scope;
        set => Set(ref _scope, value ?? string.Empty);
    }

    /// <summary>One of <see cref="ExpiryOptions"/>.</summary>
    internal string Expiry
    {
        get => _expiry;
        set
        {
            if (value is not null && ExpiryOptions.Contains(value))
            {
                Set(ref _expiry, value);
            }
        }
    }

    /// <summary>Why the form made no token, or empty.</summary>
    internal string FormError
    {
        get => _formError;
        private set
        {
            if (Set(ref _formError, value))
            {
                Raise(nameof(HasFormError));
            }
        }
    }

    internal bool HasFormError => _formError.Length > 0;

    /// <summary>Why a revoke did nothing, or empty.</summary>
    internal string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>The token just minted, until it is dismissed; it hands its value only to a hold or a copy.</summary>
    internal MintedToken? Minted => _minted;

    internal bool HasMinted => _minted is not null;

    internal bool HasNoRows => _rows.Count == 0;

    /// <summary>Whether New token is offered: not while the form or a minted token is showing.</summary>
    internal bool CanOpenForm => !_isFormOpen && _minted is null;

    internal RelayCommand OpenFormCommand { get; }

    internal RelayCommand CancelFormCommand { get; }

    /// <summary>Mints a token from the form and keeps it until Done.</summary>
    internal RelayCommand CreateCommand { get; }

    /// <summary>Copies the minted token; the clipboard clears itself as a copied password does.</summary>
    internal AsyncRelayCommand CopyMintedCommand { get; }

    /// <summary>Forgets the minted token.</summary>
    internal RelayCommand DoneMintedCommand { get; }

    /// <summary>Deletes the token at once; the row's confirmation is what runs it.</summary>
    internal RelayCommand<ScopedTokenRow> RevokeCommand { get; }

    /// <summary>Asks the row to confirm its revoke.</summary>
    internal RelayCommand<ScopedTokenRow> AskRevokeCommand { get; }

    internal RelayCommand CancelRevokeCommand { get; }

    /// <summary>Every token, by name.</summary>
    internal IReadOnlyList<ScopedTokenRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(HasNoRows));
            }
        }
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

    public void Dispose()
    {
        SetMinted(null);
        Rows = [];
    }

    private void CloseForm()
    {
        IsFormOpen = false;
        Name = string.Empty;
        Scope = string.Empty;
    }

    private void CreateFromForm()
    {
        var name = _name.Trim();
        var days = int.Parse(_expiry[..^1], CultureInfo.InvariantCulture);
        var (ok, token, message) = Create(name, _scope, TimeSpan.FromDays(days), allowProd: false);

        if (!ok || token is null)
        {
            FormError = message.Length == 0 ? "No token was made." : char.ToUpperInvariant(message[0]) + message[1..].TrimEnd('.') + ".";
            return;
        }

        CloseForm();
        var prefix = _rows.FirstOrDefault(row => row.Name == name)?.Prefix ?? TokenSecret.Prefix;
        SetMinted(new MintedToken(token, name, $"keypaste keeps only a verifier of {prefix}, so nobody can show you this token later."));
        _toast($"Created {name}");
    }

    /// <remarks>Says nothing about when the clipboard clears: the clipboard's own countdown shows that, and a failed copy.</remarks>
    private async Task CopyMintedAsync()
    {
        if (_minted is { } minted && minted.Reveal() is { } token && _clipboard is not null)
        {
            await _clipboard.CopyAsync(token, minted.Name).ConfigureAwait(true);

            if (_clipboard.Failure is null)
            {
                _toast($"Copied {minted.Name}");
            }
        }
    }

    private void RevokeRow(ScopedTokenRow? row)
    {
        if (row is null)
        {
            return;
        }

        var failure = Revoke(row);
        Message = failure ?? string.Empty;

        if (failure is null)
        {
            _toast($"Revoked {row.Name}");
        }
    }

    private void SetMinted(MintedToken? minted)
    {
        _minted?.Forget();
        _minted = minted;
        Raise(nameof(Minted));
        Raise(nameof(HasMinted));
        CopyMintedCommand.RaiseCanExecuteChanged();
        RaiseCanOpenForm();
    }

    private void RaiseCanOpenForm()
    {
        Raise(nameof(CanOpenForm));
        OpenFormCommand.RaiseCanExecuteChanged();
    }

    private void Confirm(string? id) => Rows = [.. _rows.Select(row => row with { IsConfirming = row.Id == id })];

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

/// <summary>A token just minted: drawn only while held and copied on request, then forgotten.</summary>
/// <param name="token">The whole token, which no property returns.</param>
/// <param name="name">What it is called.</param>
/// <param name="note">What to do with it, naming its prefix and never its secret.</param>
internal sealed class MintedToken(string token, string name, string note) : IRevealSource
{
    private string? _token = token;

    internal string Name { get; } = name;

    internal string Note { get; } = note;

    public int MaskedLength => _token?.Length ?? 0;

    public string? Reveal() => _token;

    public void Conceal()
    {
    }

    internal void Forget() => _token = null;
}
