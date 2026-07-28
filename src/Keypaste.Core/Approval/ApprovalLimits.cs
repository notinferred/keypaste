namespace Keypaste.Core.Approval;

/// <summary>
/// The three numbers the approval flow is allowed to have opinions about.
/// </summary>
/// <remarks>
/// <para>
/// Three, not eight. Rate limits, per-client pauses, use caps and minimum display times are all
/// real mitigations for prompt fatigue (THREATS.md T-11) and all of them are policy — which is
/// Stage 2.3's subject and, for the pause switch, Stage 4.3's feature. What is kept here is the
/// pair without which a single agent loop becomes a prompt storm: one request in front of a human
/// at a time, and a cooldown after a refusal.
/// </para>
/// <para>
/// <b>Why the window is 45 seconds and not the 60 the specification asks for.</b> MCP clients
/// impose their own request timeout, and 60 seconds is the SDK default that both Claude Desktop and
/// Claude Code inherit. A 60-second window therefore sits exactly on the client's own wall: an
/// approval given at second 55 arrives into a request that has already been abandoned, and the
/// agent's retry raises a second prompt for something the human has already approved. 45 leaves
/// room for the answer to get home. DECISIONS.md D-0027.
/// </para>
/// </remarks>
public sealed record ApprovalLimits
{
    /// <summary>How long a human has to answer, by default.</summary>
    public const int DefaultWindowSeconds = 45;

    /// <summary>The shortest window a human could reasonably be asked to answer in.</summary>
    public const int MinimumWindowSeconds = 5;

    /// <summary>The longest window, kept below every MCP client's own request timeout.</summary>
    public const int MaximumWindowSeconds = 55;

    /// <summary>How long a grant lives by default, whatever the agent asked for.</summary>
    public const int DefaultMaximumTtlSeconds = 300;

    /// <summary>How long the same refused request is auto-denied for.</summary>
    public const int DefaultCooldownSeconds = 60;

    /// <summary>The longest lifetime an agent, or a policy rule, may even ask for.</summary>
    /// <remarks>
    /// The ceiling on the <em>request</em>, not on the grant: what is actually issued is this
    /// clamped again by <see cref="MaximumTtlSeconds"/>, and by a rule's own ceiling when one
    /// applies. It lives here rather than in the tool schema or the agent's argument parser because
    /// all three have to agree about it and docs/PRODUCT.md law 4.3 does not allow a number that means one
    /// thing to be written down three times.
    /// </remarks>
    public const int MaximumRequestableTtlSeconds = 3600;

    /// <summary>The limits keypaste ships with.</summary>
    public static ApprovalLimits Default { get; } = new();

    /// <summary>How long a human has to answer before silence becomes a denial.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(DefaultWindowSeconds);

    /// <summary>The longest grant this approver will issue, however long an agent asks for.</summary>
    public int MaximumTtlSeconds { get; init; } = DefaultMaximumTtlSeconds;

    /// <summary>How long after a refusal the same request is denied without asking again.</summary>
    public TimeSpan DenialCooldown { get; init; } = TimeSpan.FromSeconds(DefaultCooldownSeconds);

    /// <summary>The TTL that will actually apply to a request.</summary>
    /// <param name="requestedSeconds">What the agent asked for.</param>
    /// <returns>The requested value, clamped to at least one second and at most <see cref="MaximumTtlSeconds"/>.</returns>
    /// <remarks>
    /// The clamped number, not the requested one, is what the human is shown. Showing an agent's
    /// requested hour when five minutes will be granted would make the prompt a worse source of
    /// truth than the audit log, which is backwards.
    /// </remarks>
    public int EffectiveTtlSeconds(int requestedSeconds) =>
        Math.Clamp(requestedSeconds, 1, MaximumTtlSeconds);

    /// <summary>The TTL that will actually apply when a policy rule covers the request.</summary>
    /// <param name="requestedSeconds">What the agent asked for.</param>
    /// <param name="ruleCeilingSeconds">The rule's own <c>max_ttl_seconds</c>.</param>
    /// <returns>The smaller of the two ceilings, applied to the request.</returns>
    /// <remarks>
    /// <b>Both ceilings apply, and a rule may only lower.</b> A rule saying <c>max_ttl_seconds =
    /// 3600</c> under an approver started <c>--max-ttl 60</c> grants 60. This overload exists so
    /// that the policy path does no arithmetic of its own: a hand-written
    /// <c>Math.Min(ruleCeiling, requested)</c> at the call site is exactly how a file in the user's
    /// home directory would come to raise a ceiling the operator set on the command line.
    /// </remarks>
    public int EffectiveTtlSeconds(int requestedSeconds, int ruleCeilingSeconds) =>
        Math.Min(EffectiveTtlSeconds(requestedSeconds), Math.Max(ruleCeilingSeconds, 1));
}
