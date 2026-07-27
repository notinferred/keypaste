using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.Core.Policy;

/// <summary>One <c>[[allow]]</c> rule: who may read what, for how long, how often.</summary>
/// <remarks>
/// <para>
/// A rule is a <b>narrowing</b> of what a person would otherwise be asked about, never a parallel
/// grant of authority. It is evaluated after the bridge's own <c>--expose</c> has already been
/// applied to a resolved entry name, so no rule can reach anything the exposure did not already
/// permit. DECISIONS.md D-0029.
/// </para>
/// <para>
/// <b>The pattern is an <see cref="EntryExposure"/>, not a second matcher.</b> D-0021 chose the
/// matching domain in Stage 2.1 specifically so the policy file would not invent a subtly different
/// one, and reusing the type rather than the algorithm is what carries the six properties that come
/// with it: group path and title matched separately so a title can never impersonate a path;
/// matching on the raw name before sanitization; ordinal and case-sensitive, because a
/// case-insensitive match is a wider match; the length and count caps; and an empty pattern list
/// allowing nothing.
/// </para>
/// <para>
/// <b>The client is the operator's <c>--client-label</c>, never the name the agent asserts.</b>
/// THREATS.md T-3: the asserted name is unauthenticated, so it may be an audit field and never an
/// authorization input. The label is written by a human into the MCP client's configuration. That is
/// a real improvement and a small one — whoever <em>spawns</em> the bridge still chooses it — so the
/// honest claim, and the one the documentation makes, is that client-scoped policy narrows
/// convenience rather than authority.
/// </para>
/// </remarks>
public sealed class PolicyRule
{
    /// <summary>The section name a rule is written under.</summary>
    public const string SectionName = "allow";

    /// <summary>The client value meaning "any labelled client".</summary>
    public const string AnyClient = "*";

    /// <summary>The longest <c>client</c> value accepted.</summary>
    public const int MaximumClientLength = 64;

    /// <summary>The largest <c>max_per_hour</c> accepted.</summary>
    /// <remarks>
    /// It bounds the memory one rule's sliding window costs, which is what lets that window keep
    /// exact timestamps rather than approximating with buckets.
    /// </remarks>
    public const int MaximumAllowance = 1000;

    /// <summary>The longest a citation may grow, in characters.</summary>
    internal const int MaximumCitationLength = 160;

    internal const string ClientKey = "client";
    internal const string EntriesKey = "entries";
    internal const string FieldsKey = "fields";
    internal const string MaximumTtlKey = "max_ttl_seconds";
    internal const string MaximumPerHourKey = "max_per_hour";

    private static readonly string[] _knownKeys =
        [ClientKey, EntriesKey, FieldsKey, MaximumTtlKey, MaximumPerHourKey];

    private PolicyRule(
        int ordinal,
        string client,
        EntryExposure scope,
        IReadOnlyList<string> fields,
        int maximumTtlSeconds,
        int? maximumPerHour)
    {
        Ordinal = ordinal;
        Client = client;
        Scope = scope;
        Fields = fields;
        MaximumTtlSeconds = maximumTtlSeconds;
        MaximumPerHour = maximumPerHour;
    }

    /// <summary>Which rule this is, counting from one in the order the file lists them.</summary>
    public int Ordinal { get; }

    /// <summary>The client label this rule applies to, or <see cref="AnyClient"/>.</summary>
    public string Client { get; }

    /// <summary>The entry names this rule covers.</summary>
    public EntryExposure Scope { get; }

    /// <summary>The fields this rule releases, in the order written.</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>The longest grant this rule allows, before the operator's own ceiling applies.</summary>
    public int MaximumTtlSeconds { get; }

    /// <summary>What this rule is called in an audit line and in <c>keypaste policy ls</c>.</summary>
    public string Id => string.Create(CultureInfo.InvariantCulture, $"{SectionName}#{Ordinal}");

    /// <summary>How many releases an hour this rule allows, or null for no limit.</summary>
    public int? MaximumPerHour { get; }

    /// <summary>Builds a rule from one parsed section, refusing anything it cannot use.</summary>
    /// <param name="table">The section.</param>
    /// <param name="ordinal">Which rule this is, counting from one.</param>
    /// <param name="rule">The rule, when the section is usable.</param>
    /// <param name="error">A message in the shape <c>line 7: ...</c>, or empty on success.</param>
    /// <returns><see langword="true"/> if the section was a usable rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    /// <remarks>
    /// <b>Nothing defaults.</b> Every key but <c>max_per_hour</c> is required, and a missing one is a
    /// refusal rather than a fallback. The defaults that suggest themselves are each a way for a rule
    /// to silently become wider than what was written: an absent <c>fields</c> meaning every field,
    /// an absent <c>entries</c> meaning everything, an absent <c>client</c> meaning anyone. A typo in
    /// a key name would then quietly select one of them.
    /// </remarks>
    public static bool TryCreate(
        TomlTable table,
        int ordinal,
        [NotNullWhen(true)] out PolicyRule? rule,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(table);

        rule = null;

        if (!string.Equals(table.Name, SectionName, StringComparison.Ordinal))
        {
            error = Problem(table.Line, $"'{Safe(table.Name)}' is not a section keypaste understands");
            return false;
        }

        foreach (var pair in table.Pairs)
        {
            if (Array.IndexOf(_knownKeys, pair.Key) < 0)
            {
                error = Problem(pair.Line, $"'{Safe(pair.Key)}' is not a key keypaste understands");
                return false;
            }
        }

        if (!TryText(table, ClientKey, out var client, out error)
            || !TryStrings(table, EntriesKey, out var entries, out error)
            || !TryStrings(table, FieldsKey, out var fields, out error)
            || !TryNumber(table, MaximumTtlKey, out var ttl, out error))
        {
            return false;
        }

        if (client.Length == 0 || client.Length > MaximumClientLength)
        {
            error = Problem(Line(table, ClientKey), $"a client label must be 1 to {MaximumClientLength} characters");
            return false;
        }

        if (ttl < 1 || ttl > ApprovalLimits.MaximumRequestableTtlSeconds)
        {
            error = Problem(
                Line(table, MaximumTtlKey),
                $"{MaximumTtlKey} must be between 1 and {ApprovalLimits.MaximumRequestableTtlSeconds}");
            return false;
        }

        int? perHour = null;
        if (table.TryGet(MaximumPerHourKey, out _))
        {
            if (!TryNumber(table, MaximumPerHourKey, out var cap, out error))
            {
                return false;
            }

            if (cap < 1 || cap > MaximumAllowance)
            {
                error = Problem(
                    Line(table, MaximumPerHourKey),
                    $"{MaximumPerHourKey} must be between 1 and {MaximumAllowance}");
                return false;
            }

            perHour = cap;
        }

        if (fields.Count == 0)
        {
            error = Problem(Line(table, FieldsKey), $"{FieldsKey} must name at least one field");
            return false;
        }

        foreach (var field in fields)
        {
            if (!CredentialFields.IsReleasable(field))
            {
                error = Problem(
                    Line(table, FieldsKey),
                    $"'{Safe(field)}' is not a field keypaste releases; use {string.Join(", ", CredentialFields.All)}");
                return false;
            }
        }

        if (entries.Count == 0)
        {
            error = Problem(Line(table, EntriesKey), $"{EntriesKey} must name at least one pattern");
            return false;
        }

        if (!IsRenderedAsWritten(client))
        {
            error = Problem(Line(table, ClientKey), "the client label contains a character that cannot be shown as written");
            return false;
        }

        foreach (var glob in entries)
        {
            if (!IsPathRenderedAsWritten(glob))
            {
                error = Problem(
                    Line(table, EntriesKey),
                    "a pattern contains a character that cannot be shown as written");
                return false;
            }
        }

        if (!EntryExposure.TryCreate(entries, out var scope, out var globError))
        {
            error = Problem(Line(table, EntriesKey), $"{EntriesKey}: {globError}");
            return false;
        }

        rule = new PolicyRule(ordinal, client, scope, fields, ttl, perHour);
        error = string.Empty;
        return true;
    }

    /// <summary>Whether this rule covers a request, before any limit is applied.</summary>
    /// <param name="clientLabel">The operator's label for the asking bridge, or null if it set none.</param>
    /// <param name="name">The resolved, unsanitized entry name.</param>
    /// <param name="field">The field asked for.</param>
    /// <returns><see langword="true"/> if all three match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="field"/> is null.</exception>
    /// <remarks>
    /// <b>A bridge with no label matches no rule, including one written <c>"*"</c>.</b> The star
    /// means "any client the operator gave a name to", not "any client at all" — a rule is a standing
    /// grant, and a standing grant to something nobody named is not something to hand out because a
    /// key was left blank.
    /// <para>
    /// <b><see cref="MaximumTtlSeconds"/> is not one of the three predicates.</b> A rule whose
    /// ceiling is below what was asked for clamps the lifetime rather than failing to match.
    /// Treating it as a predicate would create a class of silent non-matches that nothing in
    /// <c>keypaste policy ls</c> could explain.
    /// </para>
    /// </remarks>
    public bool Matches(string? clientLabel, EntryName name, string field)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        if (clientLabel is not { Length: > 0 })
        {
            return false;
        }

        if (!string.Equals(Client, AnyClient, StringComparison.Ordinal)
            && !string.Equals(Client, clientLabel, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var candidate in Fields)
        {
            if (string.Equals(candidate, field, StringComparison.Ordinal))
            {
                return Scope.Allows(name);
            }
        }

        return false;
    }

    /// <summary>Names this rule the way an audit line does.</summary>
    /// <returns>Something like <c>allow#1 (env/dev/**, password)</c>.</returns>
    /// <remarks>
    /// The number is the rule's position in the file, so it is the same heading
    /// <c>keypaste policy ls</c> prints — but editing the file renumbers rules, which is why the
    /// patterns and fields travel with it. Capped so a long glob can never push an audit record
    /// toward its size limit.
    /// </remarks>
    public string Cite()
    {
        var body = $"{Id} ({string.Join(' ', Scope.Globs)}, {string.Join(' ', Fields)})";

        return body.Length <= MaximumCitationLength
            ? body
            : string.Concat(body.AsSpan(0, MaximumCitationLength - 2), "…)");
    }

    /// <summary>
    /// Whether a string survives display unchanged. <see cref="EntryExposure"/> already rejects
    /// control characters and backslashes in a pattern, which is necessary and not sufficient: it
    /// accepts U+202E, zero-width joiners and the Unicode tag block, so a rule that <em>renders</em>
    /// as <c>env/dev/**</c> and <em>means</em> <c>env/**</c> would otherwise be writable. Reusing
    /// the sanitizer as a validator makes every pattern this file holds safe to print by
    /// construction (THREATS.md T-1).
    /// </summary>
    private static bool IsRenderedAsWritten(string text) =>
        !EntryNameSanitizer.Sanitize(text, EntryNameSanitizer.MaximumLength).WasAltered;

    /// <summary>
    /// The same check for a pattern, which keeps its slashes. Plain
    /// <see cref="EntryNameSanitizer.Sanitize(string, int)"/> treats <c>/</c> as structural and would
    /// report every glob as altered.
    /// </summary>
    private static bool IsPathRenderedAsWritten(string glob) =>
        !EntryNameSanitizer.SanitizePath(glob).WasAltered;

    private static bool TryText(TomlTable table, string key, out string text, out string error)
    {
        text = string.Empty;

        if (!table.TryGet(key, out var pair))
        {
            error = Problem(table.Line, $"a rule must set '{key}'");
            return false;
        }

        if (pair.Value.Kind != TomlValueKind.Text)
        {
            error = Problem(pair.Line, $"'{key}' must be a string, not {pair.Value.Describe()}");
            return false;
        }

        text = pair.Value.Text;
        error = string.Empty;
        return true;
    }

    private static bool TryNumber(TomlTable table, string key, out int number, out string error)
    {
        number = 0;

        if (!table.TryGet(key, out var pair))
        {
            error = Problem(table.Line, $"a rule must set '{key}'");
            return false;
        }

        if (pair.Value.Kind != TomlValueKind.Number)
        {
            error = Problem(pair.Line, $"'{key}' must be a whole number, not {pair.Value.Describe()}");
            return false;
        }

        number = pair.Value.Number;
        error = string.Empty;
        return true;
    }

    private static bool TryStrings(TomlTable table, string key, out IReadOnlyList<string> items, out string error)
    {
        items = [];

        if (!table.TryGet(key, out var pair))
        {
            error = Problem(table.Line, $"a rule must set '{key}'");
            return false;
        }

        if (pair.Value.Kind != TomlValueKind.Array)
        {
            error = Problem(pair.Line, $"'{key}' must be an array of strings, not {pair.Value.Describe()}");
            return false;
        }

        items = pair.Value.Items;
        error = string.Empty;
        return true;
    }

    private static int Line(TomlTable table, string key) =>
        table.TryGet(key, out var pair) ? pair.Line : table.Line;

    /// <summary>Makes text from the file safe to put in a message that a terminal will render.</summary>
    private static string Safe(string text) =>
        EntryNameSanitizer.Sanitize(text, MaximumClientLength).Text;

    private static string Problem(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");
}
