namespace Keypaste.Core.Policy;

/// <summary>What the policy had to say about one request.</summary>
public enum PolicyOutcomeKind
{
    /// <summary>No rule covers it. A person is asked, exactly as before this feature existed.</summary>
    NoRule = 0,

    /// <summary>A rule covers it and had allowance left. Nobody is asked.</summary>
    Granted = 1,

    /// <summary>A rule covers it and has spent its allowance for the hour.</summary>
    RateLimited = 2,
}

/// <summary>The policy's answer, and which rule gave it.</summary>
/// <param name="Kind">What the policy decided.</param>
/// <param name="Rule">The rule that decided, or null when none covered the request.</param>
public readonly record struct PolicyOutcome(PolicyOutcomeKind Kind, PolicyRule? Rule)
{
    /// <summary>The answer when nothing is pre-authorized.</summary>
    public static PolicyOutcome NoRule { get; } = new(PolicyOutcomeKind.NoRule, null);
}

/// <summary>What a person has said yes to in advance.</summary>
/// <remarks>
/// <para>
/// The fifth seam the approver holds, alongside the credential source, the entry lister, the
/// approval gate and the grant cache. It answers one question — is this request already
/// authorized — and it answers it without touching a vault, a pipe or a person.
/// </para>
/// <para>
/// <b>"Nothing is pre-authorized" is the value <see cref="None"/>, never null.</b> A nullable seam
/// makes every future reader ask what null means, the answer has to be "deny", and a thing that must
/// mean deny should be a value rather than an absence — the same argument <see cref="EntryExposure"/>
/// makes about an empty set of globs.
/// </para>
/// <para>
/// <b>It does not know the operator's ceiling and must not.</b> Applying <c>--max-ttl</c> is
/// <see cref="Approval.ApprovalLimits"/>'s job and the caller's, so there is exactly one
/// <see cref="Approval.ApprovalLimits"/> in the process. Handing this type a second copy is how a
/// file in the user's home directory would come to grant a longer lifetime than the one the operator
/// set on the command line.
/// </para>
/// </remarks>
public sealed class PolicyGate
{
    private readonly PolicyRateLimiter _limiter;

    /// <summary>Creates a gate over a rule set.</summary>
    /// <param name="document">The rules in force.</param>
    /// <param name="clock">The clock the hourly allowances are measured on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="clock"/> is null.</exception>
    public PolicyGate(PolicyDocument document, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);

        Document = document;
        _limiter = new PolicyRateLimiter(clock);
    }

    /// <summary>A gate that pre-authorizes nothing, so every request reaches a person.</summary>
    public static PolicyGate None { get; } = new(PolicyDocument.None, TimeProvider.System);

    /// <summary>The rules in force.</summary>
    public PolicyDocument Document { get; }

    /// <summary>Whether any rule is in force at all.</summary>
    public bool IsEmpty => Document.Rules.Count == 0;

    /// <summary>Decides whether a request is already authorized, and spends the allowance if it is.</summary>
    /// <param name="clientLabel">The operator's label for the asking bridge, or null if it set none.</param>
    /// <param name="name">The resolved, unsanitized entry name.</param>
    /// <param name="field">The field asked for.</param>
    /// <returns>The answer, and the rule that gave it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="field"/> is null.</exception>
    /// <remarks>
    /// <b>A rate-limited request does not fall through to the next rule.</b> The first rule matching
    /// on client, entry and field decides; retrying against rule 2 would let anyone defeat a cap by
    /// writing the same rule twice.
    /// <para>
    /// The allowance is spent here rather than after the field is read, because this is the last
    /// point at which the answer can still be "no". Everything after it is the release itself.
    /// </para>
    /// </remarks>
    public PolicyOutcome Evaluate(string? clientLabel, EntryName name, string field)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        if (!Document.TryMatch(clientLabel, name, field, out var rule))
        {
            return PolicyOutcome.NoRule;
        }

        return _limiter.TryUse(rule)
            ? new PolicyOutcome(PolicyOutcomeKind.Granted, rule)
            : new PolicyOutcome(PolicyOutcomeKind.RateLimited, rule);
    }

    /// <summary>How much of a rule's hourly allowance is spent right now.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The number of releases inside the last hour.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public int Spent(PolicyRule rule) => _limiter.Spent(rule);
}
