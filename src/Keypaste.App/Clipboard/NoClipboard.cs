namespace Keypaste.App.Clipboard;

/// <summary>
/// The clipboard when there is no window to have one.
/// </summary>
/// <remarks>
/// <para>
/// Every operation fails, which is the truth rather than a convenience: without a top level there is
/// nothing to put a value on. A copy button backed by this reports that it could not reach the
/// clipboard, which is what somebody in that situation needs to be told.
/// </para>
/// <para>
/// It exists so <see cref="ViewModels.ShellViewModel"/> can be constructed in a test with no
/// application, no window and no display — the same reason the theme applier arrives as a delegate.
/// A test that is <em>about</em> copying passes a fake instead.
/// </para>
/// </remarks>
internal sealed class NoClipboard : IAppClipboard
{
    private NoClipboard()
    {
    }

    /// <summary>The only one there needs to be.</summary>
    internal static NoClipboard Instance { get; } = new();

    /// <inheritdoc/>
    public Task<bool> TrySetSecretAsync(string secret) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<bool> TrySetPlainAsync(string text) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<byte[]?> TryReadHashAsync() => Task.FromResult<byte[]?>(null);

    /// <inheritdoc/>
    public Task<bool> TryClearAsync() => Task.FromResult(false);
}
