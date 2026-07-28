using System.Security.Cryptography;
using Keypaste.Core.Clipboard;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The rule for clearing a clipboard keypaste wrote to (DECISIONS.md D-0011).
/// </summary>
/// <remarks>
/// <para>
/// This rule had no test of its own while it lived inside the CLI's blocking strategy — it was
/// reached only through <c>keypaste get</c>, whose tests are about exit codes and output. It is now
/// on the path of two front ends, and CORE.md law 4.5 makes a test of it mandatory rather than
/// tidy.
/// </para>
/// <para>
/// The case worth staring at is <see cref="A_read_back_that_failed_clears_anyway"/>. It is the
/// fail-closed branch (law 3.7), it is the one a reader assumes is the other way round, and
/// inverting it is a one-character edit that no other test in this repository would notice.
/// </para>
/// </remarks>
public sealed class ClipboardClearTests
{
    [Fact]
    public void An_unchanged_clipboard_is_cleared()
    {
        var hash = Hash("sk_live_the_secret");

        Assert.True(ClipboardClear.Should(readSucceeded: true, hash, hash));
    }

    [Fact]
    public void A_clipboard_changed_since_the_copy_is_left_alone()
    {
        Assert.False(ClipboardClear.Should(
            readSucceeded: true,
            Hash("what the user copied afterwards"),
            Hash("sk_live_the_secret")));
    }

    [Fact]
    public void A_read_back_that_failed_clears_anyway()
    {
        // Not knowing whether the clipboard still holds the secret is not a reason to leave it
        // there. The current hash is empty because nothing could be read.
        Assert.True(ClipboardClear.Should(
            readSucceeded: false,
            ReadOnlySpan<byte>.Empty,
            Hash("sk_live_the_secret")));
    }

    /// <summary>
    /// A read that succeeded but returned nothing is a change, not a failure.
    /// </summary>
    /// <remarks>
    /// An empty clipboard is the state after somebody else already cleared it. Treating that as a
    /// failed read — and therefore clearing — would be harmless, but treating it as a match would
    /// not be, and a length-blind comparison would do exactly that.
    /// </remarks>
    [Fact]
    public void An_emptied_clipboard_is_left_alone()
    {
        Assert.False(ClipboardClear.Should(
            readSucceeded: true,
            ReadOnlySpan<byte>.Empty,
            Hash("sk_live_the_secret")));
    }

    [Fact]
    public void The_default_window_is_twenty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), ClipboardClear.DefaultWindow);
    }

    private static byte[] Hash(string text) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
}
