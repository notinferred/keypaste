using System.Globalization;

namespace Keypaste.Core.Approval;

/// <summary>
/// The three numbers the approval flow is allowed to have opinions about.
/// </summary>
/// <remarks>
/// <para>
/// Three, not eight. Hourly limits, per-agent modes, use caps and minimum display times are all
/// real mitigations for prompt fatigue (THREATS.md T-11), and the first two belong to each agent's
/// settings. What is kept here is the pair without which a single agent loop becomes a prompt storm:
/// one request in front of a human at a time, and a cooldown after a refusal.
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

    /// <summary>How long the timed grant a person can choose lasts by default, whatever the agent asked for.</summary>
    public const int DefaultMaximumTtlSeconds = 3600;

    /// <summary>How long the same refused request is auto-denied for.</summary>
    public const int DefaultCooldownSeconds = 60;

    /// <summary>The longest lifetime an agent may even ask for.</summary>
    /// <remarks>
    /// The ceiling on the <em>request</em>, not on the grant: what is actually issued is this
    /// clamped again by <see cref="MaximumTtlSeconds"/>. It lives here rather than in the tool schema or the agent's argument parser because
    /// all three have to agree about it and docs/PRODUCT.md law 4.3 does not allow a number that means one
    /// thing to be written down three times.
    /// </remarks>
    public const int MaximumRequestableTtlSeconds = 3600;

    /// <summary>The limits keypaste ships with.</summary>
    public static ApprovalLimits Default { get; } = new();

    /// <summary>How long a human has to answer before silence becomes a denial.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(DefaultWindowSeconds);

    /// <summary>The longest grant this approver will issue, and the length of the timed grant a prompt offers.</summary>
    public int MaximumTtlSeconds { get; init; } = DefaultMaximumTtlSeconds;

    /// <summary>How long after a refusal the same request is denied without asking again.</summary>
    public TimeSpan DenialCooldown { get; init; } = TimeSpan.FromSeconds(DefaultCooldownSeconds);

    /// <summary>The TTL that will actually apply to a request.</summary>
    /// <param name="requestedSeconds">What the agent asked for.</param>
    /// <returns>The requested value, clamped to at least one second and at most <see cref="MaximumTtlSeconds"/>.</returns>
    /// <remarks>
    /// What an Automatic release is bounded by. A person's timed grant is not: they chose its
    /// length on screen, and it is always <see cref="MaximumTtlSeconds"/>.
    /// </remarks>
    public int EffectiveTtlSeconds(int requestedSeconds) =>
        Math.Clamp(requestedSeconds, 1, MaximumTtlSeconds);

    /// <summary>A number of seconds as a person reads a duration: <c>1 hour</c>, <c>5 minutes</c>, <c>90 seconds</c>.</summary>
    /// <param name="seconds">The duration.</param>
    /// <returns>Whole hours or whole minutes when it is one, otherwise seconds.</returns>
    public static string Describe(int seconds) => seconds switch
    {
        > 0 when seconds % 3600 == 0 => Plural(seconds / 3600, "hour"),
        > 0 when seconds % 60 == 0 => Plural(seconds / 60, "minute"),
        _ => Plural(seconds, "second"),
    };

    private static string Plural(int count, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? string.Empty : "s")}");
}
