using System.IO.Pipelines;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The two one-way channels an in-process MCP client and server talk over: one carrying the client's
/// requests, one carrying the server's replies.
/// </summary>
/// <remarks>
/// <para>
/// <b>In memory, and never an operating-system pipe.</b> The transports keep a read outstanding on each
/// channel for as long as the harness is open. An anonymous pipe cannot read asynchronously, so each
/// such read parked a thread-pool worker in a blocking <c>ReadFile</c> — two per open harness, for its
/// whole life — and with a handful of harnesses open across the test classes running in parallel,
/// nothing else in the test host could get a worker (F.9 class 2). A <see cref="Pipe"/> read that has
/// nothing to return holds no thread at all.
/// </para>
/// <para>
/// One place builds them, because <c>Keypaste.PoolStarver</c> compiles this file too: F.9's class-2
/// regression opens exactly the channels the harness opens, and a copy of them would let the two drift.
/// </para>
/// </remarks>
internal sealed class HarnessChannels : IDisposable
{
    private readonly Stream _toServer;
    private readonly Stream _serverReads;
    private readonly Stream _toClient;
    private readonly Stream _clientReads;

    private HarnessChannels()
    {
        var requests = new Pipe();
        var replies = new Pipe();

        _toServer = requests.Writer.AsStream();
        _serverReads = requests.Reader.AsStream();
        _toClient = replies.Writer.AsStream();
        _clientReads = replies.Reader.AsStream();
    }

    /// <summary>What the client writes its requests to.</summary>
    internal Stream ClientWrites => _toServer;

    /// <summary>What the server reads the client's requests from.</summary>
    internal Stream ServerReads => _serverReads;

    /// <summary>What the server writes its replies to.</summary>
    internal Stream ServerWrites => _toClient;

    /// <summary>What the client reads the server's replies from.</summary>
    internal Stream ClientReads => _clientReads;

    internal static HarnessChannels Open() => new();

    public void Dispose()
    {
        _toServer.Dispose();
        _serverReads.Dispose();
        _toClient.Dispose();
        _clientReads.Dispose();
    }
}
