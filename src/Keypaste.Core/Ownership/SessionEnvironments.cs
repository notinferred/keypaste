using Keypaste.Core.Approval;

namespace Keypaste.Core.Ownership;

/// <summary>What an owner releases env sets to <c>keypaste run --session</c> with.</summary>
/// <param name="Gate">Where the person is asked: the owner's own gate, so its prompts and an agent's never overlap.</param>
/// <param name="VaultFor">The vault a lifetime may read, or null once it has ended.</param>
/// <param name="Clock">What expiry is judged against.</param>
/// <param name="Narrate">Optional: a line for the operator's terminal for each run released.</param>
/// <param name="Launches">The passes the app's Run confirmation issued, or null to refuse every request carrying one.</param>
public sealed record SessionEnvironments(
    ApprovalGate Gate,
    Func<SessionLifetime, Vault?> VaultFor,
    TimeProvider Clock,
    Action<string>? Narrate = null,
    LaunchPasses? Launches = null);
