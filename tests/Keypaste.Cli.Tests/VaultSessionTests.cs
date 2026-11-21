using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <see cref="VaultSession.OpenHeld(string, ParseResult, CliContext, Func{Keypaste.Core.Vault, int}, bool)"/>: a verb that saves takes the vault's claim before it asks for
/// the password, so it never saves under a running owner, and gives the claim back after.
/// </summary>
public sealed class VaultSessionTests : IDisposable
{
    private const string _master = "vault-session-master";

    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Home => KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);

    private static ParseResult Line() => CliApp.Parse([]);

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

    /// <summary>A verb reading or saving a file put back to an older copy warns on stderr, once, and still runs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOlderCopyPutBack_IsWarnedOfOnStderrOnce_AndTheVerbRuns(bool held)
    {
        _harness.SeedVault(_master, ("env/dev/TOKEN", "first"));
        var older = File.ReadAllBytes(_harness.VaultPath);
        _harness.Prompt.Enqueue(_master, "later");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/LATER", "--vault", _harness.VaultPath));
        Assert.DoesNotContain(Vault.OlderCopyText, _harness.Err, StringComparison.Ordinal);
        File.WriteAllBytes(_harness.VaultPath, older);

        Assert.Equal(CliApp.ExitSuccess, Open(held));
        Assert.Contains($"keypaste: warning: {Vault.OlderCopyText}", _harness.Err, StringComparison.Ordinal);
        Assert.Contains(VaultBackups.DirectoryFor(_harness.VaultPath), _harness.Err, StringComparison.Ordinal);

        _harness.Stderr.GetStringBuilder().Clear();
        Assert.Equal(CliApp.ExitSuccess, Open(held));
        Assert.DoesNotContain(Vault.OlderCopyText, _harness.Err, StringComparison.Ordinal);
    }

    /// <summary>A verb whose save leaves out another program's data says once, on stderr, where the copy holding it is; a verb that only reads says nothing.</summary>
    [Fact]
    public void ASaveLeavingOutAnotherProgramsData_SaysWhereTheCopyIs_Once()
    {
        _harness.SeedVault(_master, ("env/dev/TOKEN", "first"));
        _harness.Prompt.Enqueue(_master, "later");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/LATER", "--vault", _harness.VaultPath));
        ForeignXml.AddTo(_harness.VaultPath, _master);
        const string warning = "keypaste: warning: This vault held data from another program";

        Assert.Equal(CliApp.ExitSuccess, Open(held: false));
        Assert.DoesNotContain(warning, _harness.Err, StringComparison.Ordinal);

        _harness.Prompt.Enqueue(_master, "again");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/AGAIN", "--vault", _harness.VaultPath));
        var kept = VaultBackups.List(_harness.VaultPath)[0];
        Assert.Single(_harness.Err.Split('\n'), line => line.StartsWith(warning, StringComparison.Ordinal));
        Assert.Contains(kept.Path, _harness.Err, StringComparison.Ordinal);
        Assert.True(ForeignXml.Holds(kept.Path, _master));

        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master, "third");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/THIRD", "--vault", _harness.VaultPath));
        Assert.DoesNotContain(warning, _harness.Err, StringComparison.Ordinal);
    }

    /// <summary>A verb whose save the system did not flush the vault's folder for says so once, on stderr, and succeeds.</summary>
    [Fact]
    public void ASaveWhoseFolderIsNotFlushed_SaysSoOnStderr()
    {
        _harness.SeedVault(_master, ("env/dev/TOKEN", "first"));
        _harness.Prompt.Enqueue(_master, "later");

        using (UnflushableFolder.Stage(Path.GetDirectoryName(_harness.VaultPath)!))
        {
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/LATER", "--vault", _harness.VaultPath));
        }

        Assert.Single(_harness.Err.Split('\n'), line => line.StartsWith($"keypaste: warning: {Vault.UnflushedText}", StringComparison.Ordinal));

        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master, "again");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("add", "env/dev/AGAIN", "--vault", _harness.VaultPath));
        Assert.DoesNotContain(Vault.UnflushedText, _harness.Err, StringComparison.Ordinal);
    }

    private int Open(bool held)
    {
        _harness.Prompt.Enqueue(_master);
        Func<Vault, int> body = _ => CliApp.ExitSuccess;
        return held
            ? VaultSession.OpenHeld(_harness.VaultPath, Line(), _harness.NewContext(), body)
            : VaultSession.Open(_harness.VaultPath, Line(), _harness.NewContext(), body);
    }
}
