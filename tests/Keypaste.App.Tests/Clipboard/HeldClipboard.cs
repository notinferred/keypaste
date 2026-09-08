using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Clipboard;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// An in-memory clipboard whose operations can be held open, the way a real one is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FakeClipboard"/> completes every call synchronously, which is what makes the
/// fire-and-forget paths assertable on the following line — and also what makes a whole class of
/// defect unreachable. `AvaloniaClipboard` awaits the windowing system; on Windows that can wait on
/// whichever process currently owns the clipboard. A lock, a quit or a second copy can happen
/// while that await is outstanding, and F.2c exists because they did.
/// </para>
/// <para>
/// <b>The value lands when the hold is released, not when the call is entered.</b> A fixture that
/// wrote first and awaited afterwards would be modelling a write that had already happened, which
/// is the case that was never in doubt.
/// </para>
/// </remarks>
internal sealed class HeldClipboard : IAppClipboard
{
    private readonly List<TaskCompletionSource> _writes = [];
    private readonly List<TaskCompletionSource> _reads = [];

    /// <summary>What is on it. A test reads this; production code has no equivalent.</summary>
    internal string? Content { get; private set; }

    /// <summary>Whether it holds a secret set with the exclusion formats.</summary>
    internal bool ContentWasSetAsASecret { get; private set; }

    internal int ClearCount { get; private set; }

    internal int SetCount { get; private set; }

    /// <summary>Forced failure of the write.</summary>
    internal bool SetFails { get; set; }

    /// <summary>Forced failure of the read-back.</summary>
    internal bool ReadFails { get; set; }

    /// <summary>Whether a write should wait for <see cref="ReleaseWrites"/>.</summary>
    internal bool HoldWrites { get; set; }

    /// <summary>Whether a read should wait for <see cref="ReleaseReads"/>.</summary>
    internal bool HoldReads { get; set; }

    /// <summary>How many writes are waiting to be released.</summary>
    internal int WritesInFlight => _writes.Count;

    /// <summary>How many reads are waiting to be released.</summary>
    internal int ReadsInFlight => _reads.Count;

    /// <summary>Lets every held write finish.</summary>
    internal void ReleaseWrites() => Release(_writes);

    /// <summary>Lets every held read finish.</summary>
    internal void ReleaseReads() => Release(_reads);

    /// <summary>Simulates the user copying something else.</summary>
    internal void ReplaceExternally(string text)
    {
        Content = text;
        ContentWasSetAsASecret = false;
    }

    public Task<bool> TrySetSecretAsync(string secret) => SetAsync(secret, secret: true);

    public Task<bool> TrySetPlainAsync(string text) => SetAsync(text, secret: false);

    public async Task<byte[]?> TryReadHashAsync()
    {
        await Held(_reads, HoldReads).ConfigureAwait(true);

        return ReadFails
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(Content ?? string.Empty));
    }

    public async Task<bool> TryClearAsync()
    {
        await Held(_writes, HoldWrites).ConfigureAwait(true);

        Content = null;
        ContentWasSetAsASecret = false;
        ClearCount++;
        return true;
    }

    private static void Release(List<TaskCompletionSource> waiting)
    {
        var held = waiting.ToArray();
        waiting.Clear();

        foreach (var one in held)
        {
            one.SetResult();
        }
    }

    private static Task Held(List<TaskCompletionSource> waiting, bool hold)
    {
        if (!hold)
        {
            return Task.CompletedTask;
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.Add(gate);
        return gate.Task;
    }

    private async Task<bool> SetAsync(string text, bool secret)
    {
        await Held(_writes, HoldWrites).ConfigureAwait(true);

        if (SetFails)
        {
            return false;
        }

        Content = text;
        ContentWasSetAsASecret = secret;
        SetCount++;
        return true;
    }
}
