namespace Keypaste.Core.Approval;

/// <summary>
/// One field of one entry, released to an agent after a human said yes.
/// </summary>
/// <remarks>
/// <para>
/// This is the credential path's answer to <see cref="EntryName"/>, and it exists for the same
/// reason. It carries the name of the field that was asked for and the characters of that field,
/// and it has no other member — so "return ONLY the requested field value" is a property of the
/// type system rather than of a code path somebody could later widen. It is deliberately
/// <em>not</em> a <see cref="VaultEntry"/> with the other fields blanked (THREATS.md T-8).
/// </para>
/// <para>
/// The value is held in a <see cref="SecretBuffer"/> so it can be zeroed when the grant expires.
/// The honest limit is the one <see cref="SecretBuffer"/> already states: the value reached this
/// type as a <see cref="string"/> out of <see cref="VaultEntry"/>, and that copy cannot be
/// cleared. This narrows the window; it is not in-memory secrecy, and SECURITY.md says so.
/// </para>
/// </remarks>
public sealed class ReleasedField : IDisposable
{
    private readonly SecretBuffer _value = new();

    /// <summary>Holds one field value under the name it was asked for.</summary>
    /// <param name="field">The field name the agent requested, such as <c>password</c>.</param>
    /// <param name="value">The characters of that field, copied into a clearable buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public ReleasedField(string field, ReadOnlySpan<char> value)
    {
        ArgumentNullException.ThrowIfNull(field);

        Field = field;
        _value.Append(value);
    }

    /// <summary>The field name that was asked for.</summary>
    public string Field { get; }

    /// <summary>The released characters, or empty once this has been disposed.</summary>
    public ReadOnlySpan<char> Value => _value.Value;

    /// <summary>How many characters were released.</summary>
    public int Length => _value.Length;

    /// <summary>
    /// Whether the backing storage holds nothing but zeroes. A test hook, for the same reason
    /// <see cref="SecretBuffer.IsZeroed"/> is one: expiry has to be assertable rather than assumed.
    /// </summary>
    public bool IsZeroed => _value.IsZeroed;

    /// <inheritdoc/>
    public void Dispose() => _value.Dispose();
}
