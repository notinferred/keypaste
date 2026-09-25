using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The titlebar says whether the file holds what is open, and the MCP card counts the distinct
/// clients attached to the live session (D-0361).
/// </summary>
public sealed class ShellStatusTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Titlebar_SaysSavedUnsavedChangedOnDisk()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, home: fixture.Home);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var name = Path.GetFileName(fixture.Path_);

        Assert.Equal($"{name} · saved", shell.VaultStatus);
        Assert.True(shell.VaultStatusOk);
        Assert.StartsWith("Saved ", shell.VaultStatusDetail, StringComparison.Ordinal);

        session.Unlocked!.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "NEW", Password = "x" });
        Assert.Equal($"{name} · unsaved changes", shell.VaultStatus);
        Assert.Equal(StatusTone.Accent, shell.VaultStatusTone);

        session.Unlocked.Save();
        Assert.Equal($"{name} · saved", shell.VaultStatus);

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "ELSEWHERE", Password = "y" });
            other.Save();
        }

        for (var tick = 0; tick < 5; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal($"{name} · changed on disk", shell.VaultStatus);
        Assert.True(shell.VaultStatusDanger);
    }

    [Fact]
    public async Task TheMcpCard_CountsDistinctConnectedClients()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyAnswers());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(authority.Session, fixture.Home, authority, clock: clock);
        Assert.Equal("stdio · no clients", shell.McpDetail);

        var endpoint = Assert.IsType<AuthorityStatus.Serving>(authority.Status).Endpoint;
        List<ApproverClient> clients = [];

        foreach (var identity in new AttachClient?[] { new("claude", "1", "cc"), new("claude", "1", "cc"), new("cursor", "1", null), null })
        {
            var client = await ApproverClient.TryConnectAsync(endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = identity }, Token))!.Attached);
            clients.Add(client);
        }

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("stdio · 2 clients", shell.McpDetail);

        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }

    private sealed class NobodyAnswers : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);
    }
}
