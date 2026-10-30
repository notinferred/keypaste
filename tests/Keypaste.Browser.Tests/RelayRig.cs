using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.FakeBrowser;
using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// A relay in this process with the fake browser on its standard input and output, over anonymous pipes as a browser starts
/// it, and a fake owner of its vault on a real pipe.
/// </summary>
internal sealed class RelayRig : IAsyncDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-relay-tests-").FullName;
    private readonly AnonymousPipeServerStream _toRelay = new(PipeDirection.Out);
    private readonly AnonymousPipeClientStream _relayInput;
    private readonly AnonymousPipeServerStream _fromRelay = new(PipeDirection.In);
    private readonly AnonymousPipeClientStream _relayOutput;
    private readonly CancellationTokenSource _stop = new();

    internal RelayRig(bool owner = true)
    {
        _relayInput = new AnonymousPipeClientStream(PipeDirection.In, _toRelay.ClientSafePipeHandle);
        _relayOutput = new AnonymousPipeClientStream(PipeDirection.Out, _fromRelay.ClientSafePipeHandle);

        var vaultPath = Path.Combine(_home, "vault.kdbx");
        var pipeName = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        Options = new RelayOptions(BrowserIdentity.ChromeWebStore, vaultPath, pipeName, LockedAppEndpoint.NameFor(VaultIdentity.Of(_home, vaultPath)));
        Owner = new FakeOwner(pipeName);

        if (owner)
        {
            Owner.Listen();
        }

        Browser = new ExtensionClient(_toRelay, _fromRelay);
        Running = Relay.RunAsync(Options, _relayInput, _relayOutput, Error, _stop.Token);
    }

    internal RelayOptions Options { get; }

    internal FakeOwner Owner { get; }

    internal ExtensionClient Browser { get; }

    /// <summary>The relay, until it returns its exit code.</summary>
    internal Task<int> Running { get; }

    /// <summary>What the relay wrote to standard error.</summary>
    internal StringWriter Error { get; } = new();

    /// <summary>The browser's end of the relay's standard input, for bytes a test writes as they are.</summary>
    internal Stream ToRelay => _toRelay;

    /// <summary>The browser's end of the relay's standard output.</summary>
    internal Stream FromRelay => _fromRelay;

    /// <summary>A relay that has exchanged keys, the browser holding the host's key.</summary>
    internal static async Task<RelayRig> KeyedAsync(bool owner = true)
    {
        var rig = new RelayRig(owner);
        var reply = await rig.Browser.ChangePublicKeysAsync(TestContext.Current.CancellationToken);
        Assert.Equal("true", (string?)reply["success"]);
        return rig;
    }

    /// <summary>Closes the relay's standard input, as a browser does to end its host.</summary>
    internal void CloseInput() => _toRelay.Dispose();

    /// <summary>Closes the relay's end of its standard output, once it has stopped, so the browser reads to the end of what it wrote.</summary>
    internal void CloseOutput() => _relayOutput.Dispose();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _toRelay.Dispose();
        _relayInput.Dispose();
        _fromRelay.Dispose();
        _relayOutput.Dispose();

        try
        {
            await Running.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException)
        {
            // Pulling the pipes away is how a test that left the relay running ends it.
        }

        Browser.Dispose();
        await Owner.DisposeAsync();
        _stop.Dispose();
        Error.Dispose();
        Directory.Delete(_home, recursive: true);
    }
}

/// <summary>
/// The vault's owner as the relay meets it: attaches a browser to its session, or says it is locked, and answers a browser's
/// four requests as a test sets them, keeping each request it read. Any other request ends the connection unanswered.
/// </summary>
internal sealed class FakeOwner(string pipeName) : IApproverHandler, IAsyncDisposable
{
    private ApproverListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _running;

    /// <summary>The session a browser attaches to, or null to answer every attach as locked.</summary>
    internal string? Session { get; set; } = "session-one";

    /// <summary>Every request this owner read, in order.</summary>
    internal ConcurrentQueue<object> Heard { get; } = new();

    /// <summary>The requests other than attaching.</summary>
    internal IReadOnlyList<object> Asked => [.. Heard.Where(request => request is not AttachRequest)];

    internal BrowserReply PairReply { get; set; } = new(BrowserOutcome.Done, string.Empty) { Pairing = "keypaste on this computer" };

    internal BrowserReply CheckReply { get; set; } = new(BrowserOutcome.Done, string.Empty);

    internal BrowserFillReply FillReply { get; set; } = new(BrowserOutcome.Done, string.Empty);

    internal BrowserReply LockReply { get; set; } = new(BrowserOutcome.Done, string.Empty);

    /// <summary>Starts answering on the pipe, as an owner does once it unlocks.</summary>
    internal void Listen()
    {
        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(pipeName, this);
        _running = _listener.RunAsync(_stop.Token);
    }

    /// <summary>Stops answering and ends every connection, as an owner's lock does (D-0392).</summary>
    internal async Task StopAsync()
    {
        if (_stop is null || _running is null || _listener is null)
        {
            return;
        }

        await _stop.CancelAsync();

        try
        {
            await _running;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }

        _listener.Dispose();
        _stop.Dispose();
        (_listener, _stop, _running) = (null, null, null);
    }

    public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken)
    {
        Heard.Enqueue(request);

        return ValueTask.FromResult(Session is { } session
            ? AttachReply.To(session)
            : AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked"));
    }

    public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
        Unasked<NamesReply>(request);

    public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken) =>
        Unasked<CredentialReply>(request);

    public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
        Unasked<EnvReply>(request);

    public ValueTask<LockReply> LockAsync(LockRequest request, string connectionId, CancellationToken cancellationToken) =>
        Unasked<LockReply>(request);

    public ValueTask<BrowserReply> PairBrowserAsync(BrowserPairRequest request, string connectionId, CancellationToken cancellationToken) =>
        Answer(request, PairReply);

    public ValueTask<BrowserReply> CheckBrowserAsync(BrowserCheckRequest request, string connectionId, CancellationToken cancellationToken) =>
        Answer(request, CheckReply);

    public ValueTask<BrowserFillReply> FillBrowserAsync(BrowserFillRequest request, string connectionId, CancellationToken cancellationToken) =>
        Answer(request, FillReply);

    public ValueTask<BrowserReply> LockBrowserAsync(BrowserLockRequest request, string connectionId, CancellationToken cancellationToken) =>
        Answer(request, LockReply);

    public void Disconnected(string connectionId)
    {
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private ValueTask<T> Answer<T>(object request, T reply)
    {
        Heard.Enqueue(request);
        return ValueTask.FromResult(reply);
    }

    // Throwing ends the connection unanswered.
    private ValueTask<T> Unasked<T>(object request)
    {
        Heard.Enqueue(request);
        throw new InvalidOperationException($"the relay sent the owner {request.GetType().Name}");
    }
}
