using Keypaste.Core.Ipc;
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
/// the bound in <see cref="ApproverProtocol"/> is there to keep.
/// </remarks>
public sealed class MessageFramerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static EntryName Astral(int runes) =>
        new("env/dev", string.Concat(Enumerable.Repeat("\U000E0041", runes)));

    /// <summary>Ordinary names, encoding to seventy-six bytes each. See <see cref="ApproverProtocolTests"/>.</summary>
    private static IReadOnlyList<EntryName> Ordinary(int count) =>
        [.. Enumerable.Range(0, count)
            .Select(i => new EntryName("env/dev/services", $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}"))];

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
    /// Nothing <see cref="ApproverProtocol"/> encodes can be too big to send, for any input.
    /// </summary>
    /// <remarks>
    /// The single assertion this whole bound exists for. It is written as "the framer accepts it"
    /// rather than as a length comparison so that the two files cannot drift: if the ceiling moves,
    /// this still tests the real question.
    /// </remarks>
    [Fact]
    public async Task WhatTheProtocolEncodes_IsAlwaysWritable()
    {
        NamesReply[] adversarial =
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

        using var sink = new MemoryStream();
        using var framer = new MessageFramer(sink, ownsStream: false);

        foreach (var reply in adversarial)
        {
            await framer.WriteAsync(ApproverProtocol.Encode(reply), Token);
        }
    }
}
