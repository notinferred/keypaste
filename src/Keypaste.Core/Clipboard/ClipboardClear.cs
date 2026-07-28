namespace Keypaste.Core.Clipboard;

/// <summary>
/// When a clipboard keypaste wrote to should be cleared, and the only copy of that rule.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one line of logic and it is the wrong line to re-derive. Two front ends now put
/// secrets on a clipboard and take them back: <c>keypaste get</c>, which blocks a terminal for the
/// window, and the desktop app, which counts down in a corner of a window. They wait differently on
/// purpose. What they must not do differently is decide, so the deciding lives here and the waiting
/// lives in each of them.
/// </para>
/// <para>
/// <b>There is no clipboard here.</b> This namespace holds a decision, not a transport: the CLI
/// reaches the clipboard through the tool the platform ships and the app reaches it through the
/// windowing system it already depends on, and neither of those belongs in a library that has no
/// <c>PackageReference</c> and opens no handle.
/// </para>
/// </remarks>
public static class ClipboardClear
{
    /// <summary>How long a secret stays on the clipboard unless a caller says otherwise.</summary>
    /// <remarks>
    /// Twenty seconds is long enough to switch windows and paste, and short enough that walking
    /// away mid-task does not leave a password behind. Stated once so the CLI's default and the
    /// app's countdown cannot drift apart into two documented numbers.
    /// </remarks>
    public static TimeSpan DefaultWindow { get; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Whether a clipboard should be cleared, given what it holds now and what keypaste put there.
    /// </summary>
    /// <param name="readSucceeded">Whether the clipboard could be read back at all.</param>
    /// <param name="current">A hash of the clipboard's current contents.</param>
    /// <param name="expected">A hash taken immediately after keypaste wrote to it.</param>
    /// <returns><see langword="true"/> when the clipboard should be cleared.</returns>
    /// <remarks>
    /// <para>
    /// The clear is <b>conditional</b>: a user who copied something else in the meantime keeps it.
    /// That is the defect in <c>keepassxc-cli clip</c> this deliberately does not repeat.
    /// </para>
    /// <para>
    /// <b>A read-back that failed means clear anyway.</b> Skipping the clear because verification
    /// was impossible would leave a password on the clipboard indefinitely, which is the worst
    /// outcome available here — fail closed, docs/PRODUCT.md law 3.7. Clearing something the user copied
    /// since is recoverable; leaving a secret behind is not.
    /// </para>
    /// <para>
    /// Hashes rather than text, because knowing whether the clipboard changed needs nothing more.
    /// Comparison is <see cref="CryptographicOperations.FixedTimeEquals"/>, which also returns
    /// <see langword="false"/> for a length mismatch, so a truncated or absent read is a change.
    /// </para>
    /// </remarks>
    public static bool Should(
        bool readSucceeded,
        ReadOnlySpan<byte> current,
        ReadOnlySpan<byte> expected) =>
        !readSucceeded || CryptographicOperations.FixedTimeEquals(current, expected);
}
