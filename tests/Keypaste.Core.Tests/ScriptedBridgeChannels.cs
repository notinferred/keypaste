using System.IO.Pipelines;
using Keypaste.Core.Clients;

namespace Keypaste.Core.Tests;

/// <summary>In-memory channels between a connection check and a scripted bridge that run every continuation inline (F.41).</summary>
/// <remarks>Start the bridge's loop by calling it, not with <c>Task.Run</c>.</remarks>
internal sealed class ScriptedBridgeChannels : IDisposable
{
    private static readonly PipeOptions _inline = new(
        readerScheduler: PipeScheduler.Inline,
        writerScheduler: PipeScheduler.Inline,
        useSynchronizationContext: false);

    internal ScriptedBridgeChannels()
    {
        var requests = new Pipe(_inline);
        var replies = new Pipe(_inline);
        ToBridge = requests.Writer.AsStream();
        BridgeReads = requests.Reader.AsStream();
        BridgeWrites = replies.Writer.AsStream();
        FromBridge = replies.Reader.AsStream();
    }

    internal Stream ToBridge { get; }

    internal Stream BridgeReads { get; }

    internal Stream BridgeWrites { get; }

    internal Stream FromBridge { get; }

    internal McpConnectionCheck Check(TimeProvider? clock = null) => new(ToBridge, FromBridge, clock);

    public void Dispose()
    {
        ToBridge.Dispose();
        BridgeReads.Dispose();
        BridgeWrites.Dispose();
        FromBridge.Dispose();
    }
}
