using System.Buffers.Text;
using System.Globalization;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Sharing;

/// <summary>What to share, for how long and how many times.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Field">One of <see cref="ShareService.Fields"/>.</param>
/// <param name="Ttl">How long the link opens for, <see cref="ShareService.MinimumTtl"/> to <see cref="ShareService.MaximumTtl"/>.</param>
/// <param name="Views">How many times it opens, 1 to <see cref="ShareService.MaximumViews"/>.</param>
/// <param name="Passphrase">A passphrase the recipient must also type, or null.</param>
/// <param name="Recipient">The person's own label for who it goes to, never sent to the server.</param>
public sealed record ShareRequest(EntryName Entry, string Field, TimeSpan Ttl, int Views, SecretBuffer? Passphrase, string? Recipient);

/// <summary>What sharing or revoking did.</summary>
/// <param name="Ok">Whether it did what was asked.</param>
/// <param name="Link">The link, carrying the key, on a successful share.</param>
/// <param name="Info">The share's record.</param>
/// <param name="Failure">The server's failure, when there was one.</param>
/// <param name="Message">What went wrong, or empty.</param>
public sealed record ShareOutcome(bool Ok, string? Link, ShareInfo? Info, ShareFailure Failure, string Message)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"ShareOutcome {{ Ok = {Ok}, Link = {(Link is null ? "null" : "(redacted)")}, Info = {Info}, Failure = {Failure}, Message = {Message} }}";
}

/// <summary>
/// Shares one entry's fields as an encrypted, expiring, view-limited link, and revokes it (D-0354).
/// </summary>
/// <remarks>
/// Fail closed at every step after the upload: a record that cannot be saved, or an audit line that
/// cannot be written, withdraws the link from the server before anything is returned. The key lives
/// only in memory and in the returned link.
/// </remarks>
/// <param name="client">The share server.</param>
/// <param name="clock">What time it is.</param>
/// <param name="openAudit">Opens the audit log, or returns null when it cannot; the service disposes it.</param>
public sealed class ShareService(ShareClient client, TimeProvider clock, Func<AuditLog?> openAudit)
{
    /// <summary>The shortest a link may last.</summary>
    public static readonly TimeSpan MinimumTtl = TimeSpan.FromMinutes(5);

    /// <summary>The longest a link may last.</summary>
    public static readonly TimeSpan MaximumTtl = TimeSpan.FromDays(7);

    /// <summary>The most views a link may carry.</summary>
    public const int MaximumViews = 10;

    /// <summary>The shortest passphrase accepted.</summary>
    public const int MinimumPassphraseLength = 8;

    /// <summary>The longest recipient label kept.</summary>
    public const int MaximumRecipientLength = 128;

    /// <summary>What <see cref="ShareRequest.Field"/> may name.</summary>
    public static readonly IReadOnlyList<string> Fields = ["password", "username", "url", "notes", "login"];

    private const string Tool = "share";

    private readonly ShareClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Func<AuditLog?> _openAudit = openAudit ?? throw new ArgumentNullException(nameof(openAudit));

    /// <summary>Uploads a sealed share, records it and audits it, then returns its link.</summary>
    public async Task<ShareOutcome> CreateAsync(Vault vault, ShareRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(request);

        if (Validate(request) is { } invalid)
        {
            return Refused(invalid);
        }

        var saved = vault.ReadSaved(out var entries);
        if (saved != SavedRead.Current)
        {
            return Refused(saved switch
            {
                SavedRead.ChangedOnDisk => "the vault changed on disk since it was opened; open it again first",
                SavedRead.Unsaved => "the vault holds changes that are not saved yet; save them first",
                _ => "the vault file could not be read",
            });
        }

        if (ReservedGroups.IsReserved(request.Entry.GroupPath))
        {
            return Refused("keypaste's own records cannot be shared");
        }

        var what = EntryNameSanitizer.SanitizePath(Path(request.Entry)).Text;
        var matches = entries!
            .Where(entry => string.Equals(entry.GroupPath, request.Entry.GroupPath, StringComparison.Ordinal)
                            && string.Equals(entry.Title, request.Entry.Title, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (matches.Count != 1)
        {
            return Refused(matches.Count == 0 ? $"no entry '{what}'" : $"more than one entry is called '{what}'");
        }

        var fields = Select(matches[0], request.Field);
        if (fields.Count == 0)
        {
            return Refused($"'{what}' has no {request.Field} to share");
        }

        var created = _clock.GetUtcNow();
        var payload = new SharePayload(matches[0].Title, fields, created);
        var passphrase = request.Passphrase is { Length: > 0 } given ? given.Value : ReadOnlySpan<char>.Empty;
        if (!ShareCrypto.AcceptsPassphrase(passphrase))
        {
            return Refused(ShareCrypto.PassphraseRule);
        }

        if (!ShareCrypto.Fits(payload))
        {
            return new ShareOutcome(false, null, null, ShareFailure.TooLarge, $"what would be shared is over the {ShareCrypto.MaximumPlaintextBytes}-byte limit");
        }

        var sealedShare = ShareCrypto.Seal(payload, passphrase);
        var revokeToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var revokeHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(revokeToken)));

        var (accepted, failure, message) = await _client
            .CreateAsync(sealedShare.Envelope, request.Views, (int)request.Ttl.TotalSeconds, revokeHash, ct)
            .ConfigureAwait(false);
        if (accepted is null)
        {
            return new ShareOutcome(false, null, null, failure, message);
        }

        var recipient = request.Recipient is { Length: > 0 } label
            ? EntryNameSanitizer.SanitizeProse(label, MaximumRecipientLength).Text
            : null;
        var info = new ShareInfo(
            accepted.Id,
            what,
            request.Field,
            recipient,
            accepted.ExpiresAt - request.Ttl,
            accepted.ExpiresAt,
            request.Views,
            sealedShare.HasPassphrase,
            _client.Endpoint.GetLeftPart(UriPartial.Authority));

        var store = new ShareStore(vault);
        store.Add(info, revokeToken);
        if (!TrySave(vault, out var unsaved))
        {
            store.Remove(info.Id);
            await _client.RevokeAsync(info.Id, revokeToken, CancellationToken.None).ConfigureAwait(false);
            return Refused($"the vault could not be saved ({unsaved}), so the link was withdrawn");
        }

        if (!TryAudit(AuditMethod.ShareCreated, what, request.Field, CreatedReason(info)))
        {
            await _client.RevokeAsync(info.Id, revokeToken, CancellationToken.None).ConfigureAwait(false);
            store.Remove(info.Id);
            TrySave(vault, out _);
            return Refused("the audit log could not be written, so the link was withdrawn");
        }

        return new ShareOutcome(true, ShareLink.Format(_client.Endpoint, info.Id, sealedShare.Key), info, ShareFailure.None, string.Empty);
    }

    /// <summary>Revokes the one share whose id starts with <paramref name="idPrefix"/> and forgets it.</summary>
    /// <remarks>A share the server no longer has is forgotten too. One the server could not be asked about is kept.</remarks>
    public async Task<ShareOutcome> RevokeAsync(Vault vault, string idPrefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(idPrefix);

        var store = new ShareStore(vault);
        var info = store.Find(idPrefix, out var ambiguous);
        if (info is null)
        {
            return Refused(ambiguous ? $"more than one share starts with '{idPrefix}'" : $"no share starts with '{idPrefix}'");
        }

        if (!store.TryGetRevokeToken(info.Id, out var token) || !ShareEndpoint.TryParseRecorded(info.Endpoint, out var endpoint))
        {
            return Refused($"the record of share {info.Id} is incomplete, so it cannot be revoked from here");
        }

        var failure = await _client.WithEndpoint(endpoint).RevokeAsync(info.Id, token, ct).ConfigureAwait(false);
        if (failure != ShareFailure.None)
        {
            return new ShareOutcome(false, null, info, failure, ShareClient.Describe(failure));
        }

        store.Remove(info.Id);
        if (!TrySave(vault, out var unsaved))
        {
            return new ShareOutcome(false, null, info, ShareFailure.None, $"the link was revoked, but the vault could not be saved ({unsaved})");
        }

        if (!TryAudit(AuditMethod.ShareRevoked, info.What, info.Field, $"share {info.Id} revoked"))
        {
            return new ShareOutcome(false, null, info, ShareFailure.None, "the link was revoked, but the audit log could not be written");
        }

        return new ShareOutcome(true, null, info, ShareFailure.None, string.Empty);
    }

    /// <summary>Every recorded share with its status: <c>N views left</c>, <c>gone</c>, <c>expired</c> or <c>unknown</c>.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="online">Whether to ask the server; offline every live share is <c>unknown</c>.</param>
    /// <param name="ct">Cancels the requests.</param>
    public async Task<IReadOnlyList<(ShareInfo Info, string Status, int? ViewsLeft)>> ListAsync(Vault vault, bool online, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vault);

        var now = _clock.GetUtcNow();
        List<(ShareInfo, string, int?)> rows = [];

        foreach (var info in new ShareStore(vault).List())
        {
            if (info.Expires <= now)
            {
                rows.Add((info, "expired", null));
                continue;
            }

            if (!online || !ShareEndpoint.TryParseRecorded(info.Endpoint, out var endpoint))
            {
                rows.Add((info, "unknown", null));
                continue;
            }

            var (status, failure) = await _client.WithEndpoint(endpoint).StatusAsync(info.Id, ct).ConfigureAwait(false);
            rows.Add(failure != ShareFailure.None || status is null ? (info, "unknown", null)
                : !status.Exists ? (info, "gone", null)
                : (info, status.ViewsLeft == 1 ? "1 view left" : $"{status.ViewsLeft} views left", status.ViewsLeft));
        }

        return rows;
    }

    private static string? Validate(ShareRequest request)
    {
        if (!Fields.Contains(request.Field, StringComparer.Ordinal))
        {
            return $"'{request.Field}' is not a field that can be shared";
        }

        if (request.Views is < 1 or > MaximumViews)
        {
            return $"a link opens 1 to {MaximumViews} times";
        }

        if (request.Ttl < MinimumTtl || request.Ttl > MaximumTtl || request.Ttl.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            return "a link lasts from 5 minutes to 7 days";
        }

        if (request.Passphrase is { Length: > 0 and < MinimumPassphraseLength })
        {
            return $"a passphrase has at least {MinimumPassphraseLength} characters";
        }

        return request.Recipient is { Length: > MaximumRecipientLength }
            ? $"a recipient label has at most {MaximumRecipientLength} characters"
            : null;
    }

    private static List<ShareField> Select(VaultEntry entry, string field)
    {
        (string Name, string Value)[] chosen = field switch
        {
            "login" => [("username", entry.Username), ("password", entry.Password), ("url", entry.Url)],
            "username" => [("username", entry.Username)],
            "url" => [("url", entry.Url)],
            "notes" => [("notes", entry.Notes)],
            _ => [("password", entry.Password)],
        };

        return [.. chosen.Where(pair => pair.Value.Length > 0).Select(pair => new ShareField(pair.Name, pair.Value))];
    }

    private static string CreatedReason(ShareInfo info)
    {
        var views = info.Views == 1 ? "1 view" : $"{info.Views} views";
        var to = info.Recipient ?? "anyone with the link";
        var passphrase = info.Passphrase ? ", passphrase" : string.Empty;

        return string.Create(CultureInfo.InvariantCulture, $"share {info.Id}: {views}, expires {ShareStore.Iso(info.Expires)}, to {to}{passphrase}");
    }

    private static string Path(EntryName name) => name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;

    private static bool TrySave(Vault vault, out string error)
    {
        try
        {
            vault.Save();
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryAudit(AuditMethod method, string what, string field, string reason)
    {
        using var audit = _openAudit();

        return audit is not null && audit.TryAppend(
            new AuditRecord
            {
                Tool = Tool,
                Client = new AuditClient("keypaste share", CoreInfo.Version, null),
                Args = new AuditArgs { Entry = what, Field = field },
                Decision = AuditDecision.Granted,
                Method = method,
                Reason = reason,
            },
            out _);
    }

    private static ShareOutcome Refused(string message) => new(false, null, null, ShareFailure.Refused, message);
}
