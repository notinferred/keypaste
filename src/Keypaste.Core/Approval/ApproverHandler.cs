using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;

namespace Keypaste.Core.Approval;

/// <summary>
/// The approver's decision procedure: resolve, re-scope, check the grant, ask a human, release one
/// field.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the core with no I/O of its own — no terminal, no dialog, no pipe — so every
/// ordering that matters is reachable from a unit test. What it talks to are five seams: where
/// entries are resolved and read, where names come from, where a human is, what they have already
/// said yes to, and what they said yes to in advance.
/// </para>
/// <para>
/// <b>The order is the security property.</b> Resolve, then re-check the exposure, then look for a
/// live grant, then honour a refusal a person just gave, then consult the policy, and only then ask
/// a person; read the field last of all. Reading earlier would put a secret in memory for requests
/// that are about to be refused, and asking earlier would let an agent raise a prompt for an entry
/// it was never allowed to name.
/// </para>
/// <para>
/// <b>Where the policy sits in that order is the whole of DECISIONS.md D-0029.</b> After the
/// exposure re-check, because a rule is a <em>narrowing</em> of what a person would be asked about
/// and never a parallel grant — ahead of it, a rule reading <c>entries = ["**"]</c> would release
/// an entry the bridge's own <c>--expose</c> never permitted, which is a file in the user's home
/// directory overriding the client's configuration. After the grant cache, because a live grant is
/// a decision a person made about this exact request and re-attributing it to a machine rule would
/// be a lie in the log. After the cooldown, because a person's explicit no outranks a rule. And in
/// here rather than inside <see cref="ApprovalGate"/>, because a pre-authorized request must not be
/// able to collide with a prompt somebody is in the middle of answering.
/// </para>
/// </remarks>
public sealed class ApproverHandler : IApproverHandler
{
    private readonly ICredentialSource _source;
    private readonly IEntryNameLister _lister;
    private readonly ApprovalGate _gate;
    private readonly GrantCache _grants;
    private readonly PolicyGate _policy;
    private readonly Action<string>? _narrate;

    /// <summary>Builds the handler over its five seams.</summary>
    /// <param name="source">Where entries are resolved and one field is read.</param>
    /// <param name="lister">Where names come from. Deliberately not the same seam.</param>
    /// <param name="gate">Where a human is asked, and where the deadline lives.</param>
    /// <param name="grants">What a human has already said yes to.</param>
    /// <param name="policy">What a human said yes to in advance.</param>
    /// <param name="narrate">Optional: a line of running commentary for the operator's terminal.</param>
    /// <exception cref="ArgumentNullException">Any of the five seams is null.</exception>
    /// <remarks>
    /// <paramref name="policy"/> is not nullable, and "nothing is pre-authorized" is the value
    /// <see cref="PolicyGate.None"/>. A nullable seam makes every future reader work out what null
    /// means, the answer has to be "deny", and a thing that must mean deny should be a value rather
    /// than an absence.
    /// </remarks>
    public ApproverHandler(
        ICredentialSource source,
        IEntryNameLister lister,
        ApprovalGate gate,
        GrantCache grants,
        PolicyGate policy,
        Action<string>? narrate = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(policy);

        _source = source;
        _lister = lister;
        _gate = gate;
        _grants = grants;
        _policy = policy;
        _narrate = narrate;
    }

    /// <inheritdoc/>
    public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out _))
        {
            return ValueTask.FromResult(new NamesReply(false, [], "the exposure this bridge was configured with is not usable"));
        }

        if (!_lister.TryList(exposure, out var names, out var failure))
        {
            return ValueTask.FromResult(new NamesReply(false, [], Explain(failure)));
        }

        return ValueTask.FromResult(new NamesReply(true, names, string.Empty));
    }

    /// <inheritdoc/>
    public async ValueTask<CredentialReply> RequestAsync(
        CredentialRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connectionId);

        if (!CredentialFields.IsReleasable(request.Field))
        {
            return Refused(AuditMethod.InvalidRequest, "the field asked for is not one keypaste releases");
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out var globError))
        {
            return Refused(AuditMethod.Failed, $"the exposure this bridge was configured with is not usable: {globError}");
        }

        if (!_source.TryResolve(request.Entry, out var name, out var failure))
        {
            // A name that resolves to nothing and a name that resolves outside the exposure get the
            // same answer, on purpose. Telling them apart would let an agent enumerate which
            // entries exist in parts of the vault it was never allowed to see — the exposure rule
            // undone by a difference in error messages (THREATS.md T-4). The audit line below still
            // records which it was, because that reader is the human.
            return failure == CredentialFailure.VaultLocked
                ? Refused(AuditMethod.VaultLocked, "no vault is unlocked")
                : Refused(AuditMethod.OutOfScope, Explain(failure));
        }

        if (!exposure.Allows(name))
        {
            // The re-check that makes handles safe. The bridge can test a path against its globs
            // before forwarding, but it cannot resolve a handle without the vault, so without this
            // a handle would be the way around the exposure rule.
            return Refused(AuditMethod.OutOfScope, "the entry is outside this bridge's configured exposure");
        }

        var handle = EntryHandle.For(name);
        var display = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, 0).Entry;
        var key = new GrantKey(connectionId, handle, request.Field);

        // Declared before the try so the copy the cache hands out is zeroed on every path — the
        // repo's idiom for a disposable that only exists on one branch.
        ReleasedField? live = null;

        try
        {
            if (_grants.TryUse(key, out live, out var remaining))
            {
                _narrate?.Invoke($"reused an approval for {display} ({(int)remaining.TotalSeconds}s left)");

                return new CredentialReply
                {
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.GrantCache,
                    Reason = "served from a grant a person had already given, inside its lifetime",
                    Entry = display,
                    TtlSeconds = (int)remaining.TotalSeconds,
                    Value = live.Value.ToString(),
                };
            }
        }
        finally
        {
            live?.Dispose();
        }

        // Before the policy, not after. A person who refused this exact request a moment ago has
        // said something more specific and more recent than any rule, and a rule must never
        // resurrect a decision they just made.
        if (_gate.IsInCooldown(CooldownKey(key)))
        {
            _narrate?.Invoke($"refused {display}: {Explain(ApprovalAnswer.Cooldown)}");
            return Refused(AuditMethod.Cooldown, Explain(ApprovalAnswer.Cooldown), display);
        }

        var outcome = _policy.Evaluate(request.ClientLabel, name, request.Field);

        if (outcome.Kind == PolicyOutcomeKind.RateLimited)
        {
            var spent = outcome.Rule!;
            _narrate?.Invoke($"refused {display}: {spent.Id} has used its allowance for this hour");

            return Refused(
                AuditMethod.PolicyLimit,
                $"policy rule {spent.Cite()} has used its allowance for this hour",
                display);
        }

        if (outcome.Kind == PolicyOutcomeKind.Granted)
        {
            return Preapproved(request, name, display, outcome.Rule!);
        }

        var ttl = _gate.Limits.EffectiveTtlSeconds(request.TtlSeconds);
        var prompt = ApprovalPrompt.For(request.ClientName, name, request.Field, request.Reason, ttl);

        var answer = await _gate.AskAsync(CooldownKey(key), prompt, cancellationToken).ConfigureAwait(false);

        if (answer != ApprovalAnswer.Approved)
        {
            _narrate?.Invoke($"refused {display} for {prompt.Client}: {Explain(answer)}");
            return Refused(Method(answer), Explain(answer), display);
        }

        // Last of all. Nothing has decrypted a field until a person said yes to this exact request.
        if (!_source.TryRead(name, request.Field, out var released, out var readFailure))
        {
            return Refused(AuditMethod.Failed, Explain(readFailure), display);
        }

        using (released)
        {
            _grants.Store(key, released, TimeSpan.FromSeconds(ttl));
            _narrate?.Invoke($"released {display} to {prompt.Client} for {ttl}s");

            return new CredentialReply
            {
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Prompt,
                Reason = "a person approved this request",
                Entry = display,
                TtlSeconds = ttl,
                Value = released.Value.ToString(),
            };
        }
    }

    /// <summary>Releases a field because a rule the user wrote in advance covers this request.</summary>
    /// <remarks>
    /// <para>
    /// <b>Both ceilings apply, and the rule may only lower.</b> The arithmetic is
    /// <see cref="ApprovalLimits"/>'s, not this method's: a hand-written minimum here is precisely
    /// how a file in the user's home directory would come to grant a longer lifetime than the
    /// operator set with <c>--max-ttl</c>.
    /// </para>
    /// <para>
    /// <b>It does not seed the grant cache.</b> Doing so would serve every later request inside the
    /// TTL for free and off the rule's hourly count, turning <c>max_per_hour</c> into a cap on
    /// grants rather than on releases; it would hide releases two onward behind <c>grant-cache</c>
    /// lines that do not name the rule; and there is nothing to suppress, because with a rule in
    /// force nobody is being asked twice. The cost is one vault read per call, which is CPU spent
    /// to buy an accurate log.
    /// </para>
    /// <para>
    /// The read is still last of all, exactly as on the prompted path: a rule that matches is not a
    /// reason to decrypt anything until the release itself is certain.
    /// </para>
    /// </remarks>
    private CredentialReply Preapproved(
        CredentialRequest request,
        EntryName name,
        string display,
        PolicyRule rule)
    {
        var ttl = _gate.Limits.EffectiveTtlSeconds(request.TtlSeconds, rule.MaximumTtlSeconds);

        if (!_source.TryRead(name, request.Field, out var released, out var readFailure))
        {
            return Refused(AuditMethod.Failed, Explain(readFailure), display);
        }

        using (released)
        {
            // Unconditional on the operator's terminal, and the only live signal that this
            // happened at all: no prompt was drawn and nobody was asked (THREATS.md T-12).
            _narrate?.Invoke($"released {display} to {rule.Id} for {ttl}s without asking");

            return new CredentialReply
            {
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Policy,
                Reason = $"pre-authorized by policy rule {rule.Cite()}",
                Entry = display,
                TtlSeconds = ttl,
                Value = released.Value.ToString(),
            };
        }
    }

    /// <inheritdoc/>
    public void Disconnected(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        _grants.Revoke(connectionId);
    }

    /// <summary>
    /// What counts as "the same request" for the cooldown: the same connection asking for the same
    /// field of the same entry. Spelled out rather than using the key's generated ToString, which
    /// is a debugger convenience and not a thing to key behaviour on.
    /// </summary>
    private static string CooldownKey(GrantKey key) =>
        $"{key.ConnectionId}|{key.Handle}|{key.Field}";

    private static CredentialReply Refused(AuditMethod method, string reason, string? entry = null) => new()
    {
        Decision = AuditDecision.Denied,
        Method = method,
        Reason = reason,
        Entry = entry,
        TtlSeconds = 0,
    };

    private static AuditMethod Method(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => AuditMethod.Prompt,
        ApprovalAnswer.TimedOut => AuditMethod.TimedOut,
        ApprovalAnswer.Cancelled => AuditMethod.Cancelled,
        ApprovalAnswer.Busy => AuditMethod.Busy,
        ApprovalAnswer.Cooldown => AuditMethod.Cooldown,
        ApprovalAnswer.NoChannel => AuditMethod.NoApprover,
        _ => AuditMethod.Failed,
    };

    private static string Explain(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => "a person refused this request",
        ApprovalAnswer.TimedOut => "nobody answered inside the window",
        ApprovalAnswer.Cancelled => "the client gave up before anybody answered",
        ApprovalAnswer.Busy => "another request was already in front of a person",
        ApprovalAnswer.Cooldown => "the same request was refused a moment ago",
        ApprovalAnswer.NoChannel => "there was nowhere to ask a person",
        _ => "asking a person went wrong",
    };

    private static string Explain(CredentialFailure failure) => failure switch
    {
        CredentialFailure.VaultLocked => "no vault is unlocked",
        CredentialFailure.NotFound => "no entry answers to that name",
        CredentialFailure.Ambiguous => "more than one entry answers to that name",
        CredentialFailure.NoSuchField => "the field asked for is not one keypaste releases",
        CredentialFailure.Empty => "the entry has nothing in that field",
        _ => "the vault could not be read",
    };
}
