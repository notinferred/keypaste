using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Browser;

namespace Keypaste.FakeBrowser;

/// <summary>
/// KeePassXC-Browser's end of its native messaging protocol, as its <c>client.js</c> speaks it: a key exchange, then each
/// request sealed to the host under a fresh nonce, and each answer opened only under that nonce incremented.
/// </summary>
/// <remarks>
/// It seals and opens with the libsodium box <c>Keypaste.Browser</c> calls, so it shares the relay's NaCl; that the box is the
/// one TweetNaCl.js seals, as the extension's is, is held by 8.1b's known answers (D-0613).
/// </remarks>
public sealed class ExtensionClient : IDisposable
{
    private readonly Stream _toHost;
    private readonly Stream _fromHost;
    private readonly BoxKeyPair _keys = NaclBox.NewKeyPair();
    private readonly List<string> _signals = [];
    private byte[]? _hostKey;

    /// <summary>Speaks to a host over its standard input and output.</summary>
    /// <param name="toHost">The host's standard input.</param>
    /// <param name="fromHost">The host's standard output.</param>
    public ExtensionClient(Stream toHost, Stream fromHost)
    {
        _toHost = toHost;
        _fromHost = fromHost;
    }

    /// <summary>The extension's session public key in base64, as <c>change-public-keys</c> sends it.</summary>
    public string PublicKey => Convert.ToBase64String(_keys.PublicKey);

    /// <summary>The host's session public key from the last key exchange it answered, or null.</summary>
    public byte[]? HostKey => _hostKey;

    /// <summary>Each <c>database-locked</c> and <c>database-unlocked</c> the host sent, in order.</summary>
    public IReadOnlyList<string> Signals => _signals;

    /// <summary>A random nonce in base64, as the extension makes one for each request.</summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NaclBox.NonceLength));

    /// <summary>The nonce an answer to a request under <paramref name="nonce"/> must come under, as the extension computes it.</summary>
    public static string Incremented(string nonce) => Convert.ToBase64String(NonceLedger.Incremented(Convert.FromBase64String(nonce)));

    /// <summary>Sends one message, framed as native messaging frames it.</summary>
    public Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return NativeMessages.WriteAsync(_toHost, Encoding.UTF8.GetBytes(message.ToJsonString()), cancellationToken);
    }

    /// <summary>The host's next message that is not a signal, keeping each signal read on the way; null once it closed its output.</summary>
    public async Task<JsonObject?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (await NextAsync(cancellationToken).ConfigureAwait(false) is { } message)
        {
            if (!IsSignal(message))
            {
                return message;
            }

            _signals.Add((string)message["action"]!);
        }

        return null;
    }

    /// <summary>The host's next message, which must be a signal.</summary>
    public async Task<string> ReceiveSignalAsync(CancellationToken cancellationToken)
    {
        var message = await NextAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException("the host closed its output");

        if (!IsSignal(message))
        {
            throw new InvalidDataException($"the host sent {message.ToJsonString()} where a signal was expected");
        }

        var signal = (string)message["action"]!;
        _signals.Add(signal);
        return signal;
    }

    /// <summary>Exchanges session keys as the extension does on connecting, keeping the host's key when its answer is well formed.</summary>
    public Task<JsonObject> ChangePublicKeysAsync(CancellationToken cancellationToken) => ChangePublicKeysAsync(NewNonce(), cancellationToken);

    /// <summary>Exchanges session keys under <paramref name="nonce"/>.</summary>
    public async Task<JsonObject> ChangePublicKeysAsync(string nonce, CancellationToken cancellationToken)
    {
        await SendAsync(
            new JsonObject { ["action"] = "change-public-keys", ["publicKey"] = PublicKey, ["nonce"] = nonce, ["clientID"] = NewNonce() },
            cancellationToken).ConfigureAwait(false);

        var reply = await ReceiveAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException("the host closed its output");

        if ((string?)reply["success"] == "true" && (string?)reply["nonce"] == Incremented(nonce) && (string?)reply["publicKey"] is { } key)
        {
            _hostKey = Convert.FromBase64String(key);
        }

        return reply;
    }

    /// <summary>Sends one sealed request under a fresh nonce, its message <paramref name="fields"/> with the action named, and reads its answer.</summary>
    public Task<Answer> RequestAsync(string action, JsonObject? fields, CancellationToken cancellationToken) =>
        RequestAsync(action, fields, NewNonce(), triggerUnlock: false, cancellationToken);

    /// <summary>Sends one sealed request and reads its answer.</summary>
    /// <param name="action">The action.</param>
    /// <param name="fields">The rest of the sealed message, or null.</param>
    /// <param name="nonce">The request's nonce.</param>
    /// <param name="triggerUnlock">Whether to ask the host to show its unlock, as the extension does when the person asks.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The answer.</returns>
    public async Task<Answer> RequestAsync(string action, JsonObject? fields, string nonce, bool triggerUnlock, CancellationToken cancellationToken)
    {
        var message = new JsonObject { ["action"] = action };

        if (fields is not null)
        {
            foreach (var (name, value) in fields)
            {
                message[name] = value?.DeepClone();
            }
        }

        var request = new JsonObject { ["action"] = action, ["message"] = Seal(message, nonce), ["nonce"] = nonce, ["clientID"] = NewNonce() };

        if (triggerUnlock)
        {
            request["triggerUnlock"] = "true";
        }

        await SendAsync(request, cancellationToken).ConfigureAwait(false);

        var reply = await ReceiveAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException("the host closed its output");

        return new Answer(reply, Open(reply, Incremented(nonce)));
    }

    /// <summary>A message sealed to the host under <paramref name="nonce"/>, in base64, as the extension seals one.</summary>
    public string Seal(JsonObject message, string nonce)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Seal(Encoding.UTF8.GetBytes(message.ToJsonString()), nonce);
    }

    /// <summary>Any bytes sealed to the host under <paramref name="nonce"/>, in base64.</summary>
    public string Seal(byte[] plaintext, string nonce)
    {
        var host = _hostKey ?? throw new InvalidOperationException("no key exchange has been answered");

        return Convert.ToBase64String(NaclBox.Seal(plaintext, Convert.FromBase64String(nonce), host, _keys.SecretKey));
    }

    /// <inheritdoc/>
    public void Dispose() => _keys.Dispose();

    private static bool IsSignal(JsonObject message) =>
        message.Count == 1 && (string?)message["action"] is "database-locked" or "database-unlocked";

    private async Task<JsonObject?> NextAsync(CancellationToken cancellationToken) =>
        await NativeMessages.ReadAsync(_fromHost, cancellationToken).ConfigureAwait(false) is { } bytes
            ? JsonNode.Parse(bytes)!.AsObject()
            : null;

    /// <summary>The answer's message, opened, when it was sealed under <paramref name="nonce"/> and says so inside, as the extension checks.</summary>
    private JsonObject? Open(JsonObject reply, string nonce)
    {
        if ((string?)reply["message"] is not { } sealedMessage || (string?)reply["nonce"] != nonce || _hostKey is null)
        {
            return null;
        }

        var opened = NaclBox.Open(Convert.FromBase64String(sealedMessage), Convert.FromBase64String(nonce), _hostKey, _keys.SecretKey);

        return opened is not null && JsonNode.Parse(opened)!.AsObject() is var message && (string?)message["nonce"] == nonce ? message : null;
    }
}

/// <summary>A host's answer to a sealed request.</summary>
/// <param name="Reply">What the host sent.</param>
/// <param name="Message">The message it sealed, opened, when it sealed one under the request's nonce incremented; otherwise null.</param>
public sealed record Answer(JsonObject Reply, JsonObject? Message)
{
    /// <summary>The error code of a plaintext error reply, or null.</summary>
    public string? ErrorCode => (string?)Reply["errorCode"];
}
