using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An env set resolved through the app's session is read from the vault as saved and released only
/// while the unlock that asked is live (E.1a, D-0313, D-0317).
/// </summary>
/// <remarks>
/// Each resolution goes through <see cref="AppVaultSession.Environments"/> and each edit through the
/// entries screen, so a check held only by a view or a test composition would not pass.
/// </remarks>
public sealed class EnvThroughSessionTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;

    public EnvThroughSessionTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(vault, "dev", "TOKEN", "v1");
            vault.Save();
        }

        _session = new AppVaultSession(_clock, home: _fixture.Home);
        Unlock();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task A_resolution_waiting_when_the_app_locks_releases_nothing()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = _session.Environments.ResolveAsync(
            "dev",
            async (_, token) =>
            {
                asked.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return true;
            },
            Cancel).AsTask();

        await asked.Task.WaitAsync(Cancel);
        _session.Lock(VaultLockReason.Manual);

        var resolved = await pending.WaitAsync(Cancel);
        Assert.Equal(EnvOutcome.Locked, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public async Task After_another_program_saves_the_file_nothing_is_released_until_it_is_unlocked_again()
    {
        using (var writer = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(writer, "dev", "TOKEN", "external");
            writer.Save();
        }

        var refused = await _session.Environments.ResolveAsync("dev", null, Cancel);
        Assert.Equal(EnvOutcome.ChangedOnDisk, refused.Outcome);
        Assert.Empty(refused.Variables);

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        Assert.Equal("external", (await _session.Environments.ResolveAsync("dev", null, Cancel)).Variables.Single().Value);
    }

    [Fact]
    public async Task An_edit_saved_in_the_app_is_the_next_value_resolved()
    {
        Assert.Equal("v1", (await _session.Environments.ResolveAsync("dev", null, Cancel)).Variables.Single().Value);

        using (var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start)))
        using (var entries = new EntriesViewModel(_session, countdown))
        {
            entries.Selected = entries.Rows.Single(row => row.Name == ProjectVariables.Home("dev"));
            var detail = entries.Detail!;
            detail.Fields.Single(field => field.Name == "TOKEN").ReplaceCommand.Execute(null);

            foreach (var c in "v2")
            {
                detail.ReplacementFieldValue.Type(c);
            }

            detail.ConfirmReplaceFieldCommand.Execute(null);
            Assert.False(detail.IsReplacingField, entries.Error);
        }

        var resolved = await _session.Environments.ResolveAsync("dev", null, Cancel);
        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal("v2", resolved.Variables.Single().Value);
    }

    [Fact]
    public async Task A_session_past_its_idle_deadline_or_locked_releases_nothing()
    {
        _clock.AdvanceWallOnly(AppVaultSession.DefaultIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.Equal(EnvOutcome.Locked, (await _session.Environments.ResolveAsync("dev", null, Cancel)).Outcome);

        _session.Lock(VaultLockReason.Manual);
        Assert.Equal(EnvOutcome.Locked, (await _session.Environments.ResolveAsync("dev", null, Cancel)).Outcome);
    }

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }
}
