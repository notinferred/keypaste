using System.Globalization;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Makes an agent's change to an item in the owner's own unlocked vault under the owner's claim: once the person allows it, or
/// unasked for the needs-update tag, at most once a minute for each item (THREATS.md T-57). The agent learns only whether it was made.
/// </summary>
/// <remarks>
/// Ordered as <see cref="SessionSaver"/> is: the item is resolved and checked before anyone is asked, only where the agent's
/// bridge reaches it whole, and checked again before it is written, so a vault that changed meanwhile changes nothing. An asked
/// change is Allow once only, and a refused or unanswered one holds the same item back for a minute (T-11); the tag puts nothing
/// to the person, so it neither waits for a hold nor starts one. A change is written only while the unlock it was asked under is
/// live, its line is written before the vault is saved, and one whose line or save fails is taken back out of the open vault.
/// </remarks>
public sealed class SessionItemChanger
{
    private static readonly TimeSpan _tagInterval = TimeSpan.FromMinutes(1);

    private readonly ApprovalGate _gate;
    private readonly Func<SessionLifetime, Vault?> _unlockedFor;
    private readonly Func<SessionLifetime, Func<Vault, bool>, AgentSave> _write;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;
    private readonly Lock _tagTurnsLock = new();
    private readonly Dictionary<string, TagTurn> _tagTurns = new(StringComparer.OrdinalIgnoreCase);

    internal SessionItemChanger(
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

    /// <summary>Decides one request: resolves and checks the item, asks the person unless the change is unasked, then records the change and saves it.</summary>
    /// <param name="request">The request, already admitted to <paramref name="lifetime"/>.</param>
    /// <param name="connectionId">The connection it came on, which an unlabelled agent waits its turn as.</param>
    /// <param name="lifetime">The unlock it was asked under.</param>
    /// <param name="record">Writes the line of a change about to be saved, answering whether it was written.</param>
    /// <param name="waiting">The lifetime's <see cref="SessionLifetime.Waiting"/> as the request began.</param>
    /// <param name="cancellationToken">Withdraws the question when the agent stops waiting, the vault is locked or it is reloaded.</param>
    /// <returns>Whether it was made, with the item it named once resolved, and whether its line is already written.</returns>
    internal async ValueTask<Decided> DecideAsync(
        ItemChangeRequest request,
        string connectionId,
        SessionLifetime lifetime,
        Func<Decided, bool> record,
        CancellationToken waiting,
        CancellationToken cancellationToken)
    {
        if (ItemChangeRules.Check(request, out var change) is { } problem)
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
                return Refused(AuditMethod.VaultChanged, "another program changed the vault since it was opened here, so nothing is changed until it is reloaded");
            case VaultSaveStatus.Unsaved:
                return Refused(AuditMethod.VaultChanged, "the vault holds a change that is not saved yet");
            case VaultSaveStatus.Unreadable:
                return Refused(AuditMethod.Failed, "the vault's file could not be read to compare");
        }

        var source = new VaultCredentialSource(() => vault, _clock);

        if (!source.TryResolve(request.Entry, out var name, out var failure))
        {
            return failure switch
            {
                CredentialFailure.Ambiguous => Refused(AuditMethod.InvalidRequest, "more than one item answers to that path; name the item by its handle"),
                CredentialFailure.VaultLocked => Refused(AuditMethod.VaultLocked, "no vault is unlocked"),
                CredentialFailure.NotFound => Refused(AuditMethod.OutOfScope, "no item answers to that handle or path"),
                _ => Refused(AuditMethod.VaultChanged, "the vault could not be read as its file holds it"),
            };
        }

        // The re-check that makes a handle safe, and stricter than a request for a value: a reach through a project tag
        // shows only an item's project variables, which is not the item, so it changes nothing.
        switch (exposure.Reach(name, vault.Tags(name) ?? []))
        {
            case ExposureReach.None:
                return Refused(AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge");
            case ExposureReach.ProjectFields:
                return Refused(AuditMethod.OutOfScope, "this agent reaches the item through a project tag, which shows it only the item's project variables and lets it change nothing");
        }

        var zone = _clock.LocalTimeZone;
        ItemChangeCheck check;

        try
        {
            check = ItemChanges.Check(vault, name, change, request.ClientLabel, zone);
        }
        catch (VaultException failed)
        {
            return Refused(AuditMethod.InvalidRequest, failed.Message);
        }

        var where = TerminalEdit.PathOf(name);

        if (check.Refusal is { } refusal || check.Item is not { } item)
        {
            return Refused(AuditMethod.InvalidRequest, check.Refusal ?? "no item answers to that name", where);
        }

        string says;
        Decided made;

        if (change.Asks)
        {
            var prompt = ItemChangePrompt.For(request.ClientName, request.ClientLabel, item, change, request.Reason) with { Created = check.Created };
            var said = await _gate.AskAsync($"change\0{EntryHandle.For(item)}", prompt, cancellationToken, ApprovalGate.Asker(request.ClientLabel, connectionId)).ConfigureAwait(false);

            if (!lifetime.IsLive || waiting.IsCancellationRequested)
            {
                return Withdrawn(lifetime, where, item.Uuid);
            }

            if (said != ApprovalAnswer.ApprovedOnce)
            {
                var why = said switch
                {
                    ApprovalAnswer.Denied => "a person did not allow the change",
                    ApprovalAnswer.Cooldown => "a change to the same item was refused a moment ago",
                    ApprovalAnswer.TimedOut => "nobody answered inside the window",
                    ApprovalAnswer.Busy => "another prompt was already waiting for an answer",
                    ApprovalAnswer.Cancelled => "the agent gave up before anybody answered",
                    ApprovalAnswer.NoChannel => "there was nowhere to ask a person",
                    _ => "asking a person went wrong",
                };

                _narrate?.Invoke($"did not {prompt.Says} for {prompt.Client}: {why}");
                return Refused(said.ToAuditMethod(), why, where, item.Uuid);
            }

            says = $"{prompt.Says} for {prompt.Client}";
            made = new Decided(new ItemChangeReply(AuditDecision.Granted, AuditMethod.Prompt, $"a person allowed it: {prompt.Says}") { Item = where }, where, item.Uuid);
        }
        else
        {
            if (!TryTakeTagTurn(EntryHandle.For(item), out var until, out var recordsRefusal))
            {
                var held = $"{ItemChangeRules.NeedsUpdateTag} on {where} changed less than a minute ago; try again after {MinuteAfter(until, zone)}";

                if (recordsRefusal)
                {
                    _narrate?.Invoke($"did not change {where} for {request.ClientLabel ?? request.ClientName}: {held}");
                }

                return Refused(AuditMethod.Cooldown, held, where, item.Uuid) with { Unrecorded = !recordsRefusal };
            }

            var shown = ApprovalPrompt.For(request.ClientName, item, SaveRequestRules.Password, request.Reason, request.ClientLabel);
            var tag = change.Kind == ItemChangeKind.MarkNeedsUpdate ? "Tag" : "Untag";

            says = $"{tag} {shown.Entry} {ItemChangeRules.NeedsUpdateTag} for {shown.Client}, unasked";
            made = new Decided(new ItemChangeReply(AuditDecision.Granted, AuditMethod.Allowed, $"an agent may do this without asking: {tag} {shown.Entry} {ItemChangeRules.NeedsUpdateTag}") { Item = where }, where, item.Uuid);
        }

        Decided? decided = null;

        try
        {
            return decided = Commit();
        }
        finally
        {
            if (!change.Asks && decided?.Recorded != true)
            {
                GiveBackTagTurn(EntryHandle.For(item));
            }
        }

        Decided Commit()
        {
            if (cancellationToken.IsCancellationRequested || !lifetime.TryCommitAfter(waiting))
            {
                return Withdrawn(lifetime, where, item.Uuid);
            }

            string? late = null;
            var written = LoggedWrite.Run(
                _write,
                lifetime,
                open => (late = ItemChanges.Apply(open, change, request.ClientLabel, check, _clock.GetUtcNow(), zone)) is null,
                () => record(made));
            var allowed = change.Asks ? "a person allowed the change" : "the change was checked";

            if (written.Late)
            {
                return Refused(AuditMethod.VaultChanged, $"{allowed}, and then: {late}", where, item.Uuid);
            }

            if (written.Unrecorded)
            {
                _narrate?.Invoke($"did not {says}: {LoggedWrite.Unrecorded}");
                return Refused(AuditMethod.Failed, LoggedWrite.Unrecorded, where, item.Uuid);
            }

            if (!written.Made)
            {
                _narrate?.Invoke($"did not {says}: {written.Saved.Reason}");
                var after = written.Recorded ? "the change was recorded and then not saved, so it was not made" : $"{allowed}, and the vault was not saved";
                return Refused(SessionEditor.MethodOf(written.Saved), $"{after}: {written.Saved.Reason}", where, item.Uuid);
            }

            _narrate?.Invoke($"{says}: done");
            return made with { Recorded = true };
        }
    }

    /// <summary>Takes the item's one tag change of the minute, or says until when another holds it and whether this refusal is the minute's one recorded.</summary>
    /// <remarks>Held per item, not per agent, since a bridge's label is its own claim and two agents would otherwise take turns.</remarks>
    private bool TryTakeTagTurn(string item, out DateTimeOffset until, out bool recordsRefusal)
    {
        var now = _clock.GetUtcNow();

        lock (_tagTurnsLock)
        {
            foreach (var (held, turn) in _tagTurns)
            {
                if (turn.Taken + _tagInterval <= now)
                {
                    _tagTurns.Remove(held);
                }
            }

            if (_tagTurns.TryGetValue(item, out var taken))
            {
                until = taken.Taken + _tagInterval;
                recordsRefusal = !taken.RefusalRecorded;
                _tagTurns[item] = taken with { RefusalRecorded = true };
                return false;
            }

            _tagTurns[item] = new TagTurn(now, RefusalRecorded: false);
            until = now;
            recordsRefusal = false;
            return true;
        }
    }

    private void GiveBackTagTurn(string item)
    {
        lock (_tagTurnsLock)
        {
            _tagTurns.Remove(item);
        }
    }

    /// <summary>The first whole minute, in the person's time, at or after <paramref name="until"/>, as <c>HH:mm</c>.</summary>
    private static string MinuteAfter(DateTimeOffset until, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(until, zone);
        var minute = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset);
        return (minute < local ? minute.AddMinutes(1) : minute).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static Decided Withdrawn(SessionLifetime lifetime, string where, string? item) =>
        lifetime.IsLive
            ? Refused(AuditMethod.VaultChanged, $"{lifetime.WithdrawnBecause} before this was changed", where, item)
            : Refused(AuditMethod.VaultLocked, "the vault was locked before this was changed", where, item);

    private static Decided Refused(AuditMethod method, string reason, string? where = null, string? item = null) =>
        new(new ItemChangeReply(AuditDecision.Denied, method, reason), where, item);

    /// <summary>When an item's tag change of the minute was taken, and whether a refusal it held back is recorded yet.</summary>
    private readonly record struct TagTurn(DateTimeOffset Taken, bool RefusalRecorded);

    /// <summary>The reply, and the item it resolved to for the owner's line.</summary>
    /// <param name="Reply">What the agent is told.</param>
    /// <param name="Resolved">The item's path once resolved, or null.</param>
    /// <param name="Item">The item's UUID as hex once resolved, or null.</param>
    internal sealed record Decided(ItemChangeReply Reply, string? Resolved = null, string? Item = null)
    {
        /// <summary>Whether its line was written before the save it records, so no other is written for it.</summary>
        public bool Recorded { get; init; }

        /// <summary>Whether no line is written for it, as for a tag refusal repeated within the minute of the one recorded.</summary>
        public bool Unrecorded { get; init; }
    }
}
