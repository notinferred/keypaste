using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A real owner of a real vault on a real pipe of its own: its gate asks the test's person, its handler reads the vault,
/// and <see cref="TestSession"/>'s session check answers in front of both, recording in the owner's log.
/// </summary>
internal sealed class RealOwner : IAsyncDisposable
{
    private readonly ApprovalGate _gate;
    private readonly ServedOwner _served;

    private RealOwner(Vault vault, IApprovalChannel person, AuditLog log, IAgentRules rules, string? pipeName)
    {
        _gate = new ApprovalGate(person, TimeProvider.System, ApprovalLimits.Default);
        _served = ServedOwner.Serve(TestSession.Over(vault, ServedOwner.VaultHandler(() => vault, _gate, rules), log), pipeName);
    }

    internal string PipeName => _served.PipeName;

    /// <summary>Serves <paramref name="vault"/> under <paramref name="rules"/>, or under <see cref="TestSession.Settings"/> when none are given.</summary>
    internal static RealOwner Start(Vault vault, IApprovalChannel person, AuditLog log, IAgentRules? rules = null, string? pipeName = null) =>
        new(vault, person, log, rules ?? TestSession.Settings(), pipeName);

    public async ValueTask DisposeAsync()
    {
        await _served.DisposeAsync();
        _gate.Dispose();
    }
}
