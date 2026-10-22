using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Puts a terminal's edit to the person and makes it in the owner's own unlocked vault, saving it under
/// the owner's claim (D-0464).
/// </summary>
/// <remarks>
/// <para>
/// The edit is checked against the vault before anyone is asked and again before it is written, by the
/// same code the CLI runs on a vault it opens itself, and a change other than the one shown is refused.
/// The person sees names, never a value.
/// </para>
/// <para>
/// Any program can type a terminal command, so a refused or unanswered edit holds back the next one, and
/// every request for more access from an agent or a terminal, for a minute (THREATS.md T-11). A vault
/// that differs from its file is refused before anyone is asked, and an edit is written only while the
/// unlock it was asked under is live; one whose save fails is taken back out of the open vault, so no
/// later save writes what the reply says was not made.
/// </para>
/// </remarks>
public sealed class SessionEditor
{
    private readonly ApprovalGate _gate;
    private readonly Func<SessionLifetime, Vault?> _unlockedFor;
    private readonly Func<SessionLifetime, Func<Vault, bool>, AgentSave> _write;
    private readonly string _clientsPath;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;
    private readonly Action<EditDone>? _edited;

    /// <summary>Builds the editor for one unlock's gate.</summary>
    /// <param name="gate">Where the person is asked, under the same slot and cooldown as agents.</param>
    /// <param name="unlockedFor">The vault while a lifetime is live, otherwise null.</param>
    /// <param name="write">Runs an edit on the lifetime's vault and saves it under the owner's claim.</param>
    /// <param name="clientsPath">This computer's <c>clients.toml</c>, which a tag change is judged by.</param>
    /// <param name="clock">What time it is.</param>
    /// <param name="narrate">Where the owner says what it decided, or null.</param>
    /// <param name="edited">Told of each edit once it is saved, or null.</param>
    internal SessionEditor(
        ApprovalGate gate,
        Func<SessionLifetime, Vault?> unlockedFor,
        Func<SessionLifetime, Func<Vault, bool>, AgentSave> write,
        string clientsPath,
        TimeProvider clock,
        Action<string>? narrate,
        Action<EditDone>? edited)
    {
        _gate = gate;
        _unlockedFor = unlockedFor;
        _write = write;
        _clientsPath = clientsPath;
        _clock = clock;
        _narrate = narrate;
        _edited = edited;
    }

    /// <summary>Decides one edit: checks it, asks the person, then writes and saves it.</summary>
    /// <param name="request">The edit, already admitted to <paramref name="lifetime"/>.</param>
    /// <param name="lifetime">The unlock it was asked under.</param>
    /// <param name="waiting">The lifetime's <see cref="SessionLifetime.Waiting"/> as the request began, which a lock or a reload withdraws.</param>
    /// <param name="cancellationToken">Withdraws the question when the terminal stops waiting, the vault is locked or it is reloaded.</param>
    /// <returns>What became of it.</returns>
    internal async ValueTask<EditReply> DecideAsync(
        EditRequest request,
        SessionLifetime lifetime,
        CancellationToken waiting,
        CancellationToken cancellationToken)
    {
        var edit = request.Edit;

        if (!edit.TryValidate(out var invalid) || !edit.IsComplete)
        {
            return Refused(AuditMethod.InvalidRequest, invalid.Length > 0 ? invalid : "the edit is missing a value it writes", EditRefusalKind.Invalid);
        }

        if (_unlockedFor(lifetime) is not { } vault)
        {
            return Refused(AuditMethod.VaultLocked, "the vault is locked");
        }

        switch (vault.SaveState().Status)
        {
            case VaultSaveStatus.ChangedOnDisk:
                return Refused(AuditMethod.VaultChanged, "another program changed the vault since it was opened here; reload it there first, then try again");
            case VaultSaveStatus.Unsaved:
                return Refused(AuditMethod.VaultChanged, "the vault holds a change that is not saved yet; try again once it is");
            case VaultSaveStatus.Unreadable:
                return Refused(AuditMethod.Failed, "the vault's file could not be read to compare");
        }

        var scene = new EditScene(_clientsPath, _clock.GetUtcNow());
        TerminalEdit made;
        EditCheck check;

        try
        {
            made = TerminalEdits.Generate(edit);
            check = TerminalEdits.Check(vault, made, scene);
        }
        catch (Exception failure) when (failure is VaultException or ArgumentException)
        {
            return Refused(AuditMethod.InvalidRequest, failure.Message, EditRefusalKind.Conflict);
        }

        if (check.Refusal is { } refusal)
        {
            return Refused(AuditMethod.InvalidRequest, Said(refusal), refusal.Kind);
        }

        if (check.Unchanged is { } unchanged)
        {
            return new EditReply(AuditDecision.Denied, AuditMethod.Unchanged, "the vault already holds this; nothing was written") { Done = unchanged };
        }

        var preview = check.Preview!;
        var prompt = TerminalEditPrompt.For(edit, preview);
        var answer = await _gate.AskAsync([ApprovalGate.AskedForMore], prompt, cancellationToken).ConfigureAwait(false);

        if (!lifetime.IsLive || waiting.IsCancellationRequested)
        {
            return Withdrawn(lifetime);
        }

        if (!answer.Releases())
        {
            var why = answer switch
            {
                ApprovalAnswer.Denied => "a person refused this change",
                ApprovalAnswer.Cooldown => _gate.CoolingDown("make the change where the vault is unlocked"),
                ApprovalAnswer.TimedOut => "nobody answered inside the window",
                ApprovalAnswer.Busy => "another request is in front of the person; try again once it is answered",
                ApprovalAnswer.Cancelled => "the request was withdrawn before anybody answered",
                _ => "the prompt could not be shown, so nobody was asked",
            };

            _narrate?.Invoke($"did not change {prompt.Item}: {why}");
            return Refused(answer.ToAuditMethod(), why);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Refused(AuditMethod.Cancelled, "the request was withdrawn before anything was written");
        }

        if (!lifetime.TryCommit(waiting))
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
            result = TerminalEdits.Apply(open, made, scene, preview);
            return result.Done is { Outcome: not EditOutcome.Unchanged };
        });

        if (result?.Refusal is { } late)
        {
            TakeBack(written, checkpoint);
            return Refused(AuditMethod.VaultChanged, $"a person allowed it, and then: {Said(late)}", late.Kind);
        }

        if (!saved.Holds || result?.Done is not { } done)
        {
            TakeBack(written, checkpoint);
            _narrate?.Invoke($"did not change {prompt.Item}: {saved.Reason}");
            return Refused(MethodOf(saved), $"a person allowed it, and the vault was not saved: {saved.Reason}");
        }

        _narrate?.Invoke($"changed {prompt.Item} for keypaste {edit.Verb}");
        _edited?.Invoke(done);

        return new EditReply(AuditDecision.Granted, AuditMethod.Prompt, "a person allowed it, and the vault holds it") { Done = done };
    }

    /// <summary>The refusal of an edit a lock or a reload withdrew before it was saved (D-0412).</summary>
    /// <remarks>Told apart by the lifetime, since a reload replaces the wait before it cancels the one it replaced.</remarks>
    private static EditReply Withdrawn(SessionLifetime lifetime) =>
        lifetime.IsLive
            ? Refused(AuditMethod.VaultChanged, "the vault was reloaded from its file before this was saved")
            : Refused(AuditMethod.VaultLocked, "the vault was locked before this was saved");

    /// <summary>Takes an edit back out of the vault it was made in, unless the vault was locked meanwhile and took the edit with it.</summary>
    private static void TakeBack(Vault? vault, VaultCheckpoint? checkpoint)
    {
        if (vault is null || checkpoint is null)
        {
            return;
        }

        try
        {
            vault.Restore(checkpoint);
        }
        catch (ObjectDisposedException)
        {
            // Locked meanwhile: the open vault, and the edit with it, is gone.
        }
    }

    private static string Said(EditRefusal refusal) =>
        refusal.Then.Count == 0 ? refusal.Reason : $"{refusal.Reason}. {string.Join(" ", refusal.Then)}";

    private static AuditMethod MethodOf(AgentSave saved) => saved.Outcome switch
    {
        AgentSaveOutcome.Locked => AuditMethod.VaultLocked,
        AgentSaveOutcome.ChangedOnDisk => AuditMethod.VaultChanged,
        _ => AuditMethod.Failed,
    };

    private static EditReply Refused(AuditMethod method, string reason, EditRefusalKind? refusal = null) =>
        new(AuditDecision.Denied, method, reason) { Refusal = refusal };
}
