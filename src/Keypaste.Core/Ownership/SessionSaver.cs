using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Puts an agent's request to save a value to the person, who enters the value, and saves it at the place the agent named
/// in the owner's own unlocked vault, under the owner's claim (F.79). The agent learns only whether it was saved.
/// </summary>
/// <remarks>
/// The place is checked before anyone is asked, and only where the agent's bridge can see it, and checked again before it is
/// written, so a vault that changed meanwhile saves nothing. A refused or unanswered request holds the same place back for a
/// minute (THREATS.md T-11); a value is written only while the unlock it was asked under is live, and one whose save fails is
/// taken back out of the open vault.
/// </remarks>
public sealed class SessionSaver
{
    private readonly ApprovalGate _gate;
    private readonly Func<SessionLifetime, Vault?> _unlockedFor;
    private readonly Func<SessionLifetime, Func<Vault, bool>, AgentSave> _write;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;

    internal SessionSaver(
        ApprovalGate gate,
        Func<SessionLifetime, Vault?> unlockedFor,
        Func<SessionLifetime, Func<Vault, bool>, AgentSave> write,
        TimeProvider clock,
        Action<string>? narrate)
    {
        _gate = gate;
        _unlockedFor = unlockedFor;
        _write = write;
        _clock = clock;
        _narrate = narrate;
    }

    /// <summary>Decides one request: checks the place, asks the person for the value, then writes and saves it.</summary>
    /// <param name="request">The request, already admitted to <paramref name="lifetime"/>.</param>
    /// <param name="lifetime">The unlock it was asked under.</param>
    /// <param name="waiting">The lifetime's <see cref="SessionLifetime.Waiting"/> as the request began.</param>
    /// <param name="cancellationToken">Withdraws the question when the agent stops waiting, the vault is locked or it is reloaded.</param>
    /// <returns>Whether it was saved.</returns>
    internal async ValueTask<SaveReply> DecideAsync(SaveRequest request, SessionLifetime lifetime, CancellationToken waiting, CancellationToken cancellationToken)
    {
        if (SaveRequestRules.Check(request) is { } problem)
        {
            return Refused(AuditMethod.InvalidRequest, $"the request's {problem.Argument} {problem.Rule}");
        }

        if (!EntryExposure.TryCreate(request.Exposure, out var exposure, out _))
        {
            return Refused(AuditMethod.Failed, "this bridge's --expose is not usable");
        }

        if (_unlockedFor(lifetime) is not { } vault)
        {
            return Refused(AuditMethod.VaultLocked, "no vault is unlocked");
        }

        switch (vault.SaveState().Status)
        {
            case VaultSaveStatus.ChangedOnDisk:
                return Refused(AuditMethod.VaultChanged, "another program changed the vault since it was opened here, so nothing is saved until it is reloaded");
            case VaultSaveStatus.Unsaved:
                return Refused(AuditMethod.VaultChanged, "the vault holds a change that is not saved yet");
            case VaultSaveStatus.Unreadable:
                return Refused(AuditMethod.Failed, "the vault's file could not be read to compare");
        }

        var scene = new EditScene(_clock.GetUtcNow());
        SaveCheck check;

        try
        {
            check = ValueSaves.Check(vault, request, scene);
        }
        catch (VaultException failure)
        {
            return Refused(AuditMethod.InvalidRequest, failure.Message);
        }

        if (check.Refusal is { } refusal)
        {
            return Refused(AuditMethod.InvalidRequest, SessionEditor.Said(refusal));
        }

        // The same check a request for the value would meet, on the item that will hold it, so an agent cannot plant a value
        // where it could not see one, nor learn by asking that a place it cannot see exists.
        if (check.Holder is not { } holder || !EntryExposure.Permits(exposure.Reach(holder, check.HolderTags), request.Named))
        {
            return Refused(AuditMethod.OutOfScope, "the place is outside what this agent can see through this bridge");
        }

        var where = request.ForProject ? request.Where : TerminalEdit.PathOf(holder);
        var prompt = SavePrompt.For(request.ClientName, request.ClientLabel, where, request.Named, request.Reason) with
        {
            ForProject = request.ForProject,
            Creates = check.Creates,
            Replaces = check.Replaces,
        };
        var said = await _gate.AskSavingAsync($"save\0{where}\0{request.Named}", prompt, cancellationToken).ConfigureAwait(false);

        if (!lifetime.IsLive || waiting.IsCancellationRequested)
        {
            return Withdrawn(lifetime);
        }

        if (said is not { Answer: ApprovalAnswer.ApprovedOnce, Value: { Length: > 0 } value })
        {
            var why = said.Answer switch
            {
                ApprovalAnswer.Denied => "a person did not save a value there",
                ApprovalAnswer.Cooldown => "the same request was refused a moment ago",
                ApprovalAnswer.TimedOut => "nobody answered inside the window",
                ApprovalAnswer.Busy => "another prompt was already waiting for an answer",
                ApprovalAnswer.Cancelled => "the agent gave up before anybody answered",
                ApprovalAnswer.NoChannel => "there was nowhere to ask a person to enter a value",
                _ => "asking a person went wrong",
            };

            _narrate?.Invoke($"saved nothing at {prompt.Where} for {prompt.Client}: {why}");
            return Refused(said.Answer.ToAuditMethod(), why);
        }

        if (cancellationToken.IsCancellationRequested || !lifetime.TryCommitAfter(waiting))
        {
            return Withdrawn(lifetime);
        }

        Vault? written = null;
        VaultCheckpoint? checkpoint = null;
        EditResult? result = null;

        var saved = _write(lifetime, open =>
        {
            written = open;
            checkpoint = open.Checkpoint();
            result = ValueSaves.Apply(open, request, check, value, scene);
            return result.Done is not null;
        });

        if (result?.Refusal is { } late)
        {
            SessionEditor.TakeBack(written, checkpoint);
            return Refused(AuditMethod.VaultChanged, $"a person entered a value, and then: {SessionEditor.Said(late)}");
        }

        if (!saved.Holds || result?.Done is null)
        {
            SessionEditor.TakeBack(written, checkpoint);
            _narrate?.Invoke($"saved nothing at {prompt.Where}: {saved.Reason}");
            return Refused(SessionEditor.MethodOf(saved), $"a person entered a value, and the vault was not saved: {saved.Reason}");
        }

        _narrate?.Invoke($"saved {prompt.Field} at {prompt.Where} for {prompt.Client}; the agent was not given it");
        return new SaveReply(AuditDecision.Granted, AuditMethod.Prompt, "a person entered a value and the vault holds it; the agent was not given it") { Where = where };
    }

    private static SaveReply Withdrawn(SessionLifetime lifetime) =>
        lifetime.IsLive
            ? Refused(AuditMethod.VaultChanged, $"{lifetime.WithdrawnBecause} before this was saved")
            : Refused(AuditMethod.VaultLocked, "the vault was locked before this was saved");

    private static SaveReply Refused(AuditMethod method, string reason) => new(AuditDecision.Denied, method, reason);
}
