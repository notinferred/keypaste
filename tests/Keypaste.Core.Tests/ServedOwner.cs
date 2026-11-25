using System.Security.Cryptography;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault's owner as the tests stand one up: a real <see cref="SessionAuthority"/> answering on a real pipe of its
/// own until it is disposed. The test composes the authority, so each one chooses where its credentials come from.
/// </summary>
internal sealed class ServedOwner : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly IDisposable _endpoint;
    private readonly Task _running;
    private bool _disposed;

    private ServedOwner(string pipeName, ApproverListener listener, SessionAuthority authority)
    {
        PipeName = pipeName;
        Authority = authority;
        _endpoint = listener;
        _running = listener.RunAsync(_stop.Token);
    }

    private ServedOwner(string pipeName, SessionServer server)
    {
        PipeName = pipeName;
        Authority = server.Authority;
        _endpoint = server;
        _running = server.RunAsync(_stop.Token);
    }

    internal string PipeName { get; }

    internal SessionAuthority Authority { get; }

    /// <summary>Completes once the pipe has stopped accepting.</summary>
    internal Task Stopped => _running;

    /// <summary>A pipe name no other test is using.</summary>
    internal static string NewPipeName(string prefix = "keypaste-tests-") =>
        prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    /// <summary>Serves <paramref name="authority"/> on its own pipe.</summary>
    /// <param name="authority">What the owner answers with.</param>
    /// <param name="pipeName">The pipe, or a new one.</param>
    /// <param name="answering">What answers the pipe when a test puts something in front of the authority; the authority itself otherwise.</param>
    internal static ServedOwner Serve(SessionAuthority authority, string? pipeName = null, IApproverHandler? answering = null)
    {
        var name = pipeName ?? NewPipeName();
        return new ServedOwner(name, new ApproverListener(name, answering ?? authority), authority);
    }

    /// <summary>Serves an owner composed as the desktop and <c>keypaste approve</c> compose one, through <see cref="SessionServer.Listen"/>.</summary>
    internal static ServedOwner Listen(SessionServerOptions options, string? pipeName = null)
    {
        var name = pipeName ?? NewPipeName();
        return new ServedOwner(name, SessionServer.Listen(name, options));
    }

    /// <summary>What decides requests from the vault itself: both the credential source and the lister read <paramref name="vault"/>.</summary>
    internal static ApproverHandler VaultHandler(Func<Vault?> vault, ApprovalGate gate, IAgentRules rules, TimeProvider? clock = null) =>
        new(new VaultCredentialSource(vault, clock), new VaultEntryNameLister(vault), gate, rules);

    /// <summary>Stops accepting without waiting, as an owner whose lock also ends its listening does.</summary>
    internal void StopListening() => _stop.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync();

        try
        {
            await _running;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }

        _endpoint.Dispose();
        _stop.Dispose();
    }
}
