using Keypaste.Core.Approval;

namespace Keypaste.Core.Ownership;

/// <summary>What an owner releases env sets to <c>keypaste run --session</c> with.</summary>
/// <param name="Gate">Where the person is asked: the owner's own gate, so its prompts and an agent's never overlap.</param>
/// <param name="VaultFor">The vault a lifetime may read, or null once it has ended.</param>
/// <param name="Clock">What expiry is judged against.</param>
/// <param name="Grants">The timed grants a person gave repeated runs, or null to ask about every run.</param>
public sealed record SessionEnvironments(
    ApprovalGate Gate,
    Func<SessionLifetime, Vault?> VaultFor,
    TimeProvider Clock,
    EnvGrantCache? Grants = null);
