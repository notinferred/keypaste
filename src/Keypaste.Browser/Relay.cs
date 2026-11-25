using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Infrastructure;
using static Keypaste.Browser.ExtensionProtocol;

namespace Keypaste.Browser;

/// <summary>
/// keypaste as KeePassXC-Browser's native messaging host: it ends the extension's NaCl box session, answers itself whatever
/// keypaste refuses, and sends the vault's owner only typed requests over its pipe, as a browser's attachment. It never opens
/// the vault and writes no audit line.
/// </summary>
/// <remarks>
/// <para>
/// One message is answered at a time, and the browser's next is read only once the one before has its reply, so no reply
/// overtakes another and a signal never lands inside one.
/// </para>
/// <para>
/// Between messages the relay keeps its link to the owner with a read pending, so a lock, which ends the link, is told to the
/// extension as <c>database-locked</c> at once. The first request after the relay told the extension the vault was not open that
/// finds it unlocked is preceded by <c>database-unlocked</c>.
/// </para>
/// <para>
/// A released password passes through here in plaintext between the pipe and the box (THREATS.md T-48). The relay zeroes the
/// bytes it sealed; the strings it decoded stay until the collector takes them, as the bridge's do (T-18).
/// </para>
/// </remarks>
internal sealed class Relay : IAsyncDisposable
{
    private const int _longestEcho = 64;

    // Only the origin of the page a new login is for reaches the item, so a longer address is refused rather than carried.
    private const int _longestNewLoginUrl = 2048;

    private static readonly TimeSpan _connectTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds((2 * ApprovalLimits.MaximumWindowSeconds) + 15);
    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    private readonly RelayOptions _options;
    private readonly Stream _output;
    private readonly NonceLedger _nonces = new();
    private BoxKeyPair? _keys;
    private byte[]? _extensionKey;
    private BrowserLink? _link;
    private RelayVault? _reached;
    private Pairing? _paired;
    private string? _databaseHash;
    private bool _toldLocked;

    private Relay(RelayOptions options, Stream output)
    {
        _options = options;
        _output = output;
    }

    /// <summary>The vault the link's owner was attached through, which each request names; empty before any was.</summary>
    private string VaultPath => _reached?.Path ?? string.Empty;

    /// <summary>Answers the browser until it closes standard input or <paramref name="stop"/> is cancelled.</summary>
    /// <param name="options">Who started the relay, and where the owner is.</param>
    /// <param name="input">The browser's messages, standard input.</param>
    /// <param name="output">Where the replies go, standard output, which carries nothing else.</param>
    /// <param name="error">Where a reason to stop is written, standard error.</param>
    /// <param name="stop">Cancelled when the browser sends SIGTERM.</param>
    /// <returns>0 once the browser is done, or 1 for a message too large to read.</returns>
    internal static async Task<int> RunAsync(RelayOptions options, Stream input, Stream output, TextWriter error, CancellationToken stop)
    {
        await using var relay = new Relay(options, output);

        return await relay.ServeAsync(input, error, stop).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _keys?.Dispose();
        await DropLinkAsync().ConfigureAwait(false);
    }

    private async Task<int> ServeAsync(Stream input, TextWriter error, CancellationToken stop)
    {
        var stopped = Task.Delay(Timeout.Infinite, stop);
        var reading = NativeMessages.ReadAsync(input, stop);

        while (true)
        {
            var first = await Task.WhenAny(reading, stopped, _link?.Ended ?? stopped).ConfigureAwait(false);

            if (first == stopped)
            {
                return 0;
            }

            try
            {
                if (first != reading)
                {
                    await OwnerEndedAsync(stop).ConfigureAwait(false);
                    continue;
                }

                if (await reading.ConfigureAwait(false) is not { } message)
                {
                    return 0;
                }

                await SendAsync(await ReplyAsync(message, stop).ConfigureAwait(false), stop).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                await error.WriteLineAsync($"keypaste: {ex.Message}").ConfigureAwait(false);
                return 1;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The browser closed the pipes, or is stopping the relay.
                return 0;
            }

            reading = NativeMessages.ReadAsync(input, stop);
        }
    }

    private async Task<byte[]> ReplyAsync(byte[] message, CancellationToken stop)
    {
        if (Strings(message) is not { } outer)
        {
            return Error(string.Empty, ExtensionError.EmptyMessageReceived);
        }

        var action = outer.GetValueOrDefault("action") ?? string.Empty;

        if (action == ChangePublicKeys)
        {
            return ChangeKeys(outer);
        }

        if (Refused.TryGetValue(action, out var refused))
        {
            return Error(action, refused, outer.GetValueOrDefault("requestID"));
        }

        if (action is not (GetDatabaseHash or Associate or TestAssociate or GetLogins or GetLogin or OpenKeypaste or LockDatabase))
        {
            return Error(action, ExtensionError.IncorrectAction);
        }

        if (_keys is null || _extensionKey is null)
        {
            return Error(action, ExtensionError.ClientPublicKeyNotReceived);
        }

        if (!TryOpen(outer, _keys, _extensionKey, out var replyNonce, out var inner))
        {
            return Error(action, ExtensionError.CannotDecryptMessage);
        }

        if (inner.GetValueOrDefault("action") != action)
        {
            return Error(action, ExtensionError.IncorrectAction);
        }

        var knock = outer.GetValueOrDefault("triggerUnlock") == True;

        return action switch
        {
            GetDatabaseHash => await DatabaseHashAsync(replyNonce, knock, stop).ConfigureAwait(false),
            Associate => await AssociateAsync(inner, replyNonce, knock, stop).ConfigureAwait(false),
            TestAssociate => await TestAssociateAsync(inner, replyNonce, knock, stop).ConfigureAwait(false),
            GetLogins => await GetLoginsAsync(inner, replyNonce, knock, stop).ConfigureAwait(false),
            GetLogin => await GetLoginAsync(inner, replyNonce, knock, stop).ConfigureAwait(false),
            OpenKeypaste => await OpenKeypasteAsync(inner, replyNonce, stop).ConfigureAwait(false),
            _ => await LockAsync(replyNonce, stop).ConfigureAwait(false),
        };
    }

    /// <summary>A fresh host key pair at each key exchange, as KeePassXC makes one, which also forgets the pairing last checked.</summary>
    private byte[] ChangeKeys(Dictionary<string, string> outer)
    {
        if (!TryBase64(outer.GetValueOrDefault("publicKey"), NaclBox.KeyLength, out var extensionKey)
            || !TryBase64(outer.GetValueOrDefault("nonce"), NaclBox.NonceLength, out var nonce))
        {
            return Error(ChangePublicKeys, ExtensionError.ClientPublicKeyNotReceived);
        }

        if (!_nonces.TryTake(nonce, out var replyNonce))
        {
            return Error(ChangePublicKeys, ExtensionError.KeyChangeFailed);
        }

        var keys = NaclBox.NewKeyPair();
        _keys?.Dispose();
        _keys = keys;
        _extensionKey = extensionKey;
        _paired = null;

        return Json(writer =>
        {
            writer.WriteString("action", ChangePublicKeys);
            writer.WriteString("version", ReportedVersion);
            writer.WriteString("publicKey", Convert.ToBase64String(keys.PublicKey));
            writer.WriteString("nonce", Convert.ToBase64String(replyNonce));
            writer.WriteString("success", True);
        });
    }

    /// <summary>
    /// Opens a request's box, refusing a nonce the session has used or whose increment it has, and reads its string
    /// members; every other member, the extension's other pairings' keys among them, goes no further.
    /// </summary>
    private bool TryOpen(
        Dictionary<string, string> outer,
        BoxKeyPair keys,
        byte[] extensionKey,
        [NotNullWhen(true)] out byte[]? replyNonce,
        [NotNullWhen(true)] out Dictionary<string, string>? inner)
    {
        replyNonce = null;
        inner = null;

        if (!TryBase64(outer.GetValueOrDefault("nonce"), NaclBox.NonceLength, out var nonce)
            || !TryBase64(outer.GetValueOrDefault("message"), null, out var box)
            || !_nonces.TryTake(nonce, out var next)
            || NaclBox.Open(box, nonce, extensionKey, keys.SecretKey) is not { } opened)
        {
            return false;
        }

        try
        {
            inner = Strings(opened);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(opened);
        }

        replyNonce = next;
        return inner is not null;
    }

    /// <summary>
    /// The hash the owner told this attachment, which the extension files keypaste's pairing under, apart from KeePassXC's; an
    /// owner that pairs no browsers tells none, answered with error 2. No <c>oldHash</c> is ever sent.
    /// </summary>
    private async Task<byte[]> DatabaseHashAsync(byte[] replyNonce, bool knock, CancellationToken stop)
    {
        if (await AttachAsync(knock, stop).ConfigureAwait(false) is null)
        {
            return Error(GetDatabaseHash, ExtensionError.DatabaseNotOpened);
        }

        return _databaseHash is { } hash
            ? Sealed(GetDatabaseHash, replyNonce, writer => writer.WriteString("hash", hash))
            : Error(GetDatabaseHash, ExtensionError.DatabaseHashNotReceived);
    }

    /// <summary>The hash and the pairing an answer to <c>associate</c> or <c>test-associate</c> carries, as KeePassXC's does.</summary>
    private void WritePaired(Utf8JsonWriter writer, string pairing)
    {
        if (_databaseHash is { } hash)
        {
            writer.WriteString("hash", hash);
        }

        writer.WriteString("id", pairing);
    }

    private async Task<byte[]> AssociateAsync(Dictionary<string, string> inner, byte[] replyNonce, bool knock, CancellationToken stop)
    {
        // KeePassXC pairs only a request whose session key is the one the key exchange named.
        if (inner.GetValueOrDefault("idKey") is not { } identificationKey
            || !IsKey(identificationKey)
            || !TryBase64(inner.GetValueOrDefault("key"), NaclBox.KeyLength, out var sessionKey)
            || !sessionKey.AsSpan().SequenceEqual(_extensionKey))
        {
            return Error(Associate, ExtensionError.AssociationFailed);
        }

        if (await AttachAsync(knock, stop).ConfigureAwait(false) is not { } session)
        {
            return Error(Associate, ExtensionError.DatabaseNotOpened);
        }

        var request = new BrowserPairRequest(identificationKey) { Vault = VaultPath, Session = session };
        var reply = await ExchangeAsync((link, token) => link.PairAsync(request, token), stop).ConfigureAwait(false);

        if (reply is { Outcome: BrowserOutcome.Done, Pairing: { } pairing } && IsPairing(pairing))
        {
            _paired = new Pairing(pairing, identificationKey, session);
            return Sealed(Associate, replyNonce, writer => WritePaired(writer, pairing));
        }

        return Error(Associate, reply is null or { Outcome: BrowserOutcome.Locked }
            ? ExtensionError.DatabaseNotOpened
            : ExtensionError.ActionCancelledOrDenied);
    }

    private async Task<byte[]> TestAssociateAsync(Dictionary<string, string> inner, byte[] replyNonce, bool knock, CancellationToken stop)
    {
        // Whatever this check finds, no pairing stays proven but the one it proves.
        _paired = null;

        if (inner.GetValueOrDefault("id") is not { } pairing || !IsPairing(pairing) || inner.GetValueOrDefault("key") is not { } key || !IsKey(key))
        {
            return Error(TestAssociate, ExtensionError.AssociationFailed);
        }

        if (await AttachAsync(knock, stop).ConfigureAwait(false) is not { } session)
        {
            return Error(TestAssociate, ExtensionError.DatabaseNotOpened);
        }

        var request = new BrowserCheckRequest(pairing, key) { Vault = VaultPath, Session = session };
        var reply = await ExchangeAsync((link, token) => link.CheckAsync(request, token), stop).ConfigureAwait(false);

        if (reply is { Outcome: BrowserOutcome.Done })
        {
            _paired = new Pairing(pairing, key, session);
            return Sealed(TestAssociate, replyNonce, writer => WritePaired(writer, pairing));
        }

        return Error(TestAssociate, reply is null or { Outcome: BrowserOutcome.Locked }
            ? ExtensionError.DatabaseNotOpened
            : ExtensionError.AssociationFailed);
    }

    /// <summary>
    /// Asks for the names of a page's logins under the pairing the last check proved in this unlock, and that pairing alone,
    /// whatever the request's <c>id</c> and <c>keys</c> say. Each entry carries an empty password, as no password leaves keypaste
    /// before the person picks one; a list matching nothing is an answer, not KeePassXC's error 15, so the extension can still
    /// name the vaults that are locked.
    /// </summary>
    private async Task<byte[]> GetLoginsAsync(Dictionary<string, string> inner, byte[] replyNonce, bool knock, CancellationToken stop)
    {
        var (page, refusal) = await PageAsync(GetLogins, inner, knock, stop).ConfigureAwait(false);

        if (page is null)
        {
            return refusal!;
        }

        var request = new BrowserListRequest(page.Paired.Id, page.Paired.Key, page.Url) { SubmitUrl = page.SubmitUrl, Vault = VaultPath, Session = page.Session };

        if (ApproverProtocol.Encode(request).Length > MessageFramer.MaximumPayloadBytes)
        {
            return Error(GetLogins, ExtensionError.NoLoginsFound);
        }

        var reply = await ExchangeAsync((link, token) => link.ListAsync(request, token), stop).ConfigureAwait(false);

        switch (reply)
        {
            case { Outcome: BrowserOutcome.Done }:
                return Sealed(GetLogins, replyNonce, writer => WriteNames(writer, reply, page.Paired.Id));
            case { Outcome: BrowserOutcome.NotPaired }:
                _paired = null;
                return Error(GetLogins, ExtensionError.AssociationFailed);
            case null or { Outcome: BrowserOutcome.Locked }:
                return Error(GetLogins, ExtensionError.DatabaseNotOpened);
            default:
                return Error(GetLogins, ExtensionError.ActionCancelledOrDenied);
        }
    }

    /// <summary>Asks for the one login the person picked, which keypaste fills at once.</summary>
    private async Task<byte[]> GetLoginAsync(Dictionary<string, string> inner, byte[] replyNonce, bool knock, CancellationToken stop)
    {
        if (inner.GetValueOrDefault("uuid") is not { } uuid || !IsPairing(uuid))
        {
            return Error(GetLogin, ExtensionError.NoLoginsFound);
        }

        var (page, refusal) = await PageAsync(GetLogin, inner, knock, stop).ConfigureAwait(false);

        if (page is null)
        {
            return refusal!;
        }

        var request = new BrowserReleaseRequest(page.Paired.Id, page.Paired.Key, page.Url, uuid) { SubmitUrl = page.SubmitUrl, Vault = VaultPath, Session = page.Session };

        if (ApproverProtocol.Encode(request).Length > MessageFramer.MaximumPayloadBytes)
        {
            return Error(GetLogin, ExtensionError.NoLoginsFound);
        }

        var reply = await ExchangeAsync((link, token) => link.ReleaseAsync(request, token), stop).ConfigureAwait(false);

        switch (reply)
        {
            case { Outcome: BrowserOutcome.Done, Logins: [var login] }:
                return Sealed(GetLogin, replyNonce, writer => WriteLogins(writer, [login], page.Paired.Id));
            case { Outcome: BrowserOutcome.Done }:
                return Error(GetLogin, ExtensionError.NoLoginsFound);
            case { Outcome: BrowserOutcome.NotPaired }:
                _paired = null;
                return Error(GetLogin, ExtensionError.AssociationFailed);
            case null or { Outcome: BrowserOutcome.Locked }:
                return Error(GetLogin, ExtensionError.DatabaseNotOpened);
            default:
                return Error(GetLogin, ExtensionError.ActionCancelledOrDenied);
        }
    }

    /// <summary>Asks the owner to bring keypaste forward for what the person pressed in a page's list, under the pairing the last check proved.</summary>
    private async Task<byte[]> OpenKeypasteAsync(Dictionary<string, string> inner, byte[] replyNonce, CancellationToken stop)
    {
        BrowserShow? what = inner.GetValueOrDefault("what") switch
        {
            "open" => BrowserShow.Open,
            "new-login" => BrowserShow.NewLogin,
            "unlock" => BrowserShow.Unlock,
            _ => null,
        };

        var vaultName = inner.GetValueOrDefault("vault") ?? string.Empty;
        var url = inner.GetValueOrDefault("url") ?? string.Empty;

        if (what is not { } shown || vaultName.Length > AttachClient.MaximumLength || url.Length > _longestNewLoginUrl)
        {
            return Error(OpenKeypaste, ExtensionError.IncorrectAction);
        }

        if (_paired is not { } paired)
        {
            return Error(OpenKeypaste, ExtensionError.AssociationFailed);
        }

        if (await AttachAsync(knock: true, stop).ConfigureAwait(false) is not { } session || session != paired.Session)
        {
            return Error(OpenKeypaste, ExtensionError.DatabaseNotOpened);
        }

        var request = new BrowserShowRequest(shown) { VaultName = vaultName, Url = url, Vault = VaultPath, Session = session };
        var reply = await ExchangeAsync((link, token) => link.ShowAsync(request, token), stop).ConfigureAwait(false);

        return reply is { Outcome: BrowserOutcome.Done } ? Sealed(OpenKeypaste, replyNonce) : Error(OpenKeypaste, ExtensionError.ActionCancelledOrDenied);
    }

    /// <summary>
    /// What a list or a pick names of the page: the pairing proven in this unlock, the session and the URLs; or the error to send.
    /// </summary>
    private async Task<(PageRequest? Page, byte[]? Refusal)> PageAsync(string action, Dictionary<string, string> inner, bool knock, CancellationToken stop)
    {
        if (_paired is not { } paired)
        {
            return (null, Error(action, ExtensionError.AssociationFailed));
        }

        // HTTP authentication is refused before anything is sent.
        if (inner.GetValueOrDefault("httpAuth") == True)
        {
            return (null, Error(action, ExtensionError.NoLoginsFound));
        }

        if (inner.GetValueOrDefault("url") is not { Length: > 0 } url)
        {
            return (null, Error(action, ExtensionError.NoUrlProvided));
        }

        if (await AttachAsync(knock, stop).ConfigureAwait(false) is not { } session)
        {
            return (null, Error(action, ExtensionError.DatabaseNotOpened));
        }

        if (session != paired.Session)
        {
            _paired = null;
            return (null, Error(action, ExtensionError.AssociationFailed));
        }

        return (new PageRequest(paired, session, url, inner.GetValueOrDefault("submitUrl")), null);
    }

    /// <summary>Locks as <c>keypaste lock</c> does, asking nobody; the link's end then tells the extension it is locked.</summary>
    private async Task<byte[]> LockAsync(byte[] replyNonce, CancellationToken stop)
    {
        if (await AttachAsync(knock: false, stop).ConfigureAwait(false) is not { } session)
        {
            return Error(LockDatabase, ExtensionError.DatabaseNotOpened);
        }

        var request = new BrowserLockRequest { Vault = VaultPath, Session = session };
        var reply = await ExchangeAsync((link, token) => link.LockAsync(request, token), stop).ConfigureAwait(false);

        return reply switch
        {
            { Outcome: BrowserOutcome.Done } => Sealed(LockDatabase, replyNonce),
            null or { Outcome: BrowserOutcome.Locked } => Error(LockDatabase, ExtensionError.DatabaseNotOpened),
            _ => Error(LockDatabase, ExtensionError.ActionCancelledOrDenied),
        };
    }

    /// <summary>
    /// Attaches as a browser's attachment to a session holding one of the vaults, connecting when need be, and returns it; or,
    /// with nothing holding any of them unlocked, knocks on the locked app's pipe when the extension asked to unlock and
    /// returns null.
    /// </summary>
    private async Task<string?> AttachAsync(bool knock, CancellationToken stop)
    {
        if (_link is { IsSpent: true })
        {
            await OwnerEndedAsync(stop).ConfigureAwait(false);
        }

        // The owner the link reached last is asked first, then each other vault's, so one locked vault leaves the relay
        // on another that is unlocked.
        foreach (var vault in _options.Vaults.OrderBy(vault => vault == _reached ? 0 : 1))
        {
            if (await AttachThroughAsync(vault, stop).ConfigureAwait(false) is { } session)
            {
                return session;
            }
        }

        _toldLocked = true;

        if (knock)
        {
            foreach (var vault in _options.Vaults)
            {
                if (await LockedAppEndpoint.TellAsync(vault.LockedAppName, stop).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        return null;
    }

    /// <summary>Attaches to the session holding <paramref name="vault"/>, connecting when need be, or returns null when its owner does not.</summary>
    private async Task<string?> AttachThroughAsync(RelayVault vault, CancellationToken stop)
    {
        if (_link is not null && vault != _reached)
        {
            await DropLinkAsync().ConfigureAwait(false);
        }

        var request = new AttachRequest(vault.Path) { Browser = _options.Identity.Caller };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            _link ??= await BrowserLink.TryConnectAsync(vault.PipeName, _connectTimeout, stop).ConfigureAwait(false);

            if (_link is null)
            {
                return null;
            }

            _reached = vault;

            if (await _link.AttachAsync(request, stop).ConfigureAwait(false) is { Attached: true, Session: { } session } attached)
            {
                _databaseHash = attached.DatabaseHash;

                if (_toldLocked)
                {
                    _toldLocked = false;
                    await SendAsync(Signal(DatabaseUnlocked), stop).ConfigureAwait(false);
                }

                return session;
            }

            // An owner that answered no is locked or holds another vault; only a link that had died is tried again.
            var answered = !_link.IsSpent;
            await DropLinkAsync().ConfigureAwait(false);

            if (answered)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>One exchange with the owner, bounded by the longest a person could take to answer; a link that fails it is dropped.</summary>
    private async Task<T?> ExchangeAsync<T>(Func<BrowserLink, CancellationToken, ValueTask<T?>> exchange, CancellationToken stop)
        where T : class
    {
        if (_link is not { } link)
        {
            return null;
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stop);
        bounded.CancelAfter(_answerBound);

        var reply = await exchange(link, bounded.Token).ConfigureAwait(false);

        if (reply is null)
        {
            await DropLinkAsync().ConfigureAwait(false);
            _toldLocked = true;
        }

        return reply;
    }

    /// <summary>The owner ended the link, as its lock does: the extension is told the vault is locked.</summary>
    private async Task OwnerEndedAsync(CancellationToken stop)
    {
        await DropLinkAsync().ConfigureAwait(false);
        _toldLocked = true;
        await SendAsync(Signal(DatabaseLocked), stop).ConfigureAwait(false);
    }

    private async Task DropLinkAsync()
    {
        var link = _link;
        _link = null;

        if (link is not null)
        {
            await link.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task SendAsync(byte[] message, CancellationToken stop) => NativeMessages.WriteAsync(_output, message, stop);

    /// <summary>
    /// A reply sealed to the extension under <paramref name="replyNonce"/>, the request's incremented, carrying KeePassXC's
    /// version, success and nonce and <paramref name="fields"/>; or error 7 when it cannot be sealed or would be over the most
    /// a browser accepts.
    /// </summary>
    private byte[] Sealed(string action, byte[] replyNonce, Action<Utf8JsonWriter>? fields = null)
    {
        var nonce = Convert.ToBase64String(replyNonce);
        var plain = Json(writer =>
        {
            writer.WriteString("version", ReportedVersion);
            writer.WriteString("success", True);
            writer.WriteString("nonce", nonce);
            fields?.Invoke(writer);
        });

        try
        {
            var box = NaclBox.Seal(plain, replyNonce, _extensionKey!, _keys!.SecretKey);
            var reply = Json(writer =>
            {
                writer.WriteString("action", action);
                writer.WriteString("message", Convert.ToBase64String(box));
                writer.WriteString("nonce", nonce);
            });

            return reply.Length <= NativeMessages.MaximumBytes ? reply : Error(action, ExtensionError.CannotEncryptMessage);
        }
        catch (CryptographicException)
        {
            return Error(action, ExtensionError.CannotEncryptMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// A page's list as KeePassXC lists logins, each with its username, title and UUID, an empty password and, while several vaults
    /// are open, its vault; then the open vaults that are locked, the page's site and why every fill there is asked, if it is.
    /// </summary>
    private static void WriteNames(Utf8JsonWriter writer, BrowserListReply reply, string pairing)
    {
        writer.WriteNumber("count", reply.Names.Count);
        writer.WriteStartArray("entries");

        foreach (var name in reply.Names)
        {
            writer.WriteStartObject();
            writer.WriteString("login", name.Username);
            writer.WriteString("name", name.Title);
            writer.WriteString("password", string.Empty);
            writer.WriteString("uuid", name.Uuid);
            writer.WriteString("vault", name.Vault);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("lockedVaults");

        foreach (var vault in reply.LockedVaults)
        {
            writer.WriteStringValue(vault);
        }

        writer.WriteEndArray();
        writer.WriteString("site", reply.Site);
        writer.WriteString("warning", reply.Warning);
        writer.WriteString("id", pairing);
    }

    /// <summary>The logins as KeePassXC lists them, each with only its username, title, password and UUID, never its group, a TOTP code or another field.</summary>
    private static void WriteLogins(Utf8JsonWriter writer, IReadOnlyList<BrowserLogin> logins, string pairing)
    {
        writer.WriteNumber("count", logins.Count);
        writer.WriteStartArray("entries");

        foreach (var login in logins)
        {
            writer.WriteStartObject();
            writer.WriteString("login", login.Login);
            writer.WriteString("name", login.Name);
            writer.WriteString("password", login.Password);
            writer.WriteString("uuid", login.Uuid);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("id", pairing);
    }

    /// <summary>KeePassXC's error reply: in plaintext and with no nonce, which is how the extension tells it from an answer.</summary>
    private static byte[] Error(string action, ExtensionError error, string? requestId = null) =>
        Json(writer =>
        {
            writer.WriteString("action", Echoed(action));
            writer.WriteString("errorCode", ((int)error).ToString(CultureInfo.InvariantCulture));
            writer.WriteString("error", Words(error));

            if (requestId is not null)
            {
                writer.WriteString("requestID", Echoed(requestId));
            }
        });

    /// <summary>A signal the relay sends unasked, in plaintext as KeePassXC sends it.</summary>
    private static byte[] Signal(string action) => Json(writer => writer.WriteString("action", action));

    /// <summary>What the extension sent, echoed only while it is short enough to be one of its own names.</summary>
    private static string Echoed(string sent) => sent.Length <= _longestEcho ? sent : string.Empty;

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();

        try
        {
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                write(writer);
                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            buffer.Clear();
        }
    }

    /// <summary>The string members of a JSON object, or null when the bytes are not one; a member named twice refuses it.</summary>
    private static Dictionary<string, string>? Strings(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, _strict);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.String)
                {
                    strings[member.Name] = member.Value.GetString()!;
                }
            }

            return strings;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryBase64(string? text, int? length, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;

        if (text is null)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return false;
        }

        if (length is { } exact && bytes.Length != exact)
        {
            bytes = null;
            return false;
        }

        return true;
    }

    /// <summary>A public key in base64, as the extension writes one.</summary>
    private static bool IsKey(string? text) => text?.Length <= AttachClient.MaximumLength && TryBase64(text, NaclBox.KeyLength, out _);

    /// <summary>A pairing's id the owner's pipe carries.</summary>
    private static bool IsPairing(string text) => text.Length is > 0 and <= AttachClient.MaximumLength;

    /// <summary>The pairing a check proved, in the unlock it was proved in.</summary>
    private sealed record Pairing(string Id, string Key, string Session);

    /// <summary>What a list or a pick names of the page, under the pairing proven in this unlock.</summary>
    private sealed record PageRequest(Pairing Paired, string Session, string Url, string? SubmitUrl);
}
