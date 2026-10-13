using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste agent</c> records every request it answers, so a log it cannot write stops it before the
/// master password is asked for, rather than leaving a person to unlock an owner that can only refuse.
/// </summary>
public sealed class AgentAuditLogTests : IDisposable
{
    private const string _master = "agent-audit-master";

    private readonly CliHarness _harness = new();

    public AgentAuditLogTests() => _harness.SeedVault(_master, ("env/dev/TOKEN", "agent-audit-value"));

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_log_it_cannot_write_stops_it_before_the_password_is_asked_for()
    {
        var log = KeypasteHome.AuditPath(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Directory.CreateDirectory(log);
        var asked = _harness.Prompt.PromptsSeen.Count;
        _harness.Prompt.Enqueue(_master);

        var exit = _harness.Run("agent", "--vault", _harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.StartsWith($"keypaste: the audit log at '{log}' could not be opened: ", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(asked, _harness.Prompt.PromptsSeen.Count);
        Assert.DoesNotContain("listening on", _harness.Err, StringComparison.Ordinal);
    }
}
