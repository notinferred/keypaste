using Keypaste.Core.Approval;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>What the bridge is asking the approver for.</summary>
public enum ApproverMessageKind
{
    /// <summary>Not a message this version understands.</summary>
    Unknown = 0,

    /// <summary>The entry names the bridge may show an agent.</summary>
    Names = 1,

    /// <summary>One field of one entry, subject to a human saying yes.</summary>
    Credential = 2,

    /// <summary>Binds a connection to the session holding a named vault.</summary>
    Attach = 3,

    /// <summary>A project's env set for <c>keypaste run --session</c>, subject to a human saying yes.</summary>
    Env = 4,

    /// <summary>The grants the owner's current session holds, as a list shows them.</summary>
    Grants = 5,
    /// <summary>Ends grants of the owner's current session.</summary>
    RevokeGrants = 6,
    /// <summary>Asks the owner to lock now.</summary>
    Lock = 7,
    /// <summary>An env request naming a profile other than the default, or a subset of keys.</summary>
    EnvProfile = 8,
    /// <summary>An env request authorized by a scoped token instead of a prompt.</summary>
    TokenEnv = 9,
    /// <summary>An agent's command to start with approved secrets in its environment.</summary>
    Run = 10,
}

/// <summary>Who a bridge says its client is, for display and counting only (THREATS.md T-3).</summary>
/// <param name="Name">What the client called itself, or null.</param>
/// <param name="Version">What version it claimed, or null.</param>
/// <param name="Label">The bridge's raw <c>--client-label</c>, or null.</param>
public sealed record AttachClient(string? Name, string? Version, string? Label)
{
    /// <summary>The longest identity field an attach frame carries.</summary>
    public const int MaximumLength = 64;
}

/// <summary>Asks the owner of a vault to attach this connection to its current session.</summary>
/// <param name="Vault">The vault the bridge was configured with, as an absolute path.</param>
public sealed record AttachRequest(string Vault)
{
    /// <summary>The bridge's client, or null for a runner, <c>grants</c> or <c>lock</c>, which are not counted.</summary>
    public AttachClient? Client { get; init; }
}

/// <summary>The session a connection is now attached to, or why it is not attached.</summary>
/// <param name="Session">The session's identifier, or null when the connection was not attached.</param>
/// <param name="Refusal">Why not, for the audit line, or null when it was attached.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record AttachReply(string? Session, AuditMethod? Refusal, string Reason)
{
    /// <summary>Whether the connection may now make requests.</summary>
    public bool Attached => Session is { Length: > 0 } && Refusal is null;

    /// <summary>A connection attached to a session.</summary>
    /// <param name="session">The session's identifier.</param>
    /// <returns>The reply.</returns>
    public static AttachReply To(string session) => new(session, null, string.Empty);

    /// <summary>A connection that was not attached.</summary>
    /// <param name="method">vault-locked or no-session.</param>
    /// <param name="reason">keypaste's own words.</param>
    /// <returns>The reply.</returns>
    public static AttachReply Refused(AuditMethod method, string reason) => new(null, method, reason);
}

/// <summary>
/// A request for the entry names an agent may be shown. It carries nothing, deliberately.
/// </summary>
/// <remarks>
/// <c>list_entry_names</c> takes no arguments (THREATS.md T-4), so there is nothing agent-controlled
/// to forward and no parameter that could be coaxed into widening the listing. The exposure comes
/// from the bridge's own configuration, not from the call.
/// </remarks>
/// <param name="Exposure">The globs the bridge was configured with, applied again by the approver.</param>
public sealed record NamesRequest(IReadOnlyList<string> Exposure)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The names the approver is willing to have shown, or why there are none.</summary>
/// <param name="VaultUnlocked">Whether a vault was open at all.</param>
/// <param name="Names">The raw, unsanitized names inside the exposure. Sanitizing is the bridge's job.</param>
/// <param name="Reason">Why the list is empty, when it is. keypaste's own words, not an agent's.</param>
/// <param name="Complete">Whether these are all the names there were.</param>
/// <remarks>
/// <para>
/// <b><paramref name="Complete"/> has no default, deliberately.</b> One reply fits one frame, so a
/// listing can be cut short by how long its names are, and a reply that leaves names out must say
/// so — an agent told nothing assumes it has the lot. A defaulted <c>true</c> would compile every
/// existing construction unchanged, which is precisely how a future path forgets to say otherwise.
/// </para>
/// <para>
/// Each layer may lower it and none may raise it: the encoder drops what will not fit and clears
/// this, and the bridge reports whatever it was told.
/// </para>
/// </remarks>
public sealed record NamesReply(
    bool VaultUnlocked,
    IReadOnlyList<EntryName> Names,
    string Reason,
    bool Complete)
{
    /// <summary>The session that answered, or null when none did.</summary>
    public string? Session { get; init; }

    /// <summary>
    /// Why an unlocked vault named nothing, when the reason has a word of its own; null reads as the
    /// vault being locked, as before this existed.
    /// </summary>
    public AuditMethod? Refusal { get; init; }
}

/// <summary>An agent's credential request, forwarded to whoever can ask a human about it.</summary>
/// <remarks>
/// <b>The exposure travels with the request</b> rather than being configured on the approver, and
/// that is deliberate. The bridge can check a path-shaped entry against its globs before forwarding,
/// but it cannot check a handle, because resolving one needs the vault it does not have. So the
/// approver re-checks after it resolves, using the same globs — otherwise a handle would be a way
/// around the exposure rule (THREATS.md T-4). Sending them is not a weakening: whoever spawns the
/// bridge already chooses its argv, which THREATS.md assumption 2 says outright.
/// </remarks>
public sealed record CredentialRequest
{
    /// <summary>The <c>entry</c> argument exactly as the agent wrote it, handle or path.</summary>
    public required string Entry { get; init; }

    /// <summary>Which field, already checked against <see cref="Approval.CredentialFields"/>.</summary>
    public required string Field { get; init; }

    /// <summary>The agent's stated reason, verbatim and untrusted (THREATS.md T-2).</summary>
    public required string Reason { get; init; }

    /// <summary>How long the agent asked for. The approver decides what it actually gets.</summary>
    public required int TtlSeconds { get; init; }

    /// <summary>The globs the bridge was configured with.</summary>
    public required IReadOnlyList<string> Exposure { get; init; }

    /// <summary>What the client called itself. Unauthenticated, so display and audit only (T-3).</summary>
    public string? ClientName { get; init; }

    /// <summary>What version the client claimed. Same caveat.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>
    /// The name a human gave this bridge with <c>--client-label</c>, raw and exactly as written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="ClientName"/> this cannot be chosen by whoever <em>connects</em> — whoever
    /// spawns the bridge chooses it — which is the entire reason a policy rule keys on this and on
    /// nothing else (THREATS.md T-3). Null when the operator set none, and a null label matches no
    /// rule, including one written <c>client = "*"</c>.
    /// </para>
    /// <para>
    /// <b>Raw, not sanitized.</b> The operator writes <c>--client-label claude-code</c> in one file
    /// and <c>client = "claude-code"</c> in another, and those two strings have to compare as
    /// written. <c>EntryNameSanitizer</c> is lossy, and two distinct labels collapsing into one
    /// identical display string would be a widening. The audit line keeps using the sanitized form,
    /// because that one is for reading.
    /// </para>
    /// </remarks>
    public string? ClientLabel { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The approver's answer, and — on exactly one path — the field value itself.</summary>
/// <remarks>
/// <para>
/// <b><see cref="ToString"/> is overridden, and that is not cosmetic.</b> A positional or default
/// record prints every member, so one interpolated string in a log line, an exception message or a
/// debugger-friendly trace would put a live credential somewhere it can never be taken back from.
/// The override is the only thing standing between that and a stray <c>$"{reply}"</c>.
/// </para>
/// <para>
/// <see cref="Value"/> is a <see cref="string"/> because it crossed a process boundary as bytes and
/// there is nowhere else for it to land. It cannot be zeroed. SECURITY.md says so rather than
/// implying otherwise.
/// </para>
/// </remarks>
public sealed record CredentialReply
{
    /// <summary>Whether anything was released.</summary>
    public required AuditDecision Decision { get; init; }

    /// <summary>How the decision was reached, for the audit line the bridge writes.</summary>
    public required AuditMethod Method { get; init; }

    /// <summary>keypaste's own one-line explanation. Trusted text, unlike the agent's reason.</summary>
    public required string Reason { get; init; }

    /// <summary>The entry the request resolved to, sanitized, or null when it resolved to none.</summary>
    public string? Entry { get; init; }

    /// <summary>The TTL that was actually granted, or zero.</summary>
    public int TtlSeconds { get; init; }

    /// <summary>The released field value. Present only when <see cref="Decision"/> is granted.</summary>
    public string? Value { get; init; }

    /// <summary>The session that answered, or null when none did.</summary>
    public string? Session { get; init; }

    /// <summary>A description with the credential left out.</summary>
    /// <returns>The decision and method, and never the value.</returns>
    public override string ToString() =>
        $"CredentialReply {{ Decision = {Decision}, Method = {Method}, Value = {(Value is null ? "none" : "<redacted>")} }}";
}

/// <summary>
/// Asks the owner to release a project's env set to a runner, which starts the command with it.
/// </summary>
/// <remarks>
/// The command and directory are what the person is shown; the owner runs nothing. They are the
/// runner's claim, and a program running as the same user could claim one command and run another
/// (THREATS.md T-30).
/// </remarks>
/// <param name="Project">The project whose set is asked for.</param>
/// <param name="Command">The command the runner will start, one argument per item.</param>
/// <param name="Directory">The directory it will start in.</param>
public sealed record EnvRequest(string Project, IReadOnlyList<string> Command, string Directory)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>The profile asked for; the default profile travels as the original <c>env</c> kind.</summary>
    public string Profile { get; init; } = EnvProfileNames.Default;

    /// <summary>The keys the runner will inject, or null for the whole set.</summary>
    public IReadOnlyList<string>? Keys { get; init; }

    /// <summary>What a reference file makes of the set, as the runner claims it: <c>NAME ← KEY</c> and literal <c>NAME=value</c> lines; null outside reference-file mode.</summary>
    public IReadOnlyList<string>? FileLines { get; init; }
}

/// <summary>The owner's answer to an <see cref="EnvRequest"/>: the whole set, or why none of it.</summary>
/// <param name="Set">The set, with its values only when its outcome is <see cref="EnvOutcome.Resolved"/>.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record EnvReply(EnvResolved Set, string Reason)
{
    /// <summary>A description with every value left out.</summary>
    /// <returns>The outcome, and never a value.</returns>
    public override string ToString() => $"EnvReply {{ Outcome = {Set.Outcome}, Variables = {Set.Variables.Count} }}";
}

/// <summary>One grant in force, as <c>keypaste grants</c> lists it. It has no member for a value.</summary>
/// <param name="Id">The id a revoke names it by, from <see cref="GrantId"/>.</param>
/// <param name="Kind"><c>credential</c> for an agent's field, <c>env</c> for a <c>keypaste run --session</c> set, <c>run</c> for an agent's run.</param>
/// <param name="Client">Who it was granted to, as the prompt showed it.</param>
/// <param name="Scope">What it releases: the entry, or the project and profile.</param>
/// <param name="Field">Which field, or <c>set</c> for an env set.</param>
/// <param name="SecondsLeft">How long it has left, rounded up so a live grant never reads as over.</param>
public sealed record GrantSummary(string Id, string Kind, string Client, string Scope, string Field, int SecondsLeft)
{
    /// <summary>A credential grant's row.</summary>
    /// <param name="grant">The grant, as the owner's cache lists it.</param>
    /// <returns>Its id, client, entry, field and remaining seconds.</returns>
    public static GrantSummary From(GrantInForce grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new GrantSummary(
            GrantId.Of(grant.Key),
            "credential",
            grant.Approved.Client,
            grant.Approved.Entry,
            grant.Key.Field,
            SecondsOf(grant.Remaining));
    }

    /// <summary>A timed grant's row for a repeated <c>keypaste run --session</c> or an agent's run.</summary>
    /// <param name="grant">The grant, as the owner's env grants list it.</param>
    /// <returns>Its id, the project and profile, and remaining seconds.</returns>
    public static GrantSummary From(EnvGrantInForce grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new GrantSummary(
            GrantId.OfEnv(grant.Key),
            grant.Client is null ? "env" : "run",
            grant.Client ?? EnvClient,
            $"{grant.Project} · {grant.Profile}",
            "set",
            SecondsOf(grant.Remaining));
    }

    /// <summary>Every grant an activity holds, agents' and runs' alike, soonest to end first.</summary>
    /// <param name="activity">What the owner's current session holds.</param>
    /// <returns>One row per grant.</returns>
    public static IReadOnlyList<GrantSummary> Of(ApproverActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return [.. activity.Grants.Select(From).Concat(activity.EnvGrants.Select(From)).OrderBy(grant => grant.SecondsLeft)];
    }

    /// <summary>Who an env grant is listed as given to.</summary>
    public const string EnvClient = "keypaste run";

    private static int SecondsOf(TimeSpan remaining) => (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
}

/// <summary>Asks the owner which grants its current session holds.</summary>
public sealed record GrantsRequest
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The grants in force, or why none were listed.</summary>
/// <param name="Answered">Whether the owner admitted the request at all.</param>
/// <param name="Grants">The grants, soonest to end first.</param>
/// <param name="Complete">Whether these are all of them; a reply that would not fit one frame drops rows and says so.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record GrantsReply(bool Answered, IReadOnlyList<GrantSummary> Grants, bool Complete, string Reason);

/// <summary>Asks the owner to end grants of its current session.</summary>
/// <param name="Ids">The ids to end.</param>
/// <param name="Client">Ends every grant given to this client, or null.</param>
/// <param name="All">Ends every grant.</param>
public sealed record RevokeGrantsRequest(IReadOnlyList<string> Ids, string? Client, bool All)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>How many grants were ended, or why none were.</summary>
/// <param name="Revoked">How many grants ended.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record RevokeGrantsReply(int Revoked, string Reason);

/// <summary>Asks the owner to lock now.</summary>
public sealed record LockRequest
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>Whether the owner is locking.</summary>
/// <param name="Locking">Whether the lock was started; it completes after this reply leaves.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record LockReply(bool Locking, string Reason);

/// <summary>
/// Asks the owner to release a set to a runner holding a scoped token, which the owner verifies
/// in place of asking a person.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> leaves the token out, so no interpolated request can print it.
/// </remarks>
/// <param name="Token">The token, verified only by the owner.</param>
/// <param name="Project">The project whose set is asked for.</param>
/// <param name="Profile">The profile asked for.</param>
/// <param name="Command">The command the runner will start, one argument per item.</param>
/// <param name="Directory">The directory it will start in.</param>
public sealed record TokenEnvRequest(string Token, string Project, string Profile, IReadOnlyList<string> Command, string Directory)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the token left out.</summary>
    /// <returns>The set asked for, never the token.</returns>
    public override string ToString() => $"TokenEnvRequest {{ Project = {Project}, Profile = {Profile}, Token = <redacted> }}";
}

/// <summary>One variable of an agent's run named by a <c>kp://</c> reference.</summary>
/// <param name="Name">The variable name the command sees.</param>
/// <param name="Reference">The reference, as the agent wrote it.</param>
public sealed record RunReference(string Name, string Reference);

/// <summary>
/// Asks the owner to release approved secrets for a command an agent's bridge will start with them in
/// its environment.
/// </summary>
/// <remarks>
/// The owner runs nothing. The program, command and directory are the bridge's claim, as a runner's
/// are (THREATS.md T-30): they are what the person is shown and what the bridge starts.
/// </remarks>
public sealed record RunRequest
{
    /// <summary>The program's absolute path, as the bridge resolved it and will start it.</summary>
    public required string Program { get; init; }

    /// <summary>The command as the agent named it: its first item named the program.</summary>
    public required IReadOnlyList<string> Command { get; init; }

    /// <summary>The directory, every link resolved.</summary>
    public required string Directory { get; init; }

    /// <summary>The env project whose set is asked for, or null in reference mode.</summary>
    public string? Project { get; init; }

    /// <summary>The project's profile.</summary>
    public string Profile { get; init; } = EnvProfileNames.Default;

    /// <summary>Only these keys of the set, or null for the whole set.</summary>
    public IReadOnlyList<string>? Keys { get; init; }

    /// <summary>Each variable and its reference, or null in set mode.</summary>
    public IReadOnlyList<RunReference>? References { get; init; }

    /// <summary>The agent's stated reason, verbatim and untrusted (THREATS.md T-2).</summary>
    public required string Reason { get; init; }

    /// <summary>The globs the bridge was configured with.</summary>
    public required IReadOnlyList<string> Exposure { get; init; }

    /// <summary>What the client called itself. Unauthenticated.</summary>
    public string? ClientName { get; init; }

    /// <summary>What version the client claimed. Unauthenticated.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>The bridge's <c>--client-label</c>, raw, as <see cref="CredentialRequest.ClientLabel"/> is.</summary>
    public string? ClientLabel { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The owner's answer to a <see cref="RunRequest"/>: the variables, or why none.</summary>
/// <param name="Set">The variables, with values only when its outcome is <see cref="EnvOutcome.Resolved"/>.</param>
/// <param name="Method">How it was decided, for the bridge's audit line.</param>
/// <param name="Reason">keypaste's own words.</param>
public sealed record RunReply(EnvResolved Set, AuditMethod Method, string Reason)
{
    /// <summary>How long the grant that served or was given lasts, in seconds; zero for "allow once".</summary>
    public int GrantedSeconds { get; init; }

    /// <summary>Each entry released or asked about, as <see cref="ApprovalPrompt.Shown"/> writes it.</summary>
    public IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>The session that answered, or null when none did.</summary>
    public string? Session { get; init; }

    /// <summary>A description with every value left out.</summary>
    /// <returns>The outcome and method, never a value.</returns>
    public override string ToString() =>
        $"RunReply {{ Outcome = {Set.Outcome}, Method = {Method}, Variables = {Set.Variables.Count} }}";
}
