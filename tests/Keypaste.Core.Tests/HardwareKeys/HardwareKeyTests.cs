using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.HardwareKeys;
using Xunit;

namespace Keypaste.Core.Tests.HardwareKeys;

/// <summary>What one hardware key factor sends, what it answers, and when it asks the key at all.</summary>
public sealed class HardwareKeyTests
{
    /// <summary>
    /// KeePassXC's own known answer: TestYkChallengeResponseKey.cpp challenges a slot programmed with
    /// <see cref="SoftwareYubiKey.KeePassXcTestSecret"/> with "UnitTest" and expects this SHA-256 of
    /// the response, so the padding and the slot's variable-length hashing reproduce a real key's.
    /// </summary>
    [Fact]
    public void The_padded_challenge_answers_KeePassXCs_own_test_vector()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret);
        using var key = new HardwareKey(device, 2);

        var response = key.Respond(Encoding.ASCII.GetBytes("UnitTest"));

        Assert.Equal(20, response.Length);
        Assert.Equal(
            "2f7802c7112c301303526e7737b54d546c905076dca6e9538edf761a2264cd70",
            Convert.ToHexString(SHA256.HashData(response)).ToLowerInvariant());
    }

    [Fact]
    public void A_challenge_is_padded_to_64_bytes_with_its_pad_length()
    {
        var padded = HardwareKey.Pad(new byte[32]);

        Assert.Equal(64, padded.Length);
        Assert.All(padded[32..], b => Assert.Equal(32, b));
        Assert.All(padded[..32], b => Assert.Equal(0, b));
    }

    /// <summary>
    /// A variable-length slot drops every trailing byte equal to the pad, the challenge's own
    /// included, so a seed ending in 0x20 is hashed shorter. keypaste sends what KeePassXC sends and
    /// leaves the hashing to the key.
    /// </summary>
    [Fact]
    public void A_seed_ending_in_the_pad_byte_is_sent_unchanged()
    {
        var seed = Enumerable.Repeat((byte)0x41, 31).Append((byte)0x20).ToArray();
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret);
        using var key = new HardwareKey(device, 1);

        var response = key.Respond(seed);

        Assert.Equal([.. seed, .. Enumerable.Repeat((byte)0x20, 32)], Assert.Single(device.Challenges));
#pragma warning disable CA5350 // The key's algorithm, computed here to state what it hashes.
        Assert.Equal(HMACSHA1.HashData(SoftwareYubiKey.KeePassXcTestSecret, seed.AsSpan(0, 31)), response);
#pragma warning restore CA5350
    }

    [Fact]
    public void The_same_challenge_is_answered_from_memory_and_a_new_one_asks_the_key()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret);
        using var key = new HardwareKey(device, 2);

        var first = key.Respond([1, 2, 3]);
        var again = key.Respond([1, 2, 3]);
        _ = key.Respond([4, 5, 6]);

        Assert.Equal(first, again);
        Assert.Equal(2, device.Challenges.Count);
    }

    [Fact]
    public void A_slot_that_is_not_programmed_gives_no_response()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret) { Slots = [1] };
        using var key = new HardwareKey(device, 2);

        var refused = Assert.Throws<HardwareKeyException>(() => key.Respond([1]));

        Assert.Equal(HardwareKeyFailure.SlotNotConfigured, refused.Failure);
    }

    [Fact]
    public void Touch_is_signalled_while_the_key_waits_and_cleared_when_it_answers()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret) { RequiresTouch = true };
        using var key = new HardwareKey(device, 2);
        var seen = new List<bool>();
        key.WaitingForTouch += (_, waiting) => seen.Add(waiting);

        _ = key.Respond([7]);

        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task Cancelling_a_wait_for_touch_fails_the_ask()
    {
        var device = new SoftwareYubiKey(SoftwareYubiKey.KeePassXcTestSecret) { NeverTouched = true };
        using var key = new HardwareKey(device, 2);
        using var waiting = new SemaphoreSlim(0);
        key.WaitingForTouch += (_, touch) =>
        {
            if (touch)
            {
                waiting.Release();
            }
        };

        var asking = Task.Run(() => key.Respond([9]), TestContext.Current.CancellationToken);
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        key.CancelWait();

        var refused = await Assert.ThrowsAsync<HardwareKeyException>(() => asking);
        Assert.Equal(HardwareKeyFailure.Cancelled, refused.Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Only_slots_one_and_two_exist(int slot) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HardwareKey(new SoftwareYubiKey([1]), slot));
}
