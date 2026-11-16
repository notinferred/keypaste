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

/// <summary>An "Always fill" allowance as Settings lists it.</summary>
/// <param name="Allowance">The allowance.</param>
/// <param name="Item">Its item's title, sanitized, or words saying the vault no longer holds it.</param>
public sealed record AllowanceRow(BrowserAllowance Allowance, string Item);

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
    private readonly IAgentRules _rules;
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
    /// <param name="rules">This computer's sealed rules, which keep the person's browser allowances.</param>
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
        IAgentRules rules,
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
        _rules = rules;
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

        // Tidying only: an allowance applies only while its pairing exists, so one left behind here releases nothing.
        if (_rules.TryRead(out var rules, out _) && rules.BrowserAllowances.Any(allowance => Owns(removed, allowance)))
        {
            _rules.TryChange(held => held.WithoutPairing(removed.Id, removed.Created), out _);
        }

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
                ApprovalAnswer.Busy => "another prompt is waiting for an answer; connect again once it is answered",
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

        var proof = Prove(request.Pairing, request.IdentificationKey, browser);

        return ValueTask.FromResult(proof.Pairing is not null
            ? new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = request.Pairing }
            : new BrowserReply(proof.Outcome, proof.Reason));
    }

    /// <summary>The allowances of each pairing the vault holds now, as Settings lists them, each with its item's title.</summary>
    /// <returns>Each allowance, oldest first; none while the rules are ignored or the vault is locked.</returns>
    public IReadOnlyList<AllowanceRow> Allowances()
    {
        try
        {
            if (_unlockedFor(_lifetime) is not { } vault || !_rules.TryRead(out var rules, out _))
            {
                return [];
            }

            var pairings = new BrowserPairings(vault).List();
            var entries = vault.ReadEntries();

            return
            [
                .. rules.BrowserAllowances
                    .Where(allowance => pairings.Any(pairing => Owns(pairing, allowance)))
                    .Select(allowance => new AllowanceRow(
                        allowance,
                        entries.FirstOrDefault(entry => string.Equals(entry.Uuid, allowance.Item, StringComparison.OrdinalIgnoreCase)) is { } entry
                            ? EntryNameSanitizer.Sanitize(entry.Title).Text
                            : "an item no longer in the vault")),
            ];
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Removes one "Always fill" allowance for the person, so the next fill it covered is asked again.</summary>
    /// <param name="allowance">The allowance.</param>
    /// <param name="problem">Why it was not removed, or empty.</param>
    /// <returns>Whether it was removed.</returns>
    public bool Unallow(BrowserAllowance allowance, out string problem)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        if (!_rules.TryChange(rules => rules.ForgettingFill(allowance), out problem))
        {
            return false;
        }

        if (_unlockedFor(_lifetime) is { } vault && new BrowserPairings(vault).List().FirstOrDefault(pairing => Owns(pairing, allowance)) is { } owner)
        {
            var line = RequestRecords.Browser(owner.Identity, owner.Id, BrowserAct.Unallow, AuditDecision.Granted, AuditMethod.Removed, $"the person removed always filling an item on {allowance.Origin}", _lifetime.Id, _vault.Key);
            _recorder.Recorded(line with { Args = line.Args with { Item = allowance.Item } });
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A page's own request releases only the items the person always fills on its exact origin and asks nobody. A request the
    /// extension says the person made also puts any other matching item to them, one prompt at a time, and fills the one they
    /// choose. Each released item and, for a request the person made, each matched item kept out, gets its own line, all written
    /// before the reply; a lookup that matched nothing writes none.
    /// </para>
    /// <para>
    /// After the person answers, the pairing is proven again and the chosen item read again by its UUID through the same rules,
    /// so a pairing removed, or an item recycled, hidden or edited out of the page, while they were asked releases nothing.
    /// </para>
    /// </remarks>
    public async ValueTask<BrowserFillReply> FillAsync(BrowserFillRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        var waiting = _lifetime.Waiting;

        if (_unlockedFor(_lifetime) is null)
        {
            return new BrowserFillReply(BrowserOutcome.Locked, "the vault is locked");
        }

        if (!_recorder.Opens)
        {
            return new BrowserFillReply(BrowserOutcome.Refused, RequestRecorder.Unopened);
        }

        if (!LoginLookup.TryRead(request.Url, request.SubmitUrl, httpAuthentication: false, out var lookup, out _))
        {
            return NoLogins;
        }

        var proof = Prove(request.Pairing, request.IdentificationKey, browser);

        if (proof.Pairing is not { } pairing)
        {
            return new BrowserFillReply(proof.Outcome, proof.Reason);
        }

        var origin = lookup.Page.ToString();

        if (Match(lookup, out var read) is { } unread)
        {
            return Refuse(browser, pairing.Id, unread.Method, unread.Reason, origin);
        }

        var rules = _rules.TryRead(out var held, out _) ? held : AgentRules.Defaults;
        var allowed = lookup.AsksEveryTime
            ? []
            : read.Candidates.Where(item => rules.AllowsFill(pairing.Id, pairing.Created, origin, item.Name.Uuid!)).ToList();
        var toAsk = read.Candidates.Except(allowed).ToList();
        List<AuditRecord> lines = [];

        if (request.PersonAsked)
        {
            lines.AddRange(read.Refused.Select(refused => FillLine(browser, pairing.Id, refused.Item.Name, origin, AuditDecision.Denied, AuditMethod.Blocked, refused.Reason)));
        }

        FillAnswer answer = default;

        if (request.PersonAsked && toAsk.Count > 0)
        {
            var prompt = new FillPrompt
            {
                Identity = browser,
                Pairing = pairing.Id,
                Origin = origin,
                Site = lookup.Page.Site ?? lookup.Page.Host,
                FormOrigin = lookup.OtherSiteForm?.ToString(),
                Candidates = [.. toAsk.Take(FillPrompt.MaximumCandidates).Select(Listed)],
                OffersAlways = !lookup.AsksEveryTime && rules.HasBrowserRoom,
            };

            answer = await _gate.AskAsync($"browser\0{pairing.Id}\0{origin}", prompt, cancellationToken).ConfigureAwait(false);

            if (!_lifetime.IsLive || waiting.IsCancellationRequested)
            {
                var withdrawn = _lifetime.IsLive ? $"{_lifetime.WithdrawnBecause} before the login was filled" : "the vault was locked before the login was filled";
                return Refuse(browser, pairing.Id, _lifetime.IsLive ? AuditMethod.VaultChanged : AuditMethod.VaultLocked, withdrawn, origin, _lifetime.IsLive ? BrowserOutcome.Refused : BrowserOutcome.Locked);
            }

            if (answer.Answer != ApprovalAnswer.ApprovedOnce)
            {
                lines.Add(FillLine(browser, pairing.Id, null, origin, AuditDecision.Denied, answer.Answer.ToAuditMethod(), Unasked(answer.Answer)));
            }

            // Proven again, and the page's items read again, since the person may have removed the pairing or changed an item while asked.
            proof = Prove(request.Pairing, request.IdentificationKey, browser);

            if (proof.Pairing is null)
            {
                return new BrowserFillReply(proof.Outcome, proof.Reason);
            }

            if (Match(lookup, out read) is { } reread)
            {
                return Refuse(browser, pairing.Id, reread.Method, reread.Reason, origin);
            }
        }

        var chosen = answer.Answer == ApprovalAnswer.ApprovedOnce ? answer.Uuid : null;
        var releasing = read.Candidates
            .Where(item => allowed.Any(was => SameItem(was, item)) || string.Equals(item.Name.Uuid, chosen, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (releasing.Count == 0)
        {
            var refused = answer.Answer is ApprovalAnswer.Denied or ApprovalAnswer.TimedOut or ApprovalAnswer.Cooldown;
            return _recorder.Recorded(refused ? new BrowserFillReply(BrowserOutcome.Refused, Unasked(answer.Answer)) : NoLogins, lines);
        }

        if (_unlockedFor(_lifetime) is not { } vault)
        {
            return new BrowserFillReply(BrowserOutcome.Locked, "the vault is locked");
        }

        List<BrowserLogin> logins = [];
        List<(BrowserItem Item, bool Asked)> released = [];

        foreach (var item in releasing)
        {
            var wasAsked = string.Equals(item.Name.Uuid, chosen, StringComparison.OrdinalIgnoreCase);

            if (vault.ReadSavedLogin(item.Name.Uuid!, out var login) != SavedRead.Current || login is null)
            {
                lines.Add(FillLine(browser, pairing.Id, item.Name, origin, AuditDecision.Denied, AuditMethod.VaultChanged, "the item could not be read as the vault's file holds it"));
                continue;
            }

            if (login.Username.Value is not { } username || login.Password.Value is not { } password)
            {
                var why = $"its {(login.Username.Value is null ? "username" : "password")} {login.Username.Refusal ?? login.Password.Refusal}";
                lines.Add(FillLine(browser, pairing.Id, item.Name, origin, AuditDecision.Denied, AuditMethod.Unfillable, why));
                continue;
            }

            logins.Add(new BrowserLogin(username, password, login.Entry.Title, item.Name.Uuid!));
            released.Add((item, wasAsked));
        }

        var reply = logins.Count > 0 ? new BrowserFillReply(BrowserOutcome.Done, string.Empty) { Logins = logins } : NoLogins;

        if (logins.Count > 0 && !ApproverProtocol.Fits(reply))
        {
            return Refuse(browser, pairing.Id, AuditMethod.Undeliverable, "the logins would not fit one reply", origin);
        }

        if (logins.Count > 0 && !_lifetime.TryCommitAfter(waiting))
        {
            return Refuse(browser, pairing.Id, AuditMethod.VaultLocked, "the vault was locked before the login was filled", origin, BrowserOutcome.Locked);
        }

        var kept = answer.Always && chosen is not null && released.Any(pair => pair.Asked) ? KeepAlways(pairing, origin, chosen) : null;

        foreach (var (item, wasAsked) in released)
        {
            var why = !wasAsked ? "filled without asking, as the person always fills it here"
                : kept is null ? "a person chose it"
                : kept.Length == 0 ? "a person chose it and will always fill it here, until they remove that in Settings"
                : $"a person chose it; always filling it here could not be kept: {kept}";

            lines.Add(FillLine(browser, pairing.Id, item.Name, origin, AuditDecision.Granted, wasAsked ? AuditMethod.Prompt : AuditMethod.Allowed, why));
        }

        _uses.Used(_vault.Key, pairing.Id, _clock.GetUtcNow());
        _narrate?.Invoke($"filled {released.Count} login(s) on {origin} for {pairing.Id}");
        return _recorder.Recorded(reply, lines);
    }

    private static BrowserFillReply NoLogins => new(BrowserOutcome.Done, string.Empty);

    private static bool Owns(BrowserPairing pairing, BrowserAllowance allowance) =>
        string.Equals(pairing.Id, allowance.Pairing, StringComparison.Ordinal) && pairing.Created == allowance.Paired;

    private static bool SameItem(BrowserItem left, BrowserItem right) =>
        string.Equals(left.Name.Uuid, right.Name.Uuid, StringComparison.OrdinalIgnoreCase);

    private static FillCandidate Listed(BrowserItem item) => new(
        item.Name.Uuid!,
        EntryNameSanitizer.Sanitize(item.Name.Title).Text,
        item.Username.Length == 0 ? string.Empty : EntryNameSanitizer.Sanitize(item.Username).Text,
        EntryNameSanitizer.SanitizePath(item.Name.GroupPath).Text);

    private static string Unasked(ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => "a person refused to fill a login here",
        ApprovalAnswer.TimedOut => "nobody answered inside the window",
        ApprovalAnswer.Cooldown => "a fill here was refused or went unanswered a moment ago, so nobody was asked",
        ApprovalAnswer.Busy => "another prompt is waiting for an answer, so nobody was asked",
        ApprovalAnswer.Cancelled => "the request was withdrawn before anybody answered",
        _ => "the prompt could not be shown, so nobody was asked",
    };

    /// <summary>Keeps the person's "Always fill", and says why not when it could not be kept.</summary>
    /// <returns>Empty when kept, otherwise why not.</returns>
    private string KeepAlways(BrowserPairing pairing, string origin, string item)
    {
        var allowance = new BrowserAllowance(pairing.Id, pairing.Created, origin, item);

        try
        {
            return _rules.TryChange(rules => rules.HasBrowserRoom || rules.AllowsFill(pairing.Id, pairing.Created, origin, item) ? rules.AllowingFill(allowance) : rules, out var problem)
                ? string.Empty
                : problem;
        }
        catch (InvalidOperationException full)
        {
            return full.Message;
        }
    }

    /// <summary>The page's matching items and those kept out, as the vault's file holds them, or why they cannot be read.</summary>
    private (AuditMethod Method, string Reason)? Match(LoginLookup lookup, out (IReadOnlyList<BrowserItem> Candidates, IReadOnlyList<RefusedMatch> Refused) read)
    {
        read = ([], []);

        try
        {
            if (_unlockedFor(_lifetime) is not { } vault)
            {
                return (AuditMethod.VaultLocked, "the vault is locked");
            }

            switch (vault.ReadSavedBrowserItems(out var items))
            {
                case SavedRead.Current:
                    read = LoginCandidates.Of(lookup, items!, _clock.GetUtcNow());
                    return null;
                case SavedRead.Unreadable:
                    return (AuditMethod.Failed, "the vault's file could not be read to compare");
                default:
                    return (AuditMethod.VaultChanged, "the vault differs from its file, so no login is filled until keypaste saves, merges or reloads it");
            }
        }
        catch (ObjectDisposedException)
        {
            return (AuditMethod.VaultLocked, "the vault is locked");
        }
    }

    /// <summary>Proves a pairing against the vault as its file holds it, recording a refusal.</summary>
    private (BrowserPairing? Pairing, BrowserOutcome Outcome, string Reason) Prove(string id, string identificationKey, BrowserIdentity browser)
    {
        PairingCheck check;
        BrowserPairing? pairing;

        try
        {
            if (_unlockedFor(_lifetime) is not { } vault)
            {
                return (null, BrowserOutcome.Locked, "the vault is locked");
            }

            var pairings = new BrowserPairings(vault);
            check = pairings.Verify(id, browser, identificationKey);
            pairing = check == PairingCheck.Valid ? pairings.List().FirstOrDefault(listed => string.Equals(listed.Id, id, StringComparison.Ordinal)) : null;
        }
        catch (ObjectDisposedException)
        {
            return (null, BrowserOutcome.Locked, "the vault is locked");
        }

        if (pairing is not null)
        {
            _uses.Used(_vault.Key, id, _clock.GetUtcNow());
            return (pairing, BrowserOutcome.Done, string.Empty);
        }

        var (method, why) = check switch
        {
            PairingCheck.Unsaved => (AuditMethod.VaultChanged, "the vault holds a change not saved yet, so no pairing is proven until it is"),
            PairingCheck.ChangedOnDisk => (AuditMethod.VaultChanged, "another program changed the vault since it was opened here; no pairing is proven until keypaste merges that save or a person reloads the vault"),
            PairingCheck.Unreadable => (AuditMethod.Failed, "the vault's file could not be read to compare"),
            _ => (AuditMethod.NotPaired, "the vault holds no pairing of that name for this browser and key"),
        };

        Record(browser, id, BrowserAct.Check, AuditDecision.Denied, method, why);
        return (null, check is PairingCheck.Unknown or PairingCheck.Valid ? BrowserOutcome.NotPaired : BrowserOutcome.Refused, why);
    }

    private AuditRecord FillLine(BrowserIdentity browser, string pairing, EntryName? item, string origin, AuditDecision decision, AuditMethod method, string reason) =>
        RequestRecords.BrowserFill(browser, pairing, item, origin, decision, method, reason, _lifetime.Id, _vault.Key);

    /// <summary>Refuses a fill whole, recording one line that names no item.</summary>
    private BrowserFillReply Refuse(BrowserIdentity browser, string pairing, AuditMethod method, string reason, string origin, BrowserOutcome outcome = BrowserOutcome.Refused)
    {
        _recorder.Recorded(FillLine(browser, pairing, null, origin, AuditDecision.Denied, method, reason));
        return new BrowserFillReply(outcome, reason);
    }

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
