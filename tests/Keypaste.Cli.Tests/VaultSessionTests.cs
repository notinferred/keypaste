using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <see cref="VaultSession.OpenHeld"/>: a verb that saves takes the vault's claim before it asks for
/// the password, so it never saves under a running owner (D-0317), and gives the claim back after.
/// </summary>
public sealed class VaultSessionTests : IDisposable
{
    private const string _master = "vault-session-master";

    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Home => KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);

    private static CommandLine Line()
    {
        Assert.True(CommandLine.TryParse([], 0, [], out var line, out var error), error);
        return line;
    }

    [Fact]
    public void OpenHeld_RefusesAHeldVault_NamingTheHolder_WithoutAskingForThePassword()
    {
        _harness.SeedVault(_master);
        _harness.Prompt.PromptsSeen.Clear();
        Assert.True(VaultClaim.TryAcquire(Home, _harness.VaultPath, OwnerKind.DesktopApp, out var claim, out var refusal), refusal);
        var ran = false;

        using (claim)
        {
            _harness.Prompt.Enqueue(_master);

            var exit = VaultSession.OpenHeld(_harness.VaultPath, Line(), _harness.NewContext(), _ =>
            {
                ran = true;
                return CliApp.ExitSuccess;
            });

            _harness.AssertExit(CliApp.ExitInternalError, exit);
        }

        Assert.False(ran);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains(
            $"keypaste: this vault is already unlocked in the keypaste desktop app (process {Environment.ProcessId}). Lock it there first.",
            _harness.Err,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpenHeld_ReleasesTheClaimAfterward()
    {
        _harness.SeedVault(_master);
        string? heldAs = null;

        _harness.Prompt.Enqueue(_master);
        var exit = VaultSession.OpenHeld(_harness.VaultPath, Line(), _harness.NewContext(), vault =>
        {
            Assert.False(VaultClaim.TryAcquire(Home, _harness.VaultPath, OwnerKind.TerminalAgent, out var second, out heldAs));
            return CliApp.ExitSuccess;
        });

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Contains("a keypaste command", heldAs, StringComparison.Ordinal);
        Assert.True(VaultClaim.TryAcquire(Home, _harness.VaultPath, OwnerKind.TerminalAgent, out var after, out var refusal), refusal);
        after.Dispose();
    }
}
