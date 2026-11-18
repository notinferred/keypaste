using System.Text.Json;

namespace Keypaste.Core.Audit;

/// <summary>The authorization answer a bridge gave.</summary>
public enum AuditDecision
{
    /// <summary>Nothing was released.</summary>
    Denied = 0,

    /// <summary>The request was allowed.</summary>
    Granted = 1,
}

/// <summary>How a decision was reached.</summary>
/// <remarks>
/// An agent reads a refusal and decides whether to try again: <see cref="OutOfScope"/> means never,
/// <see cref="Prompt"/> with <see cref="AuditDecision.Denied"/> means a person said no, and only
/// <see cref="Busy"/> and <see cref="TimedOut"/> mean nobody got to it.
/// Only <see cref="Exposure"/>, <see cref="Prompt"/>, <see cref="Allowed"/>, <see cref="AlwaysAllowed"/>, <see cref="Automatic"/>,
/// <see cref="Token"/>, <see cref="Narrowing"/>, <see cref="RulesChanged"/>, <see cref="Merged"/> and <see cref="Removed"/> ever accompany <see cref="AuditDecision.Granted"/>.
/// <see cref="Allowed"/>, <see cref="Automatic"/> and <see cref="AlwaysAllowed"/> are the releases no person saw, so they must
/// never be logged as <see cref="Prompt"/>, which claims a person acted on this request.
/// Adding a member means adding a case to <c>AuditLog.Wire</c>.
/// </remarks>
public enum AuditMethod
{
    /// <summary>The vault could not be opened, so there was nothing to answer with.</summary>
    VaultLocked = 0,

    /// <summary>No approval path existed. Written by keypaste 2.1; kept because the log is
    /// append-only and old lines still mean what they meant when written.</summary>
    NotImplemented = 1,

    /// <summary>The entry named lies outside what this server was told it may expose.</summary>
    OutOfScope = 2,

    /// <summary>The arguments did not satisfy the tool's schema.</summary>
    InvalidRequest = 3,

    /// <summary>Allowed because everything named lay inside the configured exposure. Listing names
    /// only; releasing a credential always needs a person or a mode the person set.</summary>
    Exposure = 4,

    /// <summary>A person was shown this specific request and answered it (docs/PRODUCT.md law 3.2).</summary>
    Prompt = 5,

    /// <summary>Released without asking by an Always allow row a person confirmed, never a person's
    /// approval of this request. It records the reason given for <em>this</em> request, the one nobody read.</summary>
    AlwaysAllowed = 6,

    /// <summary>Nobody answered inside the window. Silence is a denial.</summary>
    TimedOut = 7,

    /// <summary>The client gave up on the request, or went away, before anyone answered.</summary>
    Cancelled = 8,

    /// <summary>No approver was reachable, so there was nobody to ask.</summary>
    NoApprover = 9,

    /// <summary>Another request was in front of a person and this one could not wait its turn.</summary>
    Busy = 10,

    /// <summary>The same request was refused a moment ago and has not served its cooldown.</summary>
    Cooldown = 11,

    /// <summary>Asking, resolving or reading went wrong. Fail closed (docs/PRODUCT.md law 3.7).</summary>
    Failed = 12,

    /// <summary>A tool was called before the <c>initialize</c> handshake completed, so nothing is
    /// known about who is calling.</summary>
    /// <remarks>
    /// A client that ignores the protocol still gets its request read. Answering would put it to a
    /// person, or release it under the caller's mode, with the caller missing from both the dialog and the log —
    /// the one field the human judges by.
    /// </remarks>
    NotInitialized = 15,

    /// <summary>The request was authorized and the reply carrying the field would not fit one
    /// message, so nothing was returned.</summary>
    /// <remarks>
    /// A word of its own rather than a reuse: <see cref="Failed"/> says asking went wrong and here it
    /// went right, while <see cref="Prompt"/> beside <see cref="AuditDecision.Denied"/> already means
    /// a person said no. The reason names which authority the release had, so an Automatic release is
    /// never written up as a human act. Like <see cref="Unfillable"/>, a denial that always follows an authorization.
    /// </remarks>
    Undeliverable = 16,

    /// <summary>The request did not come from a connection attached to the session now holding the
    /// vault it named, so it was not considered.</summary>
    /// <remarks>
    /// Distinct from <see cref="VaultLocked"/>: the vault may be unlocked, but not in the session
    /// this request belonged to, or not by the process it reached.
    /// </remarks>
    NoSession = 17,

    /// <summary>The vault's owner holds a copy that no longer matches its file, so it answered
    /// nothing from it.</summary>
    /// <remarks>
    /// Somebody else wrote the file, which lasts until a person reloads it, or a change made in the
    /// owner has not been saved yet, or a reload or a merge replaced the copy a request was waiting on, which
    /// withdrew it. The reason says which.
    /// </remarks>
    VaultChanged = 18,

    /// <summary>Released or refused under a scoped token rather than a prompt or a mode.</summary>
    Token = 19,

    /// <summary>A share link was created.</summary>
    ShareCreated = 20,

    /// <summary>A share link was revoked.</summary>
    ShareRevoked = 21,

    /// <summary>The agent's settings give it no values, so a request for one was refused before anything was resolved.</summary>
    InjectOnly = 22,

    /// <summary>Released without asking because the agent's mode for the entry is Automatic, a setting the person confirmed.</summary>
    /// <remarks>Nobody was shown this request, so it is never written as <see cref="Prompt"/>.</remarks>
    Automatic = 23,

    /// <summary>The agent's settings do not let it run commands, so a run was refused before anything was resolved.</summary>
    RunOff = 24,

    /// <summary>A change to an agent's settings that gives it nothing more, applied at once without asking.</summary>
    Narrowing = 25,

    /// <summary>A terminal's edit asked for what the vault already held, so nothing was written and nobody was asked.</summary>
    Unchanged = 26,

    /// <summary>The request was authorized and the value asked for holds a KeePass placeholder keypaste does not fill in, so nothing was released.</summary>
    /// <remarks>
    /// Not <see cref="Failed"/>: nothing went wrong, and asking again ends the same way until a person edits the
    /// item, so the field cools down as a refusal's does. The reason names the placeholder, never a value.
    /// </remarks>
    Unfillable = 27,

    /// <summary>The vault's holder merged another program's save with the changes its copy held and saved the result.</summary>
    /// <remarks>Released nothing and was asked of nobody: the line names the items the merge decided between, never a value.</remarks>
    Merged = 28,

    /// <summary>A browser asked to pair while the person had not armed pairing, so it was refused before anybody was asked.</summary>
    NotArmed = 29,

    /// <summary>A browser presented a pairing the vault does not hold for its build and key, so its check was refused.</summary>
    NotPaired = 30,

    /// <summary>The person removed a browser's pairing, which released nothing and took its access away.</summary>
    Removed = 31,

    /// <summary>Released, or a command started, without asking because the agent's mode is Automatic with commands, a setting the person confirmed.</summary>
    /// <remarks>Nobody was shown this request, so it is never written as <see cref="Prompt"/>; keypaste approved nothing about what the command does (T-35).</remarks>
    AutomaticCommands = 32,

    /// <summary>
    /// Released without asking because the field's category is Allow for every agent: a URL, a username shown, or a rule the person
    /// confirmed; or an item's needs-update tag added or removed, which every agent may do without asking.
    /// </summary>
    /// <remarks>Nobody was shown this request, so it is never written as <see cref="Prompt"/>.</remarks>
    Allowed = 33,

    /// <summary>Refused without asking because the field's category is Blocked for agents, as a passkey's always is.</summary>
    Blocked = 34,

    /// <summary>This computer's agent rules changed where it was asked, as <c>keypaste clients</c> does, with nobody shown a confirmation.</summary>
    /// <remarks>Released nothing; any program the person runs can run that verb, so the line never claims a person acted.</remarks>
    RulesChanged = 35,

    /// <summary>The vault's owner signed the chain up to this line with the vault's checkpoint key.</summary>
    /// <remarks>Released nothing and was asked of nobody; <c>keypaste log</c> and Activity do not list it.</remarks>
    Checkpoint = 36,

    /// <summary>One agent's lookups of one UTC day, folded into one line by the vault's owner once they were 30 days old.</summary>
    Folded = 37,

    /// <summary>Whole UTC days older than the period the person keeps the log for, removed by the vault's owner.</summary>
    Expired = 38,

    /// <summary>The vault's owner sealed the log it compacted with the vault's checkpoint key.</summary>
    /// <remarks>Released nothing and was asked of nobody; <c>keypaste log</c> and Activity do not list it.</remarks>
    Compaction = 39,
}

/// <summary>How many lookups of one tool with one decision a summary line folds.</summary>
/// <param name="Tool">The tool, <see cref="AuditTools.List"/> or <see cref="AuditTools.Find"/>.</param>
/// <param name="Decision">Whether they named anything.</param>
/// <param name="Count">How many lines.</param>
public sealed record AuditFoldCount(string Tool, AuditDecision Decision, int Count);

/// <summary>One agent's lookups on one UTC day, folded into one line.</summary>
/// <param name="Day">The UTC day, <c>yyyy-MM-dd</c>.</param>
/// <param name="First">The first folded line's timestamp.</param>
/// <param name="Last">The last folded line's timestamp.</param>
/// <param name="Counts">The folded lines by tool and decision.</param>
/// <param name="Sha256">SHA-256 over the folded lines' hashes, each followed by a newline, in file order.</param>
public sealed record AuditSummary(string Day, string First, string Last, IReadOnlyList<AuditFoldCount> Counts, string Sha256);

/// <summary>Whole UTC days removed from the log.</summary>
/// <param name="From">The first removed day, <c>yyyy-MM-dd</c>.</param>
/// <param name="To">The last removed day.</param>
/// <param name="Count">How many requests the removed lines recorded, a summary's folded lookups each counted.</param>
/// <param name="Sha256">SHA-256 over the removed lines' hashes, each followed by a newline, in file order.</param>
public sealed record AuditRemoval(string From, string To, int Count, string Sha256);

/// <summary>Who asked.</summary>
/// <remarks>
/// <paramref name="Name"/> and <paramref name="Version"/> are <b>unauthenticated</b>: any process
/// that can spawn the server can claim any name, so they are audit fields and never authorization
/// inputs (THREATS.md T-3). <paramref name="Label"/> cannot be chosen by whoever connects, but
/// whoever spawns the server does choose it, so it identifies a configuration rather than a caller.
/// </remarks>
/// <param name="Name">The client's self-declared name, sanitized and capped.</param>
/// <param name="Version">The client's self-declared version.</param>
/// <param name="Label">The name a human gave this server via <c>--client-label</c>.</param>
public sealed record AuditClient(string? Name, string? Version, string? Label)
{
    /// <summary>The longest client-supplied name, version or label kept.</summary>
    public const int MaximumTextLength = 64;

    /// <summary>A client that said nothing about itself.</summary>
    public static AuditClient Unknown { get; } = new(null, null, null);

    /// <summary>Who asked, as a line records them: each part sanitized and capped, and one that was not said left absent.</summary>
    /// <param name="name">The client's self-declared name, or null.</param>
    /// <param name="version">The client's self-declared version, or null.</param>
    /// <param name="label">The label its configuration gave it, or null.</param>
    /// <returns>The client.</returns>
    public static AuditClient Of(string? name, string? version, string? label) =>
        new(Clean(name), Clean(version), Clean(label));

    private static string? Clean(string? value) =>
        value is null ? null : EntryNameSanitizer.Sanitize(value, MaximumTextLength).Text;
}

/// <summary>What the operating system said of the code signature of the program on the other end of the owner's pipe.</summary>
public enum CallerSignature
{
    /// <summary>The check could not be made or did not finish.</summary>
    Unknown = 0,

    /// <summary>The program carries no signature.</summary>
    NotSigned = 1,

    /// <summary>The program is signed with no signer identity, as macOS signs every program a linker builds for Apple silicon.</summary>
    AdHoc = 2,

    /// <summary>The program carries a signature that does not verify.</summary>
    Invalid = 3,

    /// <summary>The program's signature verifies.</summary>
    Valid = 4,
}

/// <summary>The program on the other end of the owner's pipe, as the operating system names it when the connection is accepted.</summary>
/// <remarks>
/// Recorded, never decided on: a process id can be reused before its path is read, and a process of the
/// same user can inject into or pose as a signed program (THREATS.md T-10).
/// </remarks>
/// <param name="Pid">The connecting process's id, or null when the system did not say.</param>
/// <param name="Path">Its executable, sanitized and capped, or null.</param>
/// <param name="Signature">What its code signature checked out as, or null where the system has no code signing, as on Linux.</param>
/// <param name="Signer">The signer's Windows certificate subject or macOS team ID, sanitized and capped, or null.</param>
public sealed record AuditCaller(int? Pid, string? Path, CallerSignature? Signature, string? Signer)
{
    /// <summary>The longest executable path kept.</summary>
    public const int PathLength = DisplayTextSanitizer.MaximumLength;

    /// <summary>The longest signer kept.</summary>
    public const int SignerLength = 256;

    /// <summary>The caller as a line records it, its path and signer sanitized, on one line and capped.</summary>
    /// <param name="pid">The process id, or null.</param>
    /// <param name="path">Its executable, or null.</param>
    /// <param name="signature">Its signature state, or null where there is no code signing.</param>
    /// <param name="signer">Its signer, or null.</param>
    /// <returns>The caller.</returns>
    public static AuditCaller Of(int? pid, string? path, CallerSignature? signature, string? signer) =>
        new(pid, Clean(path, PathLength), signature, Clean(signer, SignerLength));

    private static string? Clean(string? value, int maximumLength) =>
        value is null || DisplayTextSanitizer.Sanitize(value.ReplaceLineEndings(" ").Replace('\t', ' '), maximumLength).Text is not { Length: > 0 } text ? null : text;
}

/// <summary>The arguments a tool was called with, reduced to what is safe to keep.</summary>
/// <remarks>
/// Nothing here is ever a field <em>value</em>: <see cref="Field"/> records which field was asked
/// for, never its contents, and no property on this type can hold a secret.
/// </remarks>
public sealed record AuditArgs
{
    /// <summary>The longest agent-written reason kept verbatim in the log.</summary>
    public const int ReasonExcerptLength = 200;

    /// <summary>The longest <c>entry</c> argument kept.</summary>
    public const int EntryLength = 128;

    /// <summary>A call that takes no arguments, such as <c>list_entry_names</c>.</summary>
    public static AuditArgs None { get; } = new();

    /// <summary>The <c>entry</c> argument as the agent wrote it, sanitized and capped.</summary>
    public string? Entry { get; init; }

    /// <summary>Whether that argument was a handle, a path, or neither.</summary>
    public EntryAddressKind? EntryKind { get; init; }

    /// <summary>The UUID, as hex, of the item the request was decided about, which tells it from another of its name.</summary>
    public string? Item { get; init; }

    /// <summary>Which field was requested, or <c>invalid</c> when it was not one keypaste knows.</summary>
    public string? Field { get; init; }

    /// <summary>The opening of the agent's stated reason, sanitized and capped.</summary>
    public string? ReasonExcerpt { get; init; }

    /// <summary>The reason's true length, so truncation is visible rather than silent.</summary>
    public int? ReasonLength { get; init; }

    /// <summary>Lowercase hex SHA-256 of the raw reason.</summary>
    /// <remarks>
    /// It carries its weight on a <see cref="AuditMethod.AlwaysAllowed"/> line, where nobody was shown
    /// anything: comparing that hash with the earlier <see cref="AuditMethod.Prompt"/> line's is how
    /// a reason that changed after approval becomes visible (THREATS.md T-12).
    /// </remarks>
    public string? ReasonSha256 { get; init; }

    /// <summary>What a search asked for, each part sanitized and capped, or null for any other request.</summary>
    public AuditQuery? Query { get; init; }

    /// <summary>Which linked set the line belongs to, and the field's place in it, or null for any other request.</summary>
    public AuditSet? Set { get; init; }

    /// <summary>Reduces a search to what the log keeps: each part of its query the agent wrote.</summary>
    /// <param name="query">What the agent searched for.</param>
    /// <returns>The arguments, holding no value.</returns>
    public static AuditArgs ForFind(AccessQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        static string? Prose(string? part) => string.IsNullOrWhiteSpace(part) ? null : EntryNameSanitizer.SanitizeProse(part, EntryLength).Text;

        return new AuditArgs
        {
            Query = new AuditQuery(
                Prose(query.Text),
                Prose(query.Host),
                query.Kind is { } kind ? JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()) : null,
                string.IsNullOrWhiteSpace(query.RelatedTo) ? null : EntryNameSanitizer.Sanitize(query.RelatedTo, EntryLength).Text,
                string.IsNullOrWhiteSpace(query.Project) ? null : EntryNameSanitizer.Sanitize(query.Project, EntryLength).Text),
        };
    }

    /// <summary>Reduces a run request to what the log keeps: the set's group, when it named one, and the reason.</summary>
    /// <param name="group">The set's name (<see cref="EnvProfileNames.SetName"/>), or null for references, whose entries the line lists instead.</param>
    /// <param name="reason">The agent's stated reason, verbatim.</param>
    /// <returns>The arguments, holding no value.</returns>
    public static AuditArgs ForRun(string? group, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return new AuditArgs
        {
            Entry = group is null ? null : EntryNameSanitizer.SanitizePath(group, maximumLength: EntryLength).Text,
            EntryKind = group is null ? null : EntryAddressKind.Path,
            ReasonExcerpt = EntryNameSanitizer.SanitizeProse(reason, ReasonExcerptLength).Text,
            ReasonLength = reason.Length,
            ReasonSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason))),
        };
    }

    /// <summary>Reduces a credential request to what the log keeps.</summary>
    /// <remarks>
    /// The reason is unbounded text written by the agent whose purpose is to persuade a person, which
    /// makes it the likeliest injection payload in the protocol (THREATS.md T-2), and <c>keypaste
    /// log</c> renders it in a table a human reads. Keeping all three of an excerpt, the true length
    /// and a hash serves that reader without letting the log quietly lie about what was cut.
    /// </remarks>
    public static AuditArgs ForCredentialRequest(string entry, string? field, string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reason);

        // Prose, so the slashes survive: a reason impersonates nothing.
        var excerpt = EntryNameSanitizer.SanitizeProse(reason, ReasonExcerptLength).Text;

        // Segment-wise, so the separators survive. An audit line whose whole job is to say *which
        // entry* was asked for must not render env/dev/STRIPE_KEY as "env dev STRIPE_KEY".
        var entryText = EntryNameSanitizer.SanitizePath(entry, maximumLength: EntryLength).Text;

        return new AuditArgs
        {
            Entry = entryText,
            EntryKind = EntryHandle.Classify(entry),
            Field = field ?? "invalid",
            ReasonExcerpt = excerpt,
            ReasonLength = reason.Length,
            ReasonSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason))),
        };
    }
}

/// <summary>A line's place in a linked set: one line per field released, or one for a set refused.</summary>
/// <param name="Id">The set's random identifier, which every line of one set shares, or null where the owner gave it none.</param>
/// <param name="Position">The field's place, from 1, or null on the one line of a set refused.</param>
/// <param name="Of">How many fields the set named.</param>
public sealed record AuditSet(string? Id, int? Position, int Of);

/// <summary>The parts of an agent's search, as its line keeps them.</summary>
/// <param name="Text">The text searched for, sanitized as prose and capped, or null.</param>
/// <param name="Host">The host or URL searched for, sanitized as prose and capped, or null.</param>
/// <param name="Kind">The kind searched for, in snake case, or null.</param>
/// <param name="Related">The handle of the item searched around, sanitized and capped, or null.</param>
/// <param name="Project">The project searched within, sanitized and capped, or null.</param>
public sealed record AuditQuery(string? Text, string? Host, string? Kind, string? Related, string? Project = null);

/// <summary>One line of the audit trail: who asked for what, and what keypaste did about it.</summary>
/// <remarks>
/// <b>Never present, at any schema version:</b> a password, user name, URL, notes, the master
/// password, or any entry title read out of the vault. The only entry text recorded is the argument
/// the agent itself supplied.
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>The schema version, written on every line from the first.</summary>
    /// <remarks>
    /// Version 2 adds <c>prev</c> and <c>hash</c> and redefines <c>seq</c> as a line's position in the
    /// chain rather than a count of what one process wrote. Every release of keypaste wrote version 2,
    /// so a version 1 line is reported as written by something else.
    /// </remarks>
    public const int SchemaVersion = 2;

    /// <summary>The name of the tool that was called.</summary>
    public required string Tool { get; init; }

    /// <summary>Who called it.</summary>
    public required AuditClient Client { get; init; }

    /// <summary>The program on the owner's pipe that asked, or null for a line no connection asked for.</summary>
    public AuditCaller? Caller { get; init; }

    /// <summary>What they asked for.</summary>
    public AuditArgs Args { get; init; } = AuditArgs.None;

    /// <summary>Whether anything was released.</summary>
    public required AuditDecision Decision { get; init; }

    /// <summary>How that was decided.</summary>
    public required AuditMethod Method { get; init; }

    /// <summary>keypaste's own one-line explanation. Trusted text, unlike the agent's reason.</summary>
    public required string Reason { get; init; }

    /// <summary>The globs this server was permitted to name, as configured.</summary>
    /// <remarks>
    /// On every line, including listings: what this server could ever have named cannot be recovered
    /// from a configuration file that has been edited since.
    /// </remarks>
    public IReadOnlyList<string> Exposure { get; init; } = [];

    /// <summary>The unlocked session that answered, or null when the request reached none.</summary>
    public string? Session { get; init; }

    /// <summary>The vault the request named, as <c>VaultIdentity.Key</c>, or null when none was configured.</summary>
    /// <remarks>Absent from lines written before it existed, which a reader counts for no vault in particular.</remarks>
    public string? Vault { get; init; }

    /// <summary>Each entry a release named or a run asked about, as <c>ApprovalPrompt.Shown</c> writes it, or null.</summary>
    public IReadOnlyList<string>? Entries { get; init; }

    /// <summary>The command a run started or asked to, one line, at most <see cref="CommandLength"/> characters, or null.</summary>
    /// <remarks>Its full argv is <see cref="CommandSha256"/>, so a tail past the cut is still committed to.</remarks>
    public string? Command { get; init; }

    /// <summary>Lowercase hex SHA-256 of the run's canonical argv, program first, or null.</summary>
    public string? CommandSha256 { get; init; }

    /// <summary>On a checkpoint line, the HMAC-SHA256 its owner wrote over the chain up to it, as lowercase hex; otherwise null.</summary>
    public string? Checkpoint { get; init; }

    /// <summary>On a summary line, the lookups it folds; otherwise null.</summary>
    public AuditSummary? Summary { get; init; }

    /// <summary>On a removal line, the days it removed; otherwise null.</summary>
    public AuditRemoval? Removal { get; init; }

    /// <summary>On a compaction record, its seal as <see cref="AuditCompaction"/> writes it; otherwise null.</summary>
    public string? Compaction { get; init; }

    /// <summary>The longest command head a line keeps.</summary>
    public const int CommandLength = 256;

    /// <summary>The hash a run line commits its whole command to: every item, NUL-terminated, in UTF-8.</summary>
    /// <param name="argv">The program's absolute path, then the command's arguments.</param>
    /// <returns>Lowercase hex SHA-256.</returns>
    public static string HashOf(IEnumerable<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);

        var joined = new StringBuilder();

        foreach (var item in argv)
        {
            joined.Append(item).Append('\0');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined.ToString())));
    }
}
