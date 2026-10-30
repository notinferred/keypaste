using System.Collections.Concurrent;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Ownership;

/// <summary>When each pairing was last proven to this owner, kept for the owner's life rather than in the vault, so a browser's use writes no file.</summary>
public sealed class PairingUses
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _uses = new(StringComparer.Ordinal);

    /// <summary>When the pairing was last paired or checked here, or null when it has not been since this owner started.</summary>
    /// <param name="vault">The vault's identity key.</param>
    /// <param name="pairing">The pairing's name.</param>
    /// <returns>The time, or null.</returns>
    public DateTimeOffset? LastUse(string vault, string pairing) =>
        _uses.TryGetValue(Key(vault, pairing), out var at) ? at : null;

    internal void Used(string vault, string pairing, DateTimeOffset at) => _uses[Key(vault, pairing)] = at;

    internal void Forget(string vault, string pairing) => _uses.TryRemove(Key(vault, pairing), out _);

    private static string Key(string vault, string pairing) => vault + "\0" + pairing;
}

/// <summary>A pairing as Settings lists it.</summary>
/// <param name="Pairing">What the vault records.</param>
/// <param name="LastUsed">When it was last paired or checked here since this owner started, or null.</param>
public sealed record PairingRow(BrowserPairing Pairing, DateTimeOffset? LastUsed);

/// <summary>What removing a pairing came to.</summary>
/// <param name="Removed">Whether the vault's file no longer holds it.</param>
/// <param name="Reason">keypaste's own words when it was not removed, otherwise empty.</param>
public sealed record PairingRemoval(bool Removed, string Reason);

/// <summary>
/// Pairs a browser with this owner's unlocked vault while the person has armed pairing, and checks the pairings the extension
/// presents, writing a pairing only after the person confirms it and saving it under the owner's claim (D-0633 to D-0635).
/// </summary>
/// <remarks>
/// <para>
/// Pressing Pair a browser arms pairing for <see cref="ArmedFor"/> and one confirmation, in this unlock only. A request to pair
/// while nothing is armed is refused before anybody is asked, recorded, and told to the person, so a program that starts
/// keypaste claiming to be the extension cannot put an unsolicited confirmation in front of them (THREATS.md T-48). Armed, the
/// confirmation names the build the browser named and the pairing's name, through the owner's own gate, its allow disarmed
/// for a second (D-0456).
/// </para>
/// <para>
/// A check is proven only against the vault as its file holds it. Each pairing granted, denied or refused while unarmed, each
/// check refused and each removal is recorded on a browser line naming the build and the pairing and never a key (D-0637); a
/// check that proves its pairing and the database hash release nothing and are not.
/// </para>
/// </remarks>
public sealed class SessionBrowsers : IBrowserRequests
{
    /// <summary>How long pairing stays armed once the person presses Pair a browser.</summary>
    public static readonly TimeSpan ArmedFor = TimeSpan.FromMinutes(2);

    private readonly VaultIdentity _vault;
    private readonly SessionLifetime _lifetime;
    private readonly ApprovalGate _gate;
    private readonly Func<SessionLifetime, Vault?> _unlockedFor;
    private readonly Func<SessionLifetime, Func<Vault, bool>, AgentSave> _write;
    private readonly RequestRecorder _recorder;
    private readonly PairingUses _uses;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;
    private readonly Lock _arming = new();
    private DateTimeOffset? _armedUntil;
    private string? _armedName;

    /// <summary>Builds the pairing of one unlock.</summary>
    /// <param name="vault">The vault this owner holds.</param>
    /// <param name="lifetime">The unlock this pairs for, which ends the arming with it.</param>
    /// <param name="gate">Where the person is asked, under the same slot and window as agents.</param>
    /// <param name="unlockedFor">The vault while a lifetime is live, otherwise null.</param>
    /// <param name="write">Runs an edit on the lifetime's vault and saves it under the owner's claim.</param>
    /// <param name="audit">Opens the owner's log, as the authority's does.</param>
    /// <param name="uses">When each pairing was last proven, kept for the owner's life.</param>
    /// <param name="clock">What time it is.</param>
    /// <param name="narrate">Where the owner says what it decided, or null.</param>
    internal SessionBrowsers(
        VaultIdentity vault,
        SessionLifetime lifetime,
        ApprovalGate gate,
        Func<SessionLifetime, Vault?> unlockedFor,
        Func<SessionLifetime, Func<Vault, bool>, AgentSave> write,
        Func<AuditLog?> audit,
        PairingUses uses,
        TimeProvider clock,
        Action<string>? narrate)
    {
        _vault = vault;
        _lifetime = lifetime;
        _gate = gate;
        _unlockedFor = unlockedFor;
        _write = write;
        _recorder = new RequestRecorder(audit);
        _uses = uses;
        _clock = clock;
        _narrate = narrate;
    }

    /// <summary>Raised, on whichever thread removed it, once a pairing is removed and saved, naming it.</summary>
    public event EventHandler<string>? Removed;

    /// <summary>Until when pairing is armed, or null when it is not: never armed, spent by a confirmation, past its two minutes or disarmed.</summary>
    public DateTimeOffset? ArmedUntil
    {
        get
        {
            lock (_arming)
            {
                return _armedUntil is { } until && _clock.GetUtcNow() < until && _lifetime.IsLive ? until : null;
            }
        }
    }

    /// <summary>Arms pairing for <see cref="ArmedFor"/> and one confirmation, for the person's own press and nothing on a pipe.</summary>
    /// <param name="name">The name the person chose for the pairing, or null to offer one in the confirmation.</param>
    public void Arm(string? name = null)
    {
        lock (_arming)
        {
            _armedUntil = _clock.GetUtcNow() + ArmedFor;
            _armedName = name;
        }

        _narrate?.Invoke($"pairing a browser is armed for {ApprovalLimits.Describe((int)ArmedFor.TotalSeconds)}");
    }

    /// <summary>Ends the arming before its two minutes are up.</summary>
    public void Disarm()
    {
        lock (_arming)
        {
            _armedUntil = null;
            _armedName = null;
        }
    }

    /// <summary>The pairings the open vault holds, with when each was last proven here.</summary>
    /// <returns>The rows, by name; none while locked.</returns>
    public IReadOnlyList<PairingRow> List()
    {
        try
        {
            return _unlockedFor(_lifetime) is { } vault
                ? [.. new BrowserPairings(vault).List().Select(pairing => new PairingRow(pairing, _uses.LastUse(_vault.Key, pairing.Id)))]
                : [];
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Removes a pairing for the person, saving the vault and recording it, so the extension holding it is refused from its next check.</summary>
    /// <param name="id">The pairing's name.</param>
    /// <returns>Whether it was removed, and why not.</returns>
    public PairingRemoval Remove(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!_lifetime.IsLive || _unlockedFor(_lifetime) is null)
        {
            return new PairingRemoval(false, "the vault is locked");
        }

        Vault? written = null;
        VaultCheckpoint? checkpoint = null;
        BrowserPairing? removed = null;
        var problem = "no pairing has this name any more";

        var saved = _write(_lifetime, open =>
        {
            written = open;
            checkpoint = open.Checkpoint();

            try
            {
                return new BrowserPairings(open).Remove(id, out removed);
            }
            catch (VaultException)
            {
                problem = "two pairings share this name; remove one in KeePassXC";
                return false;
            }
        });

        if (removed is null)
        {
            return new PairingRemoval(false, problem);
        }

        if (!saved.Holds)
        {
            TakeBack(written, checkpoint);
            return new PairingRemoval(false, $"the vault was not saved: {saved.Reason}");
        }

        _uses.Forget(_vault.Key, id);
        Record(removed.Identity, id, BrowserAct.Remove, AuditDecision.Granted, AuditMethod.Removed, "the person removed the pairing in keypaste");
        _narrate?.Invoke($"removed the pairing {id}");
        Removed?.Invoke(this, id);
        return new PairingRemoval(true, string.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>SHA-256 of <c>keypaste</c>, a line feed and the root group's UUID, so the extension files keypaste's pairing apart from KeePassXC's (D-0636).</remarks>
    public string? DatabaseHash()
    {
        try
        {
            return _unlockedFor(_lifetime) is { } vault ? HashOf(vault.RootGroupHex) : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The database hash keypaste answers for a vault whose root group has this UUID in hex.</summary>
    /// <param name="rootGroupHex">The root group's 16 bytes in lowercase hex.</param>
    /// <returns>The hash in lowercase hex.</returns>
    public static string HashOf(string rootGroupHex)
    {
        ArgumentNullException.ThrowIfNull(rootGroupHex);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("keypaste\n" + rootGroupHex)));
    }

    /// <inheritdoc/>
    public async ValueTask<BrowserReply> PairAsync(BrowserPairRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        var waiting = _lifetime.Waiting;

        if (_unlockedFor(_lifetime) is not { } vault)
        {
            return new BrowserReply(BrowserOutcome.Locked, "the vault is locked");
        }

        if (!_recorder.Opens)
        {
            return new BrowserReply(BrowserOutcome.Refused, RequestRecorder.Unopened);
        }

        if (!TryTakeArming(out var armedName, out var until))
        {
            Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.NotArmed, "a browser asked to pair while pairing was not armed in keypaste");
            _gate.Tell(new PairingNotice(browser));
            _narrate?.Invoke($"refused {browser.Described} pairing: pairing was not armed");
            return new BrowserReply(BrowserOutcome.Refused, "pairing is not armed; press Pair a browser in keypaste, then connect again");
        }

        List<string> taken;

        try
        {
            taken = [.. new BrowserPairings(vault).List().Select(pairing => pairing.Id)];
        }
        catch (ObjectDisposedException)
        {
            return new BrowserReply(BrowserOutcome.Locked, "the vault is locked");
        }

        var prompt = new PairingPrompt
        {
            Identity = browser,
            SuggestedName = armedName ?? PairingNames.Suggested(browser, Environment.MachineName, taken),
            TakenNames = taken,
            NameChosen = armedName is not null,
        };

        if (armedName is not null && prompt.NameProblem(armedName) is { } invalid)
        {
            Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.InvalidRequest, $"the name chosen for the pairing cannot be used: {invalid}");
            return new BrowserReply(BrowserOutcome.Refused, $"the name chosen for the pairing cannot be used: {invalid}");
        }

        var answer = await _gate.AskAsync(prompt, cancellationToken).ConfigureAwait(false);

        if (!_lifetime.IsLive || waiting.IsCancellationRequested)
        {
            var withdrawn = _lifetime.IsLive ? $"{_lifetime.WithdrawnBecause} before the pairing was saved" : "the vault was locked before the pairing was saved";
            Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, _lifetime.IsLive ? AuditMethod.VaultChanged : AuditMethod.VaultLocked, withdrawn);
            return new BrowserReply(_lifetime.IsLive ? BrowserOutcome.Refused : BrowserOutcome.Locked, withdrawn);
        }

        if (answer is not { Answer: ApprovalAnswer.ApprovedOnce, Name: { } name })
        {
            // Nobody was shown it, so the arming the request took still stands.
            if (answer.Answer is ApprovalAnswer.Busy or ApprovalAnswer.NoChannel or ApprovalAnswer.Failed)
            {
                GiveBack(until, armedName);
            }

            var why = answer.Answer switch
            {
                ApprovalAnswer.Denied => "a person refused to pair this browser",
                ApprovalAnswer.TimedOut => "nobody answered inside the window",
                ApprovalAnswer.Busy => "another request is in front of the person; connect again once it is answered",
                ApprovalAnswer.Cancelled => "the request was withdrawn before anybody answered",
                _ => "the confirmation could not be shown, so nobody was asked",
            };

            Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, answer.Answer.ToAuditMethod(), why);
            _narrate?.Invoke($"did not pair {browser.Described}: {why}");
            return new BrowserReply(BrowserOutcome.Refused, why);
        }

        if (!_lifetime.TryCommitAfter(waiting))
        {
            Record(browser, name, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.VaultLocked, "the vault was locked before the pairing was saved");
            return new BrowserReply(BrowserOutcome.Locked, "the vault was locked before the pairing was saved");
        }

        Vault? written = null;
        VaultCheckpoint? checkpoint = null;
        var problem = string.Empty;
        var now = _clock.GetUtcNow();

        var saved = _write(_lifetime, open =>
        {
            written = open;
            checkpoint = open.Checkpoint();
            return new BrowserPairings(open).TryAdd(name, browser, request.IdentificationKey, now, out problem);
        });

        if (problem.Length > 0 || !saved.Holds)
        {
            TakeBack(written, checkpoint);
            var why = problem.Length > 0 ? $"a person allowed it, and then: {problem}" : $"a person allowed it, and the vault was not saved: {saved.Reason}";
            Record(browser, name, BrowserAct.Pair, AuditDecision.Denied, problem.Length > 0 ? AuditMethod.InvalidRequest : AuditMethod.Failed, why);
            return new BrowserReply(BrowserOutcome.Refused, why);
        }

        _uses.Used(_vault.Key, name, now);
        Record(browser, name, BrowserAct.Pair, AuditDecision.Granted, AuditMethod.Prompt, "a person allowed it, and the vault holds the pairing");
        _narrate?.Invoke($"paired {browser.Described} as {name}");
        return new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = name };
    }

    /// <inheritdoc/>
    public ValueTask<BrowserReply> CheckAsync(BrowserCheckRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        PairingCheck check;

        try
        {
            if (_unlockedFor(_lifetime) is not { } vault)
            {
                return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Locked, "the vault is locked"));
            }

            check = new BrowserPairings(vault).Verify(request.Pairing, browser, request.IdentificationKey);
        }
        catch (ObjectDisposedException)
        {
            return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Locked, "the vault is locked"));
        }

        if (check == PairingCheck.Valid)
        {
            _uses.Used(_vault.Key, request.Pairing, _clock.GetUtcNow());
            return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = request.Pairing });
        }

        var (method, why) = check switch
        {
            PairingCheck.Unsaved => (AuditMethod.VaultChanged, "the vault holds a change not saved yet, so no pairing is proven until it is"),
            PairingCheck.ChangedOnDisk => (AuditMethod.VaultChanged, "another program changed the vault since it was opened here; reload it first"),
            PairingCheck.Unreadable => (AuditMethod.Failed, "the vault's file could not be read to compare"),
            _ => (AuditMethod.NotPaired, "the vault holds no pairing of that name for this browser and key"),
        };

        Record(browser, request.Pairing, BrowserAct.Check, AuditDecision.Denied, method, why);
        return ValueTask.FromResult(new BrowserReply(check == PairingCheck.Unknown ? BrowserOutcome.NotPaired : BrowserOutcome.Refused, why));
    }

    /// <inheritdoc/>
    /// <remarks>Filling comes with 8.3a2; until then a fill on a pairing the check proved is refused, unrecorded.</remarks>
    public ValueTask<BrowserFillReply> FillAsync(BrowserFillRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new BrowserFillReply(BrowserOutcome.Refused, "this keypaste pairs browsers and does not fill logins in them yet"));

    private bool TryTakeArming(out string? name, out DateTimeOffset until)
    {
        lock (_arming)
        {
            name = _armedName;
            until = _armedUntil ?? default;

            if (_armedUntil is not { } armed || _clock.GetUtcNow() >= armed)
            {
                _armedUntil = null;
                _armedName = null;
                return false;
            }

            _armedUntil = null;
            _armedName = null;
            return true;
        }
    }

    private void GiveBack(DateTimeOffset until, string? name)
    {
        lock (_arming)
        {
            if (_armedUntil is null && _clock.GetUtcNow() < until)
            {
                _armedUntil = until;
                _armedName = name;
            }
        }
    }

    private void Record(BrowserIdentity browser, string? pairing, BrowserAct act, AuditDecision decision, AuditMethod method, string reason) =>
        _recorder.Recorded(RequestRecords.Browser(browser, pairing, act, decision, method, reason, _lifetime.Id, _vault.Key));

    /// <summary>Takes a write back out of the vault it was made in, unless the vault was locked meanwhile and took it with it.</summary>
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
            // Locked meanwhile: the open vault, and the write with it, is gone.
        }
    }
}
