namespace Keypaste.Core;

/// <summary>
/// A growable character buffer for a secret, which zeroes what it abandons.
/// </summary>
/// <remarks>
/// <para>
/// Exists because <see cref="Vault"/> takes a <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>
/// and zeroes its UTF-8 copy in a <c>finally</c> (DECISIONS.md D-0007). If a caller read the master
/// password into a <see cref="string"/>, that promise would be worthless one layer up: strings are
/// immutable and cannot be cleared.
/// </para>
/// <para>
/// It lives in the core rather than in the CLI because more than one front end needs it now: the
/// CLI reads a master password with it, and the approval flow's grant cache holds a released field
/// value in one until the TTL expires (CORE.md law 4.3).
/// </para>
/// <para>
/// <b>What this does not protect against, stated plainly so the documentation does not
/// overclaim:</b> the garbage collector may relocate this array and leave an unreachable copy
/// behind; the value can reach swap, a hibernation file, or a core dump; a debugger or any
/// process running as the same user can read it; and it necessarily becomes a
/// <see cref="string"/> somewhere anyway — <see cref="VaultEntry.Password"/> is one such place.
/// This narrows the window and reduces the number of copies. It is not a security boundary, and
/// SECURITY.md says so.
/// </para>
/// </remarks>
public sealed class SecretBuffer : IDisposable
{
    /// <summary>How many characters the buffer holds before it has to grow.</summary>
    public const int InitialCapacity = 64;

    private char[] _buffer = new char[InitialCapacity];
    private int _length;
    private bool _disposed;

    /// <summary>The characters written so far.</summary>
    public ReadOnlySpan<char> Value => _disposed ? default : _buffer.AsSpan(0, _length);

    /// <summary>Number of characters written.</summary>
    public int Length => _length;

    /// <summary>
    /// Whether every character of the backing array is zero. A test hook: it inspects storage
    /// that <see cref="Value"/> deliberately stops exposing once disposed, so the zeroing can be
    /// asserted rather than merely assumed from the fact that Dispose was called.
    /// </summary>
    public bool IsZeroed
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
    /// <param name="value">The character to append.</param>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Append(char value)
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
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Backspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_length > 0)
        {
            _buffer[--_length] = '\0';
        }
    }

    /// <summary>Discards everything written so far.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Array.Clear(_buffer);
        _length = 0;
    }

    /// <summary>Appends every character of <paramref name="text"/>.</summary>
    /// <param name="text">The characters to append.</param>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Append(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            Append(c);
        }
    }

    /// <summary>Whether two buffers hold the same characters.</summary>
    /// <param name="other">The buffer to compare against.</param>
    /// <returns><see langword="true"/> when both hold the same characters in the same order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Ordinal and length-sensitive. Not constant-time, and deliberately so: both operands are
    /// values the same user just typed at the same prompt, so there is no attacker to leak a
    /// timing signal to.
    /// </remarks>
    public bool ValueEquals(SecretBuffer other)
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
