namespace Keypaste.Cli.Prompting;

/// <summary>
/// A growable character buffer for a secret, which zeroes what it abandons.
/// </summary>
/// <remarks>
/// <para>
/// Exists because <see cref="Keypaste.Core.Vault"/> takes a <see cref="ReadOnlySpan{T}"/> of
/// <see cref="char"/> and zeroes its UTF-8 copy in a <c>finally</c> (DECISIONS.md D-0007). If the
/// CLI read the master password into a <see cref="string"/>, that promise would be worthless one
/// layer up: strings are immutable and cannot be cleared.
/// </para>
/// <para>
/// <b>What this does not protect against, stated plainly so the documentation does not
/// overclaim:</b> the garbage collector may relocate this array and leave an unreachable copy
/// behind; the value can reach swap, a hibernation file, or a core dump; a debugger or any
/// process running as the same user can read it; and it necessarily becomes a
/// <see cref="string"/> later anyway — <c>VaultEntry.Password</c> is one such place. This
/// narrows the window and reduces the number of copies. It is not a security boundary, and
/// SECURITY.md says so.
/// </para>
/// </remarks>
internal sealed class SecretBuffer : IDisposable
{
    internal const int InitialCapacity = 64;

    private char[] _buffer = new char[InitialCapacity];
    private int _length;
    private bool _disposed;

    /// <summary>The characters written so far.</summary>
    internal ReadOnlySpan<char> Value => _disposed ? default : _buffer.AsSpan(0, _length);

    /// <summary>Number of characters written.</summary>
    internal int Length => _length;

    /// <summary>
    /// Whether every character of the backing array is zero. A test hook: it inspects storage
    /// that <see cref="Value"/> deliberately stops exposing once disposed, so the zeroing can be
    /// asserted rather than merely assumed from the fact that Dispose was called.
    /// </summary>
    internal bool IsZeroed
    {
        get
        {
            foreach (var c in _buffer)
            {
                if (c != '\0')
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Appends one character, growing and zeroing the old array if needed.</summary>
    internal void Append(char value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_length == _buffer.Length)
        {
            var grown = new char[_buffer.Length * 2];
            _buffer.AsSpan(0, _length).CopyTo(grown);
            Array.Clear(_buffer);
            _buffer = grown;
        }

        _buffer[_length++] = value;
    }

    /// <summary>Removes the last character, if any.</summary>
    internal void Backspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_length > 0)
        {
            _buffer[--_length] = '\0';
        }
    }

    /// <summary>Discards everything written so far.</summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Array.Clear(_buffer);
        _length = 0;
    }

    /// <summary>Appends every character of <paramref name="text"/>.</summary>
    internal void Append(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            Append(c);
        }
    }

    /// <summary>Whether two buffers hold the same characters.</summary>
    /// <remarks>
    /// Ordinal and length-sensitive. Not constant-time, and deliberately so: both operands are
    /// values the same user just typed at the same prompt, so there is no attacker to leak a
    /// timing signal to.
    /// </remarks>
    internal bool ValueEquals(SecretBuffer other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Value.SequenceEqual(other.Value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Array.Clear(_buffer);
        _length = 0;
        _disposed = true;
    }
}
