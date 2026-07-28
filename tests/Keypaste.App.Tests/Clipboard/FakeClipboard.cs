using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Clipboard;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// An in-memory clipboard whose every operation completes synchronously.
/// </summary>
/// <remarks>
/// Completing synchronously is what makes the fire-and-forget paths — the deadline tick and
/// <c>Dispose</c> — assertable on the line after they happen, rather than needing a delay nobody
/// can bound.
/// </remarks>
internal sealed class FakeClipboard : IAppClipboard
{
    /// <summary>What is on it. A test reads this; production code has no equivalent.</summary>
    internal string? Content { get; private set; }

    /// <summary>Whether it holds a secret set with the exclusion formats.</summary>
    internal bool ContentWasSetAsASecret { get; private set; }

    internal int ClearCount { get; private set; }

    internal int SetCount { get; private set; }

    /// <summary>Forced failure, for the no-clipboard path.</summary>
    internal bool SetFails { get; set; }

    /// <summary>Forced failure of the read-back, for the fail-closed branch.</summary>
    internal bool ReadFails { get; set; }

    public Task<bool> TrySetSecretAsync(string secret)
    {
        if (SetFails)
        {
            return Task.FromResult(false);
        }

        Content = secret;
        ContentWasSetAsASecret = true;
        SetCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TrySetPlainAsync(string text)
    {
        if (SetFails)
        {
            return Task.FromResult(false);
        }

        Content = text;
        ContentWasSetAsASecret = false;
        SetCount++;
        return Task.FromResult(true);
    }

    public Task<byte[]?> TryReadHashAsync() =>
        Task.FromResult<byte[]?>(ReadFails
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(Content ?? string.Empty)));

    public Task<bool> TryClearAsync()
    {
        Content = null;
        ClearCount++;
        return Task.FromResult(true);
    }

    /// <summary>Simulates the user copying something else.</summary>
    internal void ReplaceExternally(string text) => Content = text;
}
