using System.Runtime.InteropServices;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The ceiling on one frame, and the rule that nothing keypaste encodes may exceed it.
/// </summary>
/// <remarks>
/// The limit is a denial-of-service bound and the framer is right to refuse rather than truncate: a
/// truncated frame is a message that says something other than what was sent. But a refusal reaches
/// <see cref="ApproverListener"/>'s outermost <c>catch</c>, which ends the connection and revokes
/// its grants — so a producer that can hand the framer an over-size payload has a way to drop a
/// connection the user already approved on. That is what
/// <see cref="WhatTheProtocolEncodes_IsAlwaysWritable"/> exists to prevent, and it is the invariant
/// the bound in <see cref="ApproverProtocol"/> is there to keep. A call may name its own bound, which
/// only an env set's reply does (D-0454); every other call keeps one frame.
/// </remarks>
public sealed class MessageFramerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ListedEntry Astral(int runes) =>
        new(new EntryName("env/dev", string.Concat(Enumerable.Repeat("\U000E0041", runes))), [], []);

    private static CredentialReply Released(string value) => new()
    {
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this request",
        Entry = "env/dev/STRIPE_KEY",
        TtlSeconds = 300,
        Value = value,
    };

    private static EnvReply Set(string value) =>
        new(EnvResolved.Released("billing", [new EnvVariable("CERTIFICATE", value)]), string.Empty);

    /// <summary>One frame of <paramref name="payloadBytes"/> bytes and its delimiter, ready to be read.</summary>
    private static MemoryStream Framed(int payloadBytes)
    {
        var bytes = new byte[payloadBytes + 1];
        bytes.AsSpan(0, payloadBytes).Fill((byte)'a');
        bytes[^1] = (byte)'\n';
        return new MemoryStream(bytes);
    }

    /// <summary>Ordinary names with no fields or tags, encoding to ninety-eight bytes each. See <see cref="ApproverProtocolTests"/>.</summary>
    private static IReadOnlyList<ListedEntry> Ordinary(int count) =>
        [.. Enumerable.Range(0, count)
            .Select(i => new ListedEntry(new EntryName("env/dev/services", $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}"), [], []))];

    [Fact]
    public async Task AFrameAtTheLimit_IsSent()
    {
        using var sink = new MemoryStream();
        using var framer = new MessageFramer(sink, ownsStream: false);

        await framer.WriteAsync(new byte[MessageFramer.MaximumPayloadBytes], Token);

        Assert.Equal(MessageFramer.MaximumFrameBytes, sink.Length);
    }

    /// <summary>
    /// One byte more is refused. Pinned deliberately: the throw is the intended behaviour, and the
    /// budget every producer sizes itself against is only meaningful if this is where it bites.
    /// </summary>
    [Fact]
    public async Task AFrameOneByteOver_IsRefused()
    {
        using var sink = new MemoryStream();
        using var framer = new MessageFramer(sink, ownsStream: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await framer.WriteAsync(new byte[MessageFramer.MaximumPayloadBytes + 1], Token));

        Assert.Equal(0, sink.Length);
    }

    /// <summary>
    /// A frame up to the bound its read names arrives, and one byte more ends the read, whatever the
    /// chunks it arrives in: 5000 is no multiple of the framer's reads.
    /// </summary>
    [Theory]
    [InlineData(5000)]
    [InlineData(MessageFramer.MaximumFrameBytes)]
    [InlineData(ApproverProtocol.MaximumEnvReplyFrameBytes)]
    public async Task AFrameUpToItsBound_IsRead_AndOneByteMoreEndsTheRead(int bound)
    {
        using var fits = Framed(bound - 1);
        using var framer = new MessageFramer(fits, ownsStream: false);
        Assert.Equal(bound - 1, (await framer.ReadAsync(bound, Token))?.Length);

        using var over = Framed(bound);
        using var reader = new MessageFramer(over, ownsStream: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(bound, Token));
    }

    /// <summary>A read or a write that names no bound keeps one frame, so only the env set's reply is ever read past it.</summary>
    [Fact]
    public async Task ACallNamingNoBound_KeepsOneFrame()
    {
        using var fits = Framed(MessageFramer.MaximumPayloadBytes);
        using var framer = new MessageFramer(fits, ownsStream: false);
        Assert.Equal(MessageFramer.MaximumPayloadBytes, (await framer.ReadAsync(Token))?.Length);

        using var over = Framed(MessageFramer.MaximumFrameBytes);
        using var reader = new MessageFramer(over, ownsStream: false);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(Token));

        using var sink = new MemoryStream();
        using var writer = new MessageFramer(sink, ownsStream: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.WriteAsync(new byte[MessageFramer.MaximumFrameBytes], Token));
        await writer.WriteAsync(new byte[MessageFramer.MaximumFrameBytes], ApproverProtocol.MaximumEnvReplyFrameBytes, Token);

        Assert.Equal(MessageFramer.MaximumFrameBytes + 1, sink.Length);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(ApproverProtocol.MaximumEnvReplyFrameBytes)]
    public async Task AWriteOverItsBound_IsRefusedUnwritten(int bound)
    {
        using var sink = new MemoryStream();
        using var framer = new MessageFramer(sink, ownsStream: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await framer.WriteAsync(new byte[bound], bound, Token));
        Assert.Equal(0, sink.Length);

        await framer.WriteAsync(new byte[bound - 1], bound, Token);
        Assert.Equal(bound, sink.Length);
    }

    /// <summary>A large frame and the small ones after it each arrive whole, the next searched from its own start.</summary>
    [Fact]
    public async Task FramesBackToBack_EachArriveWhole()
    {
        using var stream = new MemoryStream();
        using (var writer = new MessageFramer(stream, ownsStream: false))
        {
            await writer.WriteAsync(new byte[100_000], ApproverProtocol.MaximumEnvReplyFrameBytes, Token);
            await writer.WriteAsync("b"u8.ToArray(), Token);
            await writer.WriteAsync("cc"u8.ToArray(), Token);
        }

        stream.Position = 0;
        using var reader = new MessageFramer(stream, ownsStream: false);

        Assert.Equal(100_000, (await reader.ReadAsync(ApproverProtocol.MaximumEnvReplyFrameBytes, Token))?.Length);
        Assert.Equal("b"u8.ToArray(), await reader.ReadAsync(Token));
        Assert.Equal("cc"u8.ToArray(), await reader.ReadAsync(Token));
        Assert.Null(await reader.ReadAsync(Token));
    }

    /// <summary>
    /// A frame leaves no copy of itself in the buffer the framer wrote it from, at an env set's size
    /// as at a credential's, because the pool clears only an array it keeps.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(ApproverProtocol.MaximumEnvReplyPayloadBytes)]
    public async Task AWrittenFrame_LeavesNoCopyInTheBufferItWasWrittenFrom(int payloadBytes)
    {
        var payload = new byte[payloadBytes];
        payload.AsSpan().Fill((byte)'s');
        using var sink = new Capturing();
        using var framer = new MessageFramer(sink, ownsStream: false);

        await framer.WriteAsync(payload, ApproverProtocol.MaximumEnvReplyFrameBytes, Token);

        Assert.Equal(payloadBytes + 1, sink.Length);
        Assert.Equal(payloadBytes + 1, sink.Written.Count);
        Assert.True(sink.Written.AsSpan().IndexOfAnyExcept((byte)0) < 0, "the framer's buffer still holds the frame");
    }

    /// <summary>
    /// Nothing <see cref="ApproverProtocol"/> encodes can be too big to send, for any input, on
    /// any reply path, under the bound the listener writes that reply with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single assertion this whole bound exists for. It is written as "the framer accepts it"
    /// rather than as a length comparison so that the two files cannot drift: if the ceiling moves,
    /// this still tests the real question.
    /// </para>
    /// <para>
    /// Both kinds, because they failed the same way and are fixed differently: a listing keeps the
    /// names that fit, and a credential cannot be trimmed at all, so an over-size one comes back as
    /// a refusal. Neither may reach the guard below. An env set is refused whole the same way, past
    /// its own bound.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WhatTheProtocolEncodes_IsAlwaysWritable()
    {
        NamesReply[] listings =
        [
            // The reproduced case: a thousand ordinary names, which encode past sixty-four kibibytes.
            new(true, Ordinary(1000), string.Empty, true),

            // Escaping, not length. Utf8JsonWriter emits \uXXXX per UTF-16 code unit, so one astral
            // rune costs twelve bytes for what is four in UTF-8 — the budget goes at a few dozen
            // entries rather than a thousand.
            new(true, [.. Enumerable.Repeat(Astral(128), 200)], string.Empty, true),

            // One name no frame could ever hold.
            new(true, [Astral(100_000)], string.Empty, true),

            new(true, [], string.Empty, true),
            new(false, [], "no vault is unlocked", true),
        ];

        // A KDBX note is a releasable field with no length limit, so this is not a hostile peer —
        // it is somebody who pasted a certificate into an entry and then approved a request for it.
        CredentialReply[] releases =
        [
            Released(new string('n', 100_000)),
            Released(string.Concat(Enumerable.Repeat("\U000E0041", 100_000))),
            Released(string.Concat(Enumerable.Repeat("\"<&> ", 40_000))),
            Released("sk_live_short"),

            // A refusal whose own explanation is over-size: the operator's glob error, uncapped.
            new()
            {
                Decision = AuditDecision.Denied,
                Method = AuditMethod.Failed,
                Reason = new string('e', 100_000),
                Entry = "env/dev/STRIPE_KEY",
            },
        ];

        EnvReply[] sets =
        [
            Set(new string('n', ApproverProtocol.MaximumEnvReplyFrameBytes)),
            Set(new string('+', (ApproverProtocol.MaximumEnvReplyFrameBytes / 6) + 1)),
            Set(new string('n', 1024 * 1024)),
            Set("sk_live_short"),
            new(EnvResolved.Refused("billing", EnvOutcome.Unusable, [new EnvProblem("A", new string('e', ApproverProtocol.MaximumEnvReplyFrameBytes))]), string.Empty),
        ];

        using var sink = new MemoryStream();
        using var framer = new MessageFramer(sink, ownsStream: false);

        foreach (var reply in listings)
        {
            await framer.WriteAsync(ApproverProtocol.Encode(reply), Token);
        }

        foreach (var reply in releases)
        {
            await framer.WriteAsync(ApproverProtocol.Encode(reply), Token);
        }

        foreach (var reply in sets)
        {
            await framer.WriteAsync(ApproverProtocol.Encode(reply), ApproverProtocol.MaximumEnvReplyFrameBytes, Token);
        }
    }

    /// <summary>A stream that keeps the array segment it was last written from.</summary>
    private sealed class Capturing : MemoryStream
    {
        internal ArraySegment<byte> Written { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.True(MemoryMarshal.TryGetArray(buffer, out var segment));
            Written = segment;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
