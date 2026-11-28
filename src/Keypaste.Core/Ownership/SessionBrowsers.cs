using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Keypaste.Core.Totp;

namespace Keypaste.Core.Ownership;

/// <summary>
/// This computer's browser pairings as one owner process serves them: the pairings file, the vaults unlocked in the process,
/// which vouch for them together and whose logins a page's list spans, and what the process remembers rather than writes: when
/// each pairing was last proven, the one pairing request it is asking about and how long a refused pairing holds the next one back.
/// </summary>
/// <remarks>
/// Only one request to pair is asked about at a time, and a refusal or no answer holds every request back for a minute, then two,
/// four and on up to an hour each time it happens again, starting from a minute once an hour passes without one, so a program
/// that keeps asking cannot keep a confirmation in front of the person (THREATS.md T-48).
/// </remarks>
/// <param name="file">This computer's pairings file.</param>
/// <param name="unlocked">The vaults unlocked in this process now, or null for an owner that holds one vault alone.</param>
/// <param name="locked">The names of the vaults open in this process and locked now, or null for an owner that holds one vault alone.</param>
public sealed class OwnerPairings(BrowserPairings file, Func<IReadOnlyList<PairingVault>>? unlocked = null, Func<IReadOnlyList<string>>? locked = null)
{
    /// <summary>How long the first refusal holds the next request to pair back.</summary>
    public static readonly TimeSpan FirstHoldBack = TimeSpan.FromMinutes(1);

    /// <summary>The longest a refusal holds the next request to pair back.</summary>
    public static readonly TimeSpan LongestHoldBack = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _uses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContinuedFill> _continued = new(StringComparer.Ordinal);
    private readonly Func<IReadOnlyList<PairingVault>> _unlocked = unlocked ?? (() => []);
    private readonly Func<IReadOnlyList<string>> _locked = locked ?? (() => []);
    private readonly List<SessionBrowsers> _serving = [];
    private readonly Lock _gate = new();
    private bool _asking;
    private int _refusals;
    private DateTimeOffset _refusedAt;

    /// <summary>Raised, on whichever thread made it, once a pairing is made or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>This computer's pairings file.</summary>
    public BrowserPairings File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    /// <summary>
    /// Brings keypaste forward for what the person pressed in a page's list, on whichever thread asked: what was pressed, the
    /// vault the list named, a locked one for an unlock or the site's for a new login, and what a new login starts with;
    /// null in an owner with no window, which refuses it.
    /// </summary>
    public Action<BrowserShow, string, NewLoginPage>? Show { get; set; }

    /// <summary>When the pairing was last paired or checked here, or null when it has not been since this process started.</summary>
    /// <param name="pairing">The pairing's name.</param>
    /// <returns>The time, or null.</returns>
    public DateTimeOffset? LastUse(string pairing) => _uses.TryGetValue(pairing, out var at) ? at : null;

    /// <summary>The vaults unlocked in this process now.</summary>
    internal IReadOnlyList<PairingVault> Unlocked() => _unlocked();

    /// <summary>The names of the vaults open in this process and locked now.</summary>
    internal IReadOnlyList<string> Locked() => _locked();

    /// <summary>Each unlock's browsers serving now, in the order they started.</summary>
    internal IReadOnlyList<SessionBrowsers> Serving()
    {
        lock (_gate)
        {
            return [.. _serving];
        }
    }

    /// <summary>Serves one unlock's browsers until its lifetime ends, so a page's list spans its vault; a lock continues no fill.</summary>
    internal void Serve(SessionBrowsers browsers, CancellationToken ended)
    {
        lock (_gate)
        {
            _serving.Add(browsers);
        }

        ended.Register(() =>
        {
            lock (_gate)
            {
                _serving.Remove(browsers);
                _continued.Clear();
            }
        });
    }

    internal void Used(string pairing, DateTimeOffset at) => _uses[pairing] = at;

    internal void Forget(string pairing)
    {
        _uses.TryRemove(pairing, out _);

        lock (_gate)
        {
            _continued.Remove(pairing);
        }
    }

    /// <summary>What the last fill for a pairing may still be continued by, or null.</summary>
    internal ContinuedFill? Continued(string pairing)
    {
        lock (_gate)
        {
            return _continued.GetValueOrDefault(pairing);
        }
    }

    /// <summary>Has a fill for a pairing continue as <paramref name="next"/> says, or nothing continue it when that is null, replacing what continued before.</summary>
    internal void Continue(string pairing, ContinuedFill? next)
    {
        lock (_gate)
        {
            if (next is null)
            {
                _continued.Remove(pairing);
            }
            else
            {
                _continued[pairing] = next;
            }
        }
    }

    /// <summary>Takes the continuation a fill was checked against, once, putting <paramref name="next"/> in its place; false when another fill took it first.</summary>
    internal bool TryTake(ContinuedFill taken, ContinuedFill? next)
    {
        lock (_gate)
        {
            if (!_continued.TryGetValue(taken.Pairing, out var held) || !ReferenceEquals(held, taken))
            {
                return false;
            }

            if (next is null)
            {
                _continued.Remove(taken.Pairing);
            }
            else
            {
                _continued[taken.Pairing] = next;
            }

            return true;
        }
    }

    internal void Change() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Takes the one pairing request asked about at a time, or says how long a refusal still holds requests back.</summary>
    /// <param name="now">The time now.</param>
    /// <param name="holdBack">How long a refusal still holds requests back, or zero when another request holds the turn.</param>
    /// <returns>Whether this request has the turn, which <see cref="EndTurn"/> gives back.</returns>
    internal bool TryTakeTurn(DateTimeOffset now, out TimeSpan holdBack)
    {
        lock (_gate)
        {
            holdBack = _refusals == 0 ? TimeSpan.Zero : _refusedAt + HoldBack(_refusals) - now;

            if (holdBack > TimeSpan.Zero)
            {
                return false;
            }

            holdBack = TimeSpan.Zero;

            if (_asking)
            {
                return false;
            }

            _asking = true;
            return true;
        }
    }

    internal void EndTurn()
    {
        lock (_gate)
        {
            _asking = false;
        }
    }

    /// <summary>Counts a refusal or a confirmation nobody answered, which holds the next request back.</summary>
    internal void Refused(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_refusals > 0 && now - (_refusedAt + HoldBack(_refusals)) >= LongestHoldBack)
            {
                _refusals = 0;
            }

            _refusals++;
            _refusedAt = now;
        }
    }

    /// <summary>Forgets the refusals once a pairing is made.</summary>
    internal void Paired()
    {
        lock (_gate)
        {
            _refusals = 0;
        }
    }

    private static TimeSpan HoldBack(int refusals) =>
        refusals > 7 ? LongestHoldBack : TimeSpan.FromTicks(Math.Min(FirstHoldBack.Ticks << (refusals - 1), LongestHoldBack.Ticks));
}

/// <summary>A pairing as Settings lists it.</summary>
/// <param name="Pairing">What this computer records.</param>
/// <param name="LastUsed">When it was last paired or checked since this process started, or null.</param>
public sealed record PairingRow(BrowserPairing Pairing, DateTimeOffset? LastUsed);

/// <summary>What removing a pairing came to.</summary>
/// <param name="Removed">Whether this computer no longer holds it.</param>
/// <param name="Reason">keypaste's own words when it was not removed, otherwise empty.</param>
public sealed record PairingRemoval(bool Removed, string Reason);

/// <summary>
/// Pairs a browser with this computer once the person confirms the code it shows, for every vault unlocked in this owner
/// process, and checks the pairings the extension presents (THREATS.md T-48, T-50).
/// </summary>
/// <remarks>
/// <para>
/// A request to pair puts a confirmation in front of the person straight away, naming the build the browser named and the six
/// digits <see cref="PairingCode"/> derives from the extension's key, which the extension shows too; only one is asked about at
/// a time, and a refusal holds the next back (<see cref="OwnerPairings"/>). The confirmation goes through the owner's own gate,
/// its allow disarmed for a second.
/// </para>
/// <para>
/// A pairing counts while an unlocked vault vouches for it in this computer's pairings file (<see cref="BrowserPairings"/>).
/// Each pairing granted or denied, each check refused and each removal is recorded on a browser line naming the build and the
/// pairing and never a key; a check that proves its pairing and the database hash release nothing and are not.
/// </para>
/// <para>
/// A page's list carries the names of its logins in every vault unlocked in this process and never a password; a pick then
/// releases that one login at once, since the person chose it in keypaste's list (THREATS.md T-59), and only the parts the page
/// shows. Each list that names a login and each release is recorded with the page's origin before the reply.
/// </para>
/// <para>
/// A fill may be continued once per part left, with no list and no pick, by the next click or fill shortcut in the same tab within
/// <see cref="ContinuedFill.Window"/> on a page of the same registrable domain and scheme: the password after the username, or the
/// one-time code. An https frame of the page's own site, the same registrable domain and scheme, is filled as the page itself is; a frame
/// of another site is offered only its own site's logins, and only once it is a known sign-in frame or
/// the person trusted it for the pairing here (THREATS.md T-62).
/// </para>
/// </remarks>
public sealed class SessionBrowsers : IBrowserRequests
{
    private readonly VaultIdentity _vault;
    private readonly SessionLifetime _lifetime;
    private readonly ApprovalGate _gate;
    private readonly Func<SessionLifetime, Vault?> _unlockedFor;
    private readonly Func<SessionLifetime, Func<Vault, bool>, AgentSave> _write;
    private readonly RequestRecorder _recorder;
    private readonly OwnerPairings _pairings;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _narrate;

    /// <summary>Builds the pairing of one unlock.</summary>
    /// <param name="vault">The vault this owner holds.</param>
    /// <param name="lifetime">The unlock this pairs for.</param>
    /// <param name="gate">Where the person is asked, under the same slot and window as agents.</param>
    /// <param name="unlockedFor">The vault while a lifetime is live, otherwise null.</param>
    /// <param name="write">Runs an edit on the lifetime's vault and saves it under the owner's claim.</param>
    /// <param name="audit">Opens the owner's log, as the authority's does.</param>
    /// <param name="pairings">This computer's pairings, as this owner process serves them.</param>
    /// <param name="clock">What time it is.</param>
    /// <param name="narrate">Where the owner says what it decided, or null.</param>
    internal SessionBrowsers(
        VaultIdentity vault,
        SessionLifetime lifetime,
        ApprovalGate gate,
        Func<SessionLifetime, Vault?> unlockedFor,
        Func<SessionLifetime, Func<Vault, bool>, AgentSave> write,
        Func<AuditLog?> audit,
        OwnerPairings pairings,
        TimeProvider clock,
        Action<string>? narrate)
    {
        _vault = vault;
        _lifetime = lifetime;
        _gate = gate;
        _unlockedFor = unlockedFor;
        _write = write;
        _recorder = new RequestRecorder(audit);
        _pairings = pairings;
        _clock = clock;
        _narrate = narrate;
        pairings.Serve(this, lifetime.Ended);
    }

    /// <summary>The most logins one list names.</summary>
    public const int MaximumNames = 50;

    /// <summary>The vault's name as a page's list shows it while several vaults are open.</summary>
    private string VaultName => Path.GetFileNameWithoutExtension(_vault.Path);

    /// <summary>Raised, on whichever thread removed it, once a pairing is removed, naming it.</summary>
    public event EventHandler<string>? Removed;

    /// <summary>The pairings any unlocked vault vouches for, with when each was last proven here.</summary>
    /// <returns>The rows, by name; none while locked.</returns>
    public IReadOnlyList<PairingRow> List()
    {
        try
        {
            return _unlockedFor(_lifetime) is { } vault
                ? [.. _pairings.File.List(Vouching(vault)).Select(pairing => new PairingRow(pairing, _pairings.LastUse(pairing.Id)))]
                : [];
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Removes a pairing for the person, for every vault, recording it, so the extension holding it is refused from its next check.</summary>
    /// <param name="id">The pairing's name.</param>
    /// <returns>Whether it was removed, and why not.</returns>
    public PairingRemoval Remove(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!_lifetime.IsLive || _unlockedFor(_lifetime) is not { } vault)
        {
            return new PairingRemoval(false, "the vault is locked");
        }

        if (!_pairings.File.TryRemove(Sealing(vault), id, out var removed, out var problem))
        {
            return new PairingRemoval(false, problem);
        }

        _pairings.Forget(id);
        Record(removed.Identity, id, BrowserAct.Remove, AuditDecision.Granted, AuditMethod.Removed, "the person removed the pairing in keypaste");
        _narrate?.Invoke($"removed the pairing {id}");
        Removed?.Invoke(this, id);
        _pairings.Change();
        return new PairingRemoval(true, string.Empty);
    }

    /// <summary>The sign-in frames of other sites the person trusted, for pairings any unlocked vault vouches for.</summary>
    /// <returns>The frames, by pairing, page and frame; none while locked.</returns>
    public IReadOnlyList<FrameTrust> FrameTrusts()
    {
        try
        {
            return _unlockedFor(_lifetime) is { } vault ? _pairings.File.Frames(Vouching(vault)) : [];
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Removes the person's trust of a sign-in frame for a pairing, for every vault, recording it, so the frame is asked about again.</summary>
    /// <param name="trust">The trust, as <see cref="FrameTrusts"/> listed it.</param>
    /// <returns>Whether it was removed, and why not.</returns>
    public PairingRemoval RemoveFrameTrust(FrameTrust trust)
    {
        ArgumentNullException.ThrowIfNull(trust);

        if (!_lifetime.IsLive || _unlockedFor(_lifetime) is not { } vault)
        {
            return new PairingRemoval(false, "the vault is locked");
        }

        if (!_pairings.File.TryDistrust(Sealing(vault), trust.Pairing, trust.Top, trust.Frame, out _, out var problem))
        {
            return new PairingRemoval(false, problem);
        }

        var identity = _pairings.File.List(Vouching(vault)).FirstOrDefault(pairing => string.Equals(pairing.Id, trust.Pairing, StringComparison.Ordinal))?.Identity;
        var named = $"sign-in from {trust.Frame} on {trust.Top}";

        if (identity is not null)
        {
            Record(identity, trust.Pairing, BrowserAct.Trust, AuditDecision.Granted, AuditMethod.Removed, $"the person removed their trust of {named} in keypaste");
        }

        _narrate?.Invoke($"no longer trusts {named} for {trust.Pairing}");
        _pairings.Change();
        return new PairingRemoval(true, string.Empty);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The SHA-256 of <c>keypaste</c>, a line feed and this home's identity, so the extension keeps one pairing for this
    /// computer whichever vaults are open, filed apart from KeePassXC's.
    /// </remarks>
    public string? DatabaseHash() =>
        _unlockedFor(_lifetime) is not null && _pairings.File.HomeId() is { } home ? HashOf(home) : null;

    /// <summary>The database hash keypaste answers for a home of this identity.</summary>
    /// <param name="home">The home's identity, as the pairings file keeps it.</param>
    /// <returns>The hash in lowercase hex.</returns>
    public static string HashOf(string home)
    {
        ArgumentNullException.ThrowIfNull(home);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("keypaste\n" + home)));
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

        if (PairingCode.Of(request.IdentificationKey) is not { } code)
        {
            Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.InvalidRequest, "the extension's key is not a public key");
            return new BrowserReply(BrowserOutcome.Refused, "the extension's key is not a public key");
        }

        // Neither refusal is recorded or asked about, so a program asking again and again writes no line for each request.
        if (!_pairings.TryTakeTurn(_clock.GetUtcNow(), out var holdBack))
        {
            return new BrowserReply(BrowserOutcome.Refused, holdBack > TimeSpan.Zero
                ? $"a request to pair was refused or went unanswered a moment ago; connect again in {ApprovalLimits.Describe((int)Math.Ceiling(holdBack.TotalSeconds))}"
                : "keypaste is already asking about another browser's pairing; answer it, then connect again");
        }

        try
        {
            List<string> taken;

            try
            {
                taken = [.. _pairings.File.List(Vouching(vault)).Select(pairing => pairing.Id)];
            }
            catch (ObjectDisposedException)
            {
                return new BrowserReply(BrowserOutcome.Locked, "the vault is locked");
            }

            var prompt = new PairingPrompt
            {
                Identity = browser,
                Code = code,
                SuggestedName = PairingNames.Suggested(browser, Environment.MachineName, taken),
                TakenNames = taken,
            };

            var answer = await _gate.AskAsync(prompt, cancellationToken).ConfigureAwait(false);

            if (!_lifetime.IsLive || waiting.IsCancellationRequested)
            {
                var withdrawn = _lifetime.IsLive ? $"{_lifetime.WithdrawnBecause} before the pairing was saved" : "the vault was locked before the pairing was saved";
                Record(browser, null, BrowserAct.Pair, AuditDecision.Denied, _lifetime.IsLive ? AuditMethod.VaultChanged : AuditMethod.VaultLocked, withdrawn);
                return new BrowserReply(_lifetime.IsLive ? BrowserOutcome.Refused : BrowserOutcome.Locked, withdrawn);
            }

            if (answer is not { Answer: ApprovalAnswer.ApprovedOnce, Name: { } name })
            {
                if (answer.Answer is ApprovalAnswer.Denied or ApprovalAnswer.TimedOut)
                {
                    _pairings.Refused(_clock.GetUtcNow());
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

            if (!_lifetime.TryCommitAfter(waiting) || _unlockedFor(_lifetime) is not { } open)
            {
                Record(browser, name, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.VaultLocked, "the vault was locked before the pairing was saved");
                return new BrowserReply(BrowserOutcome.Locked, "the vault was locked before the pairing was saved");
            }

            var now = _clock.GetUtcNow();

            if (!_pairings.File.TryAdd(Sealing(open), name, browser, request.IdentificationKey, now, out var problem))
            {
                var why = $"a person allowed it, and then: {problem}";
                Record(browser, name, BrowserAct.Pair, AuditDecision.Denied, AuditMethod.Failed, why);
                return new BrowserReply(BrowserOutcome.Refused, why);
            }

            _pairings.Paired();
            _pairings.Used(name, now);
            Record(browser, name, BrowserAct.Pair, AuditDecision.Granted, AuditMethod.Prompt, "a person allowed it, and this computer holds the pairing");
            _narrate?.Invoke($"paired {browser.Described} as {name}");
            _pairings.Change();
            return new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = name };
        }
        finally
        {
            _pairings.EndTurn();
        }
    }

    /// <inheritdoc/>
    /// <remarks>A pairing proven is sealed under each unlocked vault that did not vouch for it yet, so it holds in each after the others lock.</remarks>
    public ValueTask<BrowserReply> CheckAsync(BrowserCheckRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        var proof = Prove(request.Pairing, request.IdentificationKey, browser);

        if (proof.Pairing is { } pairing)
        {
            Adopt(pairing);
        }

        return ValueTask.FromResult(proof.Pairing is not null
            ? new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = request.Pairing }
            : new BrowserReply(proof.Outcome, proof.Reason));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The list spans every vault unlocked in this process and names the open vaults that are locked. A list that names a login
    /// is recorded as one line before the reply; one that matches nothing writes none.
    /// </remarks>
    public ValueTask<BrowserListReply> ListAsync(BrowserListRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        return ListOwnAsync(request, browser, cancellationToken);
    }

    private async ValueTask<BrowserListReply> ListOwnAsync(BrowserListRequest request, BrowserIdentity browser, CancellationToken cancellationToken)
    {
        if (_unlockedFor(_lifetime) is null)
        {
            return new BrowserListReply(BrowserOutcome.Locked, "the vault is locked");
        }

        if (!_recorder.Opens)
        {
            return new BrowserListReply(BrowserOutcome.Refused, RequestRecorder.Unopened);
        }

        var proof = Prove(request.Pairing, request.IdentificationKey, browser);

        if (proof.Pairing is not { } pairing)
        {
            return new BrowserListReply(proof.Outcome, proof.Reason);
        }

        var locked = _pairings.Locked();

        if (!LoginLookup.TryRead(request.Url, request.SubmitUrl, httpAuthentication: false, out var lookup, out _))
        {
            return new BrowserListReply(BrowserOutcome.Done, string.Empty) { LockedVaults = locked };
        }

        if (!TryFrame(lookup, request.TopUrl, out var top, out var refusal))
        {
            Record(browser, pairing.Id, BrowserAct.List, AuditDecision.Denied, AuditMethod.OutOfScope, $"{refusal}, on {lookup.Page}");
            return new BrowserListReply(BrowserOutcome.Refused, refusal);
        }

        if (top is not null && !request.AsksTrust && !IsTrusted(pairing, top, lookup.Page))
        {
            var unasked = $"sign-in from {lookup.Page.Host} on {top.Host} is not trusted, and the fill shortcut asks nobody";
            Record(browser, pairing.Id, BrowserAct.List, AuditDecision.Denied, AuditMethod.OutOfScope, $"{unasked}, on {lookup.Page}");
            return new BrowserListReply(BrowserOutcome.Refused, unasked);
        }

        if (top is not null && await TrustAsync(browser, pairing, top, lookup.Page, cancellationToken).ConfigureAwait(false) is { } distrusted)
        {
            return new BrowserListReply(BrowserOutcome.Refused, distrusted);
        }

        var serving = Serving();
        var named = serving.Count + locked.Count > 1;
        List<BrowserLoginName> names = [.. serving.SelectMany(vault => vault.Names(lookup, named)).Take(MaximumNames)];
        var reply = new BrowserListReply(BrowserOutcome.Done, string.Empty)
        {
            Site = lookup.Page.Host,
            Names = names,
            LockedVaults = locked,
            Warning = Warning(lookup),
        };

        if (names.Count == 0)
        {
            return reply;
        }

        var origin = top is null ? lookup.Page.ToString() : $"{lookup.Page} in a sign-in frame on {top}";
        var line = RequestRecords.Browser(browser, pairing.Id, BrowserAct.List, AuditDecision.Granted, AuditMethod.Exposure, $"listed {names.Count} login(s) by name, no password, on {origin}", _lifetime.Id, _vault.Key);
        return _recorder.Recorded(reply, line);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The vault holding the item fills it at once, read again by its UUID through the page's matching rules, and records the
    /// fill with the page's origin before the reply (SECURITY.md law 3).
    /// </remarks>
    public ValueTask<BrowserFillReply> ReleaseAsync(BrowserReleaseRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(browser);

        return ValueTask.FromResult(Release(request, browser));
    }

    private BrowserFillReply Release(BrowserReleaseRequest request, BrowserIdentity browser)
    {
        if (_unlockedFor(_lifetime) is null)
        {
            return new BrowserFillReply(BrowserOutcome.Locked, "the vault is locked");
        }

        if (!_recorder.Opens)
        {
            return new BrowserFillReply(BrowserOutcome.Refused, RequestRecorder.Unopened);
        }

        var proof = Prove(request.Pairing, request.IdentificationKey, browser);

        if (proof.Pairing is not { } pairing)
        {
            return new BrowserFillReply(proof.Outcome, proof.Reason);
        }

        if (!LoginLookup.TryRead(request.Url, request.SubmitUrl, httpAuthentication: false, out var lookup, out _))
        {
            return new BrowserFillReply(BrowserOutcome.Refused, "keypaste fills no login on this page");
        }

        var origin = lookup.Page.ToString();

        if (!TryFrame(lookup, request.TopUrl, out var top, out var refusal))
        {
            return Refuse(browser, pairing.Id, AuditMethod.OutOfScope, refusal, origin);
        }

        if (top is not null && !IsTrusted(pairing, top, lookup.Page))
        {
            return Refuse(browser, pairing.Id, AuditMethod.OutOfScope, $"sign-in from {lookup.Page.Host} on {top.Host} is not trusted", origin);
        }

        var parts = ContinuedFill.PartsOf(request.Fill);
        var continuing = (ContinuedFill?)null;

        if (request.Continued && !TryContinuing(pairing, request.Uuid, lookup.Page, parts, out continuing, out var why))
        {
            return Refuse(browser, pairing.Id, AuditMethod.OutOfScope, why, origin);
        }

        foreach (var vault in Serving())
        {
            var waiting = vault._lifetime.Waiting;

            if (vault.Holds(lookup, request.Uuid, out var read))
            {
                return vault.ReleaseOwn(lookup, read, new Fill(request.Uuid, parts, continuing, top), pairing, browser, waiting);
            }
        }

        return Refuse(browser, pairing.Id, AuditMethod.OutOfScope, "no open vault holds that login for this page", origin);
    }

    /// <summary>What a release fills: the item, the parts the page shows, the fill it continues if it does, and the tab's origin when the page is a frame of another.</summary>
    private sealed record Fill(string Uuid, LoginParts Parts, ContinuedFill? Continuing, WebOrigin? Top);

    /// <summary>The fill a continued request continues, when one for this pairing and item is armed, unlapsed, on this page's site and has the parts it asks for left.</summary>
    private bool TryContinuing(BrowserPairing pairing, string uuid, WebOrigin page, LoginParts parts, [NotNullWhen(true)] out ContinuedFill? continuing, out string why)
    {
        continuing = _pairings.Continued(pairing.Id);
        why = continuing switch
        {
            null => "nothing filled in this browser in the last two minutes is left to continue",
            _ when !string.Equals(continuing.Uuid, uuid, StringComparison.OrdinalIgnoreCase) => "the fill it continues was of another login",
            _ when _clock.GetUtcNow() > continuing.Until => "the fill it continues was more than two minutes ago",
            _ when !string.Equals(page.Scheme, continuing.PickedOn.Scheme, StringComparison.Ordinal) || !string.Equals(page.Site, continuing.Site, StringComparison.Ordinal) => "the fill it continues was on another site",
            _ when (parts & ~continuing.Left) != LoginParts.None => $"the fill it continues has no {ContinuedFill.Described(parts & ~continuing.Left)} left to fill",
            _ => string.Empty,
        };

        return why.Length == 0;
    }

    /// <summary>
    /// The tab's origin when the page is a frame of another site, or null when it is the tab's own page or an https frame of its
    /// site, the same registrable domain and scheme, which is filled as the page is; false, with why, when keypaste fills nothing
    /// in it, as in any frame of another origin over http.
    /// </summary>
    private static bool TryFrame(LoginLookup lookup, string? topUrl, out WebOrigin? top, out string refusal)
    {
        top = null;
        refusal = string.Empty;

        if (topUrl is null)
        {
            return true;
        }

        if (!WebOrigin.TryRead(topUrl, out var tab, out _))
        {
            refusal = "keypaste fills no login in a frame of a page it cannot read";
            return false;
        }

        if (string.Equals(tab.ToString(), lookup.Page.ToString(), StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(lookup.Page.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            refusal = "keypaste fills a frame of another origin only over https";
            return false;
        }

        // A frame of the page's own site is answered by the same site as the page, so it is the page's own as far as logins go.
        if (string.Equals(tab.Scheme, lookup.Page.Scheme, StringComparison.Ordinal) && tab.Site is { } site && string.Equals(site, lookup.Page.Site, StringComparison.Ordinal))
        {
            return true;
        }

        top = tab;
        return true;
    }

    /// <summary>Whether a frame may be offered its own site's logins in the tab's page: a known sign-in frame, or one the person trusted for this pairing.</summary>
    private bool IsTrusted(BrowserPairing pairing, WebOrigin top, WebOrigin frame)
    {
        if (SignInFrames.IsKnown(top.Host, frame.Host))
        {
            return true;
        }

        try
        {
            return _unlockedFor(_lifetime) is { } vault && _pairings.File.Trusts(Vouching(vault), pairing.Id, top.Host, frame.Host);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Null when a frame may be offered its own site's logins in the tab's page, asking the person first when it is neither a
    /// known sign-in frame nor one they trusted, and recording their trust; otherwise why not, recorded.
    /// </summary>
    private async ValueTask<string?> TrustAsync(BrowserIdentity browser, BrowserPairing pairing, WebOrigin top, WebOrigin frame, CancellationToken cancellationToken)
    {
        if (IsTrusted(pairing, top, frame))
        {
            return null;
        }

        var waiting = _lifetime.Waiting;
        var named = $"sign-in from {frame.Host} on {top.Host}";
        var prompt = new FrameTrustPrompt { Identity = browser, Pairing = pairing.Id, Top = top.Host, Frame = frame.Host };
        var answer = await _gate.AskAsync(prompt, cancellationToken).ConfigureAwait(false);

        if (!_lifetime.IsLive || waiting.IsCancellationRequested)
        {
            var withdrawn = _lifetime.IsLive ? $"{_lifetime.WithdrawnBecause} before {named} was trusted" : $"the vault was locked before {named} was trusted";
            Record(browser, pairing.Id, BrowserAct.Trust, AuditDecision.Denied, _lifetime.IsLive ? AuditMethod.VaultChanged : AuditMethod.VaultLocked, withdrawn);
            return withdrawn;
        }

        if (answer != ApprovalAnswer.ApprovedOnce)
        {
            var why = answer switch
            {
                ApprovalAnswer.Denied => $"a person did not trust {named}",
                ApprovalAnswer.TimedOut => "nobody answered inside the window",
                ApprovalAnswer.Busy => "another prompt is waiting for an answer; try again once it is answered",
                ApprovalAnswer.Cooldown => $"a person did not trust {named} a moment ago; try again in a minute",
                ApprovalAnswer.Cancelled => "the request was withdrawn before anybody answered",
                _ => "the question could not be shown, so nobody was asked",
            };

            Record(browser, pairing.Id, BrowserAct.Trust, AuditDecision.Denied, answer.ToAuditMethod(), why);
            _narrate?.Invoke($"did not trust {named}: {why}");
            return why;
        }

        if (!_lifetime.TryCommitAfter(waiting) || _unlockedFor(_lifetime) is not { } open)
        {
            Record(browser, pairing.Id, BrowserAct.Trust, AuditDecision.Denied, AuditMethod.VaultLocked, $"the vault was locked before {named} was trusted");
            return "the vault is locked";
        }

        if (!_pairings.File.TryTrust(Sealing(open), pairing.Id, top.Host, frame.Host, _clock.GetUtcNow(), out var problem))
        {
            var failed = $"a person trusted {named}, and then: {problem}";
            Record(browser, pairing.Id, BrowserAct.Trust, AuditDecision.Denied, AuditMethod.Failed, failed);
            return failed;
        }

        Record(browser, pairing.Id, BrowserAct.Trust, AuditDecision.Granted, AuditMethod.Prompt, $"a person trusted {named}");
        _narrate?.Invoke($"trusted {named} for {pairing.Id}");
        _pairings.Change();
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>Releases nothing and is not recorded; an owner with no window refuses it.</remarks>
    public ValueTask<BrowserReply> ShowAsync(BrowserShowRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_pairings.Show is not { } show)
        {
            return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Refused, "this keypaste has no window to show"));
        }

        show(
            request.What,
            request.What == BrowserShow.Open ? string.Empty : request.VaultName,
            request.What == BrowserShow.NewLogin ? NewLoginPage.Of(request.Url) : NewLoginPage.None);
        return ValueTask.FromResult(new BrowserReply(BrowserOutcome.Done, string.Empty));
    }

    /// <summary>What the page's list warns of before a pick, such as its connection not being secure, or empty.</summary>
    private static string Warning(LoginLookup lookup) =>
        lookup.OtherSiteForm is { } form ? $"This form sends what you fill to {form.Host}, another site."
        : lookup.Page.Scheme == Uri.UriSchemeHttp ? "This page's connection is not secure."
        : lookup.Page.IsSingleLabel ? $"Anyone on this network can answer to {lookup.Page.Host}."
        : string.Empty;

    /// <summary>This unlock's browsers first, then every other unlock's serving in this process.</summary>
    private List<SessionBrowsers> Serving() =>
        [this, .. _pairings.Serving().Where(other => !ReferenceEquals(other, this) && other._lifetime.IsLive)];

    /// <summary>The names of this vault's logins a page matches, with the vault's name when <paramref name="named"/>; none when it cannot be read.</summary>
    private IReadOnlyList<BrowserLoginName> Names(LoginLookup lookup, bool named)
    {
        if (!_lifetime.IsLive || Match(lookup, out var read) is not null)
        {
            return [];
        }

        var vault = named ? VaultName : string.Empty;

        return [.. read.Candidates.Select(item => new BrowserLoginName(item.Name.Uuid!, Sanitized(item.Name.Title), Sanitized(item.Username)) { Vault = vault })];
    }

    /// <summary>Whether this vault holds an item of that UUID the page matches, offered or kept out, with the page's match it read.</summary>
    private bool Holds(LoginLookup lookup, string uuid, out (IReadOnlyList<BrowserItem> Candidates, IReadOnlyList<RefusedMatch> Refused) read)
    {
        read = ([], []);

        return _lifetime.IsLive
            && Match(lookup, out read) is null
            && (read.Candidates.Any(item => IsItem(item, uuid)) || read.Refused.Any(refused => IsItem(refused.Item, uuid)));
    }

    /// <summary>
    /// Releases one of this vault's logins at the person's pick in the page's list, from the match <see cref="Holds"/> read after
    /// <paramref name="waiting"/>; reading the login by its UUID checks the file again before it is released.
    /// </summary>
    private BrowserFillReply ReleaseOwn(
        LoginLookup lookup,
        (IReadOnlyList<BrowserItem> Candidates, IReadOnlyList<RefusedMatch> Refused) read,
        Fill fill,
        BrowserPairing pairing,
        BrowserIdentity browser,
        CancellationToken waiting)
    {
        var origin = lookup.Page.ToString();

        if (read.Candidates.FirstOrDefault(candidate => IsItem(candidate, fill.Uuid)) is not { } item)
        {
            return read.Refused.FirstOrDefault(refused => IsItem(refused.Item, fill.Uuid)) is { } kept
                ? Refuse(browser, pairing.Id, AuditMethod.Blocked, kept.Reason, origin, item: kept.Item.Name)
                : Refuse(browser, pairing.Id, AuditMethod.OutOfScope, "the item does not match this page", origin);
        }

        if (_unlockedFor(_lifetime) is not { } vault)
        {
            return new BrowserFillReply(BrowserOutcome.Locked, "the vault is locked");
        }

        if (vault.ReadSavedLogin(item.Name.Uuid!, out var login) != SavedRead.Current || login is null)
        {
            return Refuse(browser, pairing.Id, AuditMethod.VaultChanged, "the item could not be read as the vault's file holds it", origin, item: item.Name);
        }

        // Only the parts the page shows are read out and must fill, so a username-only page needs no password that can leave.
        var wantsUsername = fill.Parts.HasFlag(LoginParts.Username);
        var wantsPassword = fill.Parts.HasFlag(LoginParts.Password);

        if ((wantsUsername && login.Username.Value is null) || (wantsPassword && login.Password.Value is null))
        {
            var unfilled = wantsUsername && login.Username.Value is null
                ? $"its username {login.Username.Refusal}"
                : $"its password {login.Password.Refusal}";
            return Refuse(browser, pairing.Id, AuditMethod.Unfillable, unfilled, origin, item: item.Name);
        }

        if (!TryReadCode(vault, item.Name.Uuid!, fill.Parts.HasFlag(LoginParts.Totp), out var holdsCode, out var code))
        {
            return Refuse(browser, pairing.Id, AuditMethod.VaultChanged, "the item could not be read as the vault's file holds it", origin, item: item.Name);
        }

        if (fill.Parts.HasFlag(LoginParts.Totp) && code.Length == 0)
        {
            return Refuse(browser, pairing.Id, AuditMethod.Unfillable, "it has no one-time code keypaste can make", origin, item: item.Name);
        }

        var now = _clock.GetUtcNow();
        var left = (fill.Continuing?.Left ?? LoginParts.All) & ~fill.Parts & (holdsCode ? LoginParts.All : ~LoginParts.Totp);
        var next = left != LoginParts.None && lookup.Page.Site is { } site
            ? new ContinuedFill(pairing.Id, item.Name.Uuid!, fill.Continuing?.PickedOn ?? lookup.Page, site, left, now + ContinuedFill.Window)
            : null;

        var released = new BrowserLogin(wantsUsername ? login.Username.Value! : string.Empty, wantsPassword ? login.Password.Value! : string.Empty, login.Entry.Title, item.Name.Uuid!) { Totp = code };
        var reply = new BrowserFillReply(BrowserOutcome.Done, string.Empty)
        {
            Logins = [released],
            ContinueSite = next?.Site ?? string.Empty,
            ContinueSeconds = next is null ? 0 : (int)ContinuedFill.Window.TotalSeconds,
        };

        if (!ApproverProtocol.Fits(reply))
        {
            return Refuse(browser, pairing.Id, AuditMethod.Undeliverable, "the login would not fit one reply", origin, item: item.Name);
        }

        if (fill.Continuing is { } continuing && !_pairings.TryTake(continuing, next))
        {
            return Refuse(browser, pairing.Id, AuditMethod.OutOfScope, "the fill it continues was continued already", origin, item: item.Name);
        }

        if (!_lifetime.TryCommitAfter(waiting))
        {
            return Refuse(browser, pairing.Id, AuditMethod.VaultLocked, "the vault was locked before the login was filled", origin, BrowserOutcome.Locked, item.Name);
        }

        if (fill.Continuing is null)
        {
            _pairings.Continue(pairing.Id, next);
        }

        _pairings.Used(pairing.Id, now);
        _narrate?.Invoke($"filled a login on {origin} for {pairing.Id}");

        var framed = fill.Top is { } top ? $" in a sign-in frame on {top}" : string.Empty;
        var parts = fill.Parts == (LoginParts.Username | LoginParts.Password) ? string.Empty : $", its {ContinuedFill.Described(fill.Parts)}";
        var (method, reason) = fill.Continuing is { } continued
            ? (AuditMethod.Continued, $"it continued, within two minutes, the person's pick on {continued.PickedOn}{parts}{framed}")
            : (AuditMethod.Picked, $"the person picked it in the page's list{parts}{framed}");

        return _recorder.Recorded(reply, [FillLine(browser, pairing.Id, item.Name, origin, AuditDecision.Granted, method, reason)]);
    }

    /// <summary>
    /// Reads whether the item holds a one-time code keypaste can make and, when <paramref name="compute"/>, its current code; the seed
    /// goes no further than the settings, disposed here. False when the vault differs from its file.
    /// </summary>
    private bool TryReadCode(Vault vault, string uuid, bool compute, out bool holds, out string code)
    {
        holds = false;
        code = string.Empty;
        TotpSpec? spec = null;

        try
        {
            if (vault.ReadSavedLoginTotp(uuid, out spec, out _) != SavedRead.Current)
            {
                return false;
            }

            holds = spec is not null;

            if (compute && spec is not null)
            {
                code = spec.Code(_clock).Value;
            }

            return true;
        }
        catch (Exception failure) when (failure is not ObjectDisposedException)
        {
            // Settings keypaste cannot make a code from make none, as an agent's request for one is refused (law 7).
            holds = false;
            code = string.Empty;
            return true;
        }
        finally
        {
            spec?.Dispose();
        }
    }

    private static bool IsItem(BrowserItem item, string uuid) =>
        string.Equals(item.Name.Uuid, uuid, StringComparison.OrdinalIgnoreCase);

    private static string Sanitized(string text) => text.Length == 0 ? string.Empty : EntryNameSanitizer.Sanitize(text).Text;

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

    /// <summary>Proves a pairing against the vaults unlocked in this process, recording a refusal.</summary>
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

            check = _pairings.File.Verify(Vouching(vault), id, browser, identificationKey, out pairing);
        }
        catch (ObjectDisposedException)
        {
            return (null, BrowserOutcome.Locked, "the vault is locked");
        }

        if (pairing is not null)
        {
            _pairings.Used(id, _clock.GetUtcNow());
            return (pairing, BrowserOutcome.Done, string.Empty);
        }

        var (method, why) = check == PairingCheck.Unreadable
            ? (AuditMethod.Failed, "this computer's pairings could not be read")
            : (AuditMethod.NotPaired, "no open vault vouches for a pairing of that name for this browser and key");

        Record(browser, id, BrowserAct.Check, AuditDecision.Denied, method, why);
        return (null, check == PairingCheck.Unreadable ? BrowserOutcome.Refused : BrowserOutcome.NotPaired, why);
    }

    /// <summary>Seals a pairing under each unlocked vault that does not vouch for it yet; a failure changes nothing the seal that proved it does not hold.</summary>
    private void Adopt(BrowserPairing pairing)
    {
        try
        {
            if (_unlockedFor(_lifetime) is { } vault && !_pairings.File.AllVouch(Vouching(vault), pairing))
            {
                _pairings.File.TryAdopt(Sealing(vault), out _);
            }
        }
        catch (ObjectDisposedException)
        {
            // Locked meanwhile: the other vault's seal still holds the pairing.
        }
    }

    /// <summary>This vault and every other vault unlocked in this process, which vouch for pairings together.</summary>
    private List<Vault> Vouching(Vault vault) =>
        [vault, .. _pairings.Unlocked().Select(other => other.Vault).Where(other => !ReferenceEquals(other, vault))];

    /// <summary>The vaults a pairing write seals under: this one first, which must save, then every other unlocked here.</summary>
    private List<PairingVault> Sealing(Vault vault) =>
        [new PairingVault(vault, Save), .. _pairings.Unlocked().Where(other => !ReferenceEquals(other.Vault, vault))];

    /// <summary>Saves an edit to this unlock's vault, taking it back out of the open vault when the save fails.</summary>
    private AgentSave Save(Func<Vault, bool> edit)
    {
        Vault? written = null;
        VaultCheckpoint? checkpoint = null;

        var saved = _write(_lifetime, open =>
        {
            written = open;
            checkpoint = open.Checkpoint();
            return edit(open);
        });

        if (!saved.Holds)
        {
            TakeBack(written, checkpoint);
        }

        return saved;
    }

    private AuditRecord FillLine(BrowserIdentity browser, string pairing, EntryName? item, string origin, AuditDecision decision, AuditMethod method, string reason) =>
        RequestRecords.BrowserFill(browser, pairing, item, origin, decision, method, reason, _lifetime.Id, _vault.Key);

    /// <summary>Refuses a release, recording one line naming the item when one was picked.</summary>
    private BrowserFillReply Refuse(BrowserIdentity browser, string pairing, AuditMethod method, string reason, string origin, BrowserOutcome outcome = BrowserOutcome.Refused, EntryName? item = null)
    {
        _recorder.Recorded(FillLine(browser, pairing, item, origin, AuditDecision.Denied, method, reason));
        return new BrowserFillReply(outcome, reason);
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
