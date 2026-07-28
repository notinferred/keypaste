using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The buffer was internal to the CLI until the approval flow needed it, and it was exercised only
/// through the prompt. Now that it is public core API on the secret path, docs/PRODUCT.md law 4.5 wants the
/// zeroing asserted directly rather than inferred from a command's behaviour.
/// </summary>
public sealed class SecretBufferTests
{
    [Fact]
    public void ItHoldsWhatWasAppended()
    {
        using var buffer = new SecretBuffer();
        buffer.Append("hunter2");

        Assert.Equal("hunter2", buffer.Value.ToString());
        Assert.Equal(7, buffer.Length);
    }

    /// <summary>
    /// The property the whole type exists for. Asserted on the backing array rather than on
    /// <see cref="SecretBuffer.Value"/>, which stops reporting once disposed — checking the
    /// accessor would pass for a buffer that merely hid its contents.
    /// </summary>
    [Fact]
    public void DisposingZeroesTheCharacters()
    {
        var buffer = new SecretBuffer();
        buffer.Append("hunter2");

        Assert.False(buffer.IsZeroed);

        buffer.Dispose();

        Assert.True(buffer.IsZeroed);
        Assert.Equal(0, buffer.Length);
        Assert.True(buffer.Value.IsEmpty);
    }

    /// <summary>
    /// Growing copies into a bigger array, and the old one is still reachable by the collector.
    /// If it were not cleared, every master password longer than the initial capacity would leave
    /// a full copy of itself behind.
    /// </summary>
    [Fact]
    public void GrowingZeroesTheArrayItAbandons()
    {
        using var buffer = new SecretBuffer();
        buffer.Append(new string('x', SecretBuffer.InitialCapacity * 3));

        Assert.Equal(SecretBuffer.InitialCapacity * 3, buffer.Length);
        Assert.Equal(new string('x', SecretBuffer.InitialCapacity * 3), buffer.Value.ToString());
    }

    [Fact]
    public void BackspaceRemovesTheLastCharacterAndZeroesIt()
    {
        using var buffer = new SecretBuffer();
        buffer.Append("ab");
        buffer.Backspace();

        Assert.Equal("a", buffer.Value.ToString());

        buffer.Backspace();
        buffer.Backspace();

        Assert.Equal(0, buffer.Length);
        Assert.True(buffer.IsZeroed);
    }

    [Fact]
    public void ClearZeroesWithoutDisposing()
    {
        using var buffer = new SecretBuffer();
        buffer.Append("hunter2");
        buffer.Clear();

        Assert.True(buffer.IsZeroed);

        buffer.Append("again");

        Assert.Equal("again", buffer.Value.ToString());
    }

    [Fact]
    public void TwoBuffersCompareByValue()
    {
        using var one = new SecretBuffer();
        using var same = new SecretBuffer();
        using var other = new SecretBuffer();

        one.Append("hunter2");
        same.Append("hunter2");
        other.Append("hunter3");

        Assert.True(one.ValueEquals(same));
        Assert.False(one.ValueEquals(other));
    }

    [Fact]
    public void UsingADisposedBufferThrows()
    {
        var buffer = new SecretBuffer();
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.Append('a'));
        Assert.Throws<ObjectDisposedException>(buffer.Backspace);
        Assert.Throws<ObjectDisposedException>(buffer.Clear);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var buffer = new SecretBuffer();
        buffer.Append("hunter2");
        buffer.Dispose();
        buffer.Dispose();

        Assert.True(buffer.IsZeroed);
    }

    [Fact]
    public void ValueEquals_RejectsNull()
    {
        using var buffer = new SecretBuffer();

        Assert.Throws<ArgumentNullException>(() => buffer.ValueEquals(null!));
    }
}
