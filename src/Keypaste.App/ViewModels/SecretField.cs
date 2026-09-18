using Keypaste.App.Clipboard;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One field somebody enters a secret into, and the buffer behind it.
/// </summary>
/// <remarks>
/// <para>
/// 4.9 adds four of these — a new entry's password, a replacement password, a new variable's value
/// and a replacement value — and they differ only in which form owns them. Four hand-rolled buffers
/// would be four chances to forget one of the clearing paths, which is the failure that matters
/// here, so the rules live once and each form holds an instance.
/// </para>
/// <para>
/// <b>The value leaves through <see cref="Compose"/>, which is a method on purpose.</b>
/// <c>SecretHygieneTests</c> reflects over every property of every view model and reads what it
/// finds; a property returning the characters would hand them to that sweep, and one returning a
/// <see cref="ReadOnlySpan{T}"/> would throw from it and take the sweep down with it. A method is
/// invisible to both, and the only callers are the three <c>ConfirmAdd</c>/<c>SaveEdit</c> paths
/// that write to the vault.
/// </para>
/// <para>
/// <b>It deliberately does not override <c>ToString</c>.</b> It is about to be the value of a
/// styled property on <see cref="Controls.MaskedInput"/>, and the automation sweep in
/// <c>MaskedInputAutomationTests</c> stringifies every registered styled property it finds.
/// </para>
/// <para>
/// <b>Every mutator returns silently after disposal</b>, as <c>UnlockViewModel</c>'s do: a
/// keystroke can be in flight in the visual tree when the shell disposes its content on lock, and
/// <see cref="SecretBuffer"/> would throw from the key handler. The honest limit on all of this is
/// <see cref="SecretBuffer"/>'s own: a pasted or typed value existed as an immutable string on the
/// way in and cannot be wiped (THREATS.md T-18).
/// </para>
/// </remarks>
internal sealed class SecretField(ClipboardCountdown clipboard) : ObservableObject, ISecretSink, IDisposable
{
    private readonly SecretBuffer _buffer = new();

    private bool _disposed;
    private string _note = string.Empty;

    /// <summary>How many characters have been entered, which is all a screen may know.</summary>
    internal int MaskedLength => _disposed ? 0 : _buffer.Length;

    /// <summary>Whether anything has been entered.</summary>
    internal bool HasValue => MaskedLength > 0;

    /// <summary>Whether the backing array is all zeroes. A test seam, as on <see cref="SecretBuffer"/>.</summary>
    internal bool IsZeroed => _buffer.IsZeroed;

    /// <summary>
    /// What to tell the person about the last paste, or empty when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// A refused paste has to say so while the form is still open. The alternative — accepting it
    /// and quietly dropping whatever could not be typed — stores a secret that differs from the one
    /// on the clipboard, and nothing on screen would ever show the difference.
    /// </remarks>
    internal string Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    /// <inheritdoc/>
    public void Type(char value)
    {
        if (_disposed)
        {
            return;
        }

        _buffer.Append(value);
        Changed();
    }

    /// <inheritdoc/>
    public void Backspace()
    {
        if (_disposed)
        {
            return;
        }

        _buffer.Backspace();
        Changed();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        _buffer.Clear();
        Note = string.Empty;
        Changed();
    }

    /// <inheritdoc/>
    public async Task Paste()
    {
        if (_disposed)
        {
            return;
        }

        var outcome = await clipboard.TryPasteIntoAsync(_buffer).ConfigureAwait(true);

        if (_disposed)
        {
            // The vault locked while the windowing system was answering. Whatever landed in the
            // buffer has already been zeroed by Dispose; saying anything about it now would put a
            // message on a screen that no longer exists.
            return;
        }

        Note = outcome switch
        {
            PasteOutcome.Pasted => string.Empty,
            PasteOutcome.Empty => "There was nothing to paste.",
            PasteOutcome.Refused =>
                "That could not be pasted: it contains something no keyboard can type. Nothing was added.",
            _ => "The clipboard could not be read.",
        };

        Changed();
    }

    /// <summary>The entered characters, as the one string the vault write needs.</summary>
    /// <remarks>
    /// The string is unavoidable — <see cref="VaultEntry.Password"/> is one — and this is the only
    /// place it is made. <see cref="SecretBuffer"/>'s remarks state what that costs.
    /// </remarks>
    internal string Compose() => _disposed ? string.Empty : new string(_buffer.Value);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _buffer.Dispose();
        _disposed = true;
        _note = string.Empty;

        Raise(nameof(MaskedLength));
        Raise(nameof(HasValue));
        Raise(nameof(Note));
    }

    private void Changed()
    {
        Raise(nameof(MaskedLength));
        Raise(nameof(HasValue));
    }
}
