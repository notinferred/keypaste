using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Internal;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// A lock does not wait for a save queued behind another save in this process: the queued save writes nothing and
/// its terminal edit is refused as locked, so the lock returns, its endpoint stopped, while the other still holds the
/// save gate (F.35, D-0392).
/// </summary>
/// <remarks>Alone, because the save gate is the whole process's and this test holds it.</remarks>
[Collection(nameof(SaveGateHeld))]
public sealed class LockWhileASaveWaitsTests
{
    private const string _sentinel = "SENTINEL-QUEUED-SAVE-6d1f93";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_lock_refuses_a_terminal_edit_whose_save_waits_at_the_gate_and_returns_while_the_gate_is_held()
    {
        using var fixture = new TempVault();
#pragma warning disable CA2000 // The authority owns the session, as it does at launch.
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home), null, () => new AllowsEdits());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        var before = await File.ReadAllBytesAsync(fixture.Path_, Token);
        await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _wait, Token);
        Assert.NotNull(client);
        var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
        var edit = new EditRequest(new SetPassword(new EntryName(string.Empty, "example"), NewSecret.Of(_sentinel)))
        {
            Vault = fixture.Path_,
            Session = attached!.Session!,
        };

        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();

        void OnWaitingForGate(long holder)
        {
            if (holder == KeePassInterop.HeldByAFileOperation)
            {
                queued.TrySetResult();
            }
        }

        SaveClock.WaitingForGate += OnWaitingForGate;
        var holding = Task.Run(
            () => KeePassInterop.WhileNoSaveReplaces(() =>
            {
                held.SetResult();
                release.Wait(Token);
                return true;
            }),
            Token);

        try
        {
            await held.Task.WaitAsync(_wait, Token);
            var editing = client.EditAsync(edit, Token).AsTask();
            await queued.Task.WaitAsync(_wait, Token);

            await authority.Session.LockAsync(VaultLockReason.Manual).WaitAsync(_wait, Token);

            Assert.False(holding.IsCompleted, "the gate was given up before the lock returned");
            var reply = await editing.WaitAsync(_wait, Token);
            Assert.NotNull(reply);
            Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked), (reply.Decision, reply.Method));
        }
        finally
        {
            SaveClock.WaitingForGate -= OnWaitingForGate;
            release.Set();
            await holding;
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path_, Token));
    }

    /// <summary>A person who allows every terminal edit at once.</summary>
    private sealed class AllowsEdits : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.NoChannel);

        public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
    }
}

[CollectionDefinition(nameof(SaveGateHeld), DisableParallelization = true)]
public sealed class SaveGateHeld;
