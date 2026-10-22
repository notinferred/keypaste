using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;

namespace Keypaste.Core.Tests;

/// <summary>
/// The approver's five seams, with fakes for the vault and the human and nothing faked in between.
/// </summary>
/// <remarks>
/// Shared by the handler's two test classes. One
/// set of fakes rather than two, because the claim both classes make is about the <em>order</em>
/// the handler does things in, and two subtly different vaults would let a change look fine in one
/// file while breaking the ordering asserted in the other.
/// </remarks>
internal sealed class ApproverFixture : IDisposable
{
    internal const string Sentinel = "sk_live_handler_sentinel";

    /// <summary>The agent a test's requests come from unless it says otherwise.</summary>
    internal const string Agent = "claude-code";

    /// <summary>
    /// Settings that see as far as any bridge exposes and are otherwise the defaults, so the bridge's
    /// own exposure decides what <see cref="Agent"/> reaches, as it did before agents had settings.
    /// </summary>
    internal static AgentAccess SeesAll { get; } = AgentAccess.Defaults with { See = AgentAccessBuilders.See("**") };

    internal FakeSource Source { get; } = new();

    internal FakeChannel Channel { get; } = new();

    internal ManualClock Clock { get; } = new();

    internal GrantCache Grants { get; }

    internal ApprovalGate Gate { get; }

    internal ApproverHandler Handler { get; }

    /// <summary>Each agent's settings and this computer's ceilings; every agent gets the defaults until a test sets more.</summary>
    internal FakeAgentSettings Settings { get; } = new();

    /// <summary>The owner's hourly limits on Automatic, on the same clock as everything else.</summary>
    internal HourlyLimit Hourly { get; }

    /// <summary>Every line the handler printed to the operator's terminal, in order.</summary>
    /// <remarks>
    /// The handler writes no audit line; the authority around it records the reply. So this is the
    /// only record the handler produces itself, and a line claiming a release that never left the
    /// process is a claim nothing else would contradict, which is why it is worth asserting on.
    /// </remarks>
    internal List<string> Narration { get; } = [];

    /// <summary>Builds a fixture.</summary>
    /// <param name="limits">The operator's own ceilings, when a test needs them narrower.</param>
    /// <remarks>
    /// The hourly limits and the handler share <see cref="Clock"/>. A limit measured on a second clock
    /// would silently ignore <c>ManualClock.Advance</c>, and a test asserting that a limit comes back
    /// would pass for the wrong reason.
    /// </remarks>
    internal ApproverFixture(ApprovalLimits? limits = null)
    {
        Grants = new GrantCache(Clock);
        Gate = new ApprovalGate(Channel, Clock, limits ?? ApprovalLimits.Default);
        Hourly = new HourlyLimit(Clock);
        Settings.Records[Agent] = SeesAll;
        Handler = new ApproverHandler(Source, Source, Gate, Grants, Settings, Narration.Add, hourly: Hourly, clock: Clock);
    }

    public void Dispose()
    {
        Gate.Dispose();
        Grants.Dispose();
    }
}

/// <summary>A vault with two entries, one under <c>env/</c> and one well outside it, untagged unless a test tags them.</summary>
internal sealed class FakeSource : ICredentialSource, IEntryNameLister
{
    private static readonly EntryName[] _entries =
    [
        new EntryName("env/dev", "STRIPE_KEY"),
        new EntryName("personal", "bank"),
    ];

    internal bool Locked { get; set; }

    internal bool FailReads { get; set; }

    /// <summary>When set, a read refuses the value as holding a placeholder keypaste cannot fill in, for this reason.</summary>
    internal string? Unfilled { get; set; }

    internal int Reads { get; private set; }

    internal int Resolves { get; private set; }

    /// <summary>Runs inside a read or a listing, so a test can lock the owner in the middle of one.</summary>
    internal Action? During { get; set; }

    /// <summary>What a read returns. Settable so a test can hand over a field no frame can carry.</summary>
    internal string Value { get; set; } = ApproverFixture.Sentinel;

    /// <summary>The entries asked about live every time, as a protecting tag would make them.</summary>
    internal HashSet<EntryName> Protected { get; } = [];

    /// <summary>Each entry's own tags, which a <c>tag:</c> exposure reads.</summary>
    internal Dictionary<EntryName, string[]> Tagged { get; } = [];

    public bool RequiresLiveApproval(EntryName name) => Protected.Contains(name);

    public IReadOnlyList<string> Tags(EntryName name) => Tagged.TryGetValue(name, out var tags) ? tags : [];

    public bool TryResolve(string entryArgument, [NotNullWhen(true)] out EntryName? name, out CredentialFailure failure)
    {
        name = null;
        Resolves++;

        if (Locked)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        foreach (var candidate in _entries)
        {
            var path = candidate.GroupPath.Length == 0
                ? candidate.Title
                : candidate.GroupPath + "/" + candidate.Title;

            if (string.Equals(path, entryArgument, StringComparison.Ordinal)
                || string.Equals(EntryHandle.For(candidate), entryArgument, StringComparison.Ordinal))
            {
                name = candidate;
                failure = CredentialFailure.None;
                return true;
            }
        }

        failure = CredentialFailure.NotFound;
        return false;
    }

    public bool TryRead(EntryName name, string field, [NotNullWhen(true)] out ReleasedField? value, out CredentialFailure failure, out string unfilled)
    {
        value = null;
        unfilled = string.Empty;

        if (FailReads)
        {
            failure = CredentialFailure.Failed;
            return false;
        }

        if (Unfilled is { } reason)
        {
            failure = CredentialFailure.Unfilled;
            unfilled = reason;
            return false;
        }

        Reads++;
        During?.Invoke();
        value = new ReleasedField(field, Value);
        failure = CredentialFailure.None;
        return true;
    }

    public bool TryList(EntryExposure exposure, [NotNullWhen(true)] out IReadOnlyList<ListedEntry>? names, out CredentialFailure failure)
    {
        names = null;

        if (Locked)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        During?.Invoke();
        names =
        [
            .. _entries
                .Select(name => new ListedEntry(name, ["password", "STRIPE_SECRET_KEY"], Tags(name)))
                .Select(entry => (Entry: entry, Reach: exposure.Reach(entry.Name, entry.Tags)))
                .Where(listed => listed.Reach != ExposureReach.None)
                .Select(listed => listed.Entry.Within(listed.Reach)),
        ];
        failure = CredentialFailure.None;
        return true;
    }
}

internal sealed class FakeChannel : IApprovalChannel
{
    private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

    /// <summary>When set, nobody answers: the prompt stays up until it is withdrawn.</summary>
    internal bool Hold { get; set; }

    /// <summary>Runs while a request is on screen, before it is answered.</summary>
    internal Action? WhileAsking { get; set; }

    internal int Asked { get; private set; }

    internal ApprovalPrompt? LastPrompt { get; private set; }

    internal EnvReleasePrompt? LastEnvPrompt { get; private set; }

    internal RunPrompt? LastRunPrompt { get; private set; }

    internal AccessChangePrompt? LastAccessPrompt { get; private set; }

    /// <summary>What a confirmation of a change that gives an agent more is answered with; a no unless a test says otherwise.</summary>
    internal ApprovalAnswer AccessAnswer { get; set; } = ApprovalAnswer.Denied;

    /// <summary>How many confirmations of a change were asked.</summary>
    internal int AccessAsked { get; private set; }

    /// <summary>Runs while a confirmation of a change is on screen, before it is answered.</summary>
    internal Action? WhileConfirming { get; set; }

    /// <summary>What a terminal's edit is answered with; a no unless a test says otherwise.</summary>
    internal ApprovalAnswer EditAnswer { get; set; } = ApprovalAnswer.Denied;

    /// <summary>How many terminal edits were asked about.</summary>
    internal int EditAsked { get; private set; }

    /// <summary>The last terminal edit asked about.</summary>
    internal TerminalEditPrompt? LastEditPrompt { get; private set; }

    /// <summary>Runs while a terminal's edit is on screen, before it is answered.</summary>
    internal Action? WhileEditing { get; set; }

    /// <summary>Every notice that the change Always for asked for did not take effect, in order.</summary>
    internal List<AlwaysForNotice> Told { get; } = [];

    /// <summary>Completes once a held prompt is up.</summary>
    internal Task Waiting => _waiting.Task;

    /// <summary>Whether a held prompt was withdrawn.</summary>
    internal bool Withdrawn { get; private set; }

    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        return AnswerAsync(cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken)
    {
        LastEnvPrompt = prompt;
        return AnswerAsync(cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken)
    {
        LastRunPrompt = prompt;
        return AnswerAsync(cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
    {
        LastAccessPrompt = prompt;
        AccessAsked++;
        WhileConfirming?.Invoke();
        return ValueTask.FromResult(AccessAnswer);
    }

    public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken)
    {
        LastEditPrompt = prompt;
        EditAsked++;
        WhileEditing?.Invoke();
        return ValueTask.FromResult(EditAnswer);
    }

    public void Tell(AlwaysForNotice notice) => Told.Add(notice);

    private async ValueTask<ApprovalAnswer> AnswerAsync(CancellationToken cancellationToken)
    {
        Asked++;
        WhileAsking?.Invoke();

        if (!Hold)
        {
            return Answer;
        }

        _waiting.TrySetResult();

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Withdrawn = true;
        }

        return ApprovalAnswer.Denied;
    }
}
