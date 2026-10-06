using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>How much of one entry an exposure lets a bridge reach.</summary>
public enum ExposureReach
{
    /// <summary>Nothing: the entry cannot be named.</summary>
    None = 0,

    /// <summary>Its custom fields named like environment variables, through a project tag alone (D-0422).</summary>
    ProjectFields = 1,

    /// <summary>The whole entry, through a pattern naming its place.</summary>
    Entry = 2,
}

/// <summary>
/// The set of entries a bridge is permitted to talk about at all, and how much of each.
/// </summary>
/// <remarks>
/// <para>
/// docs/PRODUCT.md law 3.2 says the default is deny. That is usually read as being about credentials, but
/// entry names are themselves an asset — a complete inventory of a personal vault is what turns a
/// vague request into a targeted one, even with no secret attached (law 3.5, THREATS.md T-4). So
/// the listing surface gets the same treatment: <see cref="Default"/> covers the project variables
/// the product is actually about, and anything wider has to be written down by a human in the MCP
/// client's configuration.
/// </para>
/// <para>
/// <b>A pattern starting <c>tag:</c> selects entries by project membership</b>, read from their own
/// tags as <see cref="ProjectTag"/> reads them, and reaches only their custom fields named like
/// environment variables; any other pattern names a place and reaches the whole entry (D-0422).
/// Reach is decided per entry, so an entry tagged into two environments is reached through either
/// (THREATS.md T-38).
/// </para>
/// <para>
/// <b>Globs match the group path and the title as two separate values</b>, never the joined
/// <see cref="VaultEntry.Path"/>. That is what stops a title from impersonating a path: an entry
/// titled <c>../../prod/ROOT_TOKEN</c> sitting in group <c>env/dev</c> is matched as a
/// <em>title</em>, so it can never satisfy a group pattern and can never escape into
/// <c>env/prod</c>.
/// </para>
/// <para>
/// Matching is done against the <b>raw</b> name, before sanitization, so no change to
/// <see cref="EntryNameSanitizer"/> can ever widen what is exposed. It is ordinal and
/// case-sensitive: a case-insensitive match is a wider match, and widening is not something this
/// type is allowed to do by accident.
/// </para>
/// <para>
/// Since Stage 2.3 this is also what a <c>policy.toml</c> rule matches with. That is DECISIONS.md
/// D-0021 being cashed in: the policy file does not define a second matching domain, it constructs
/// one of these — so every property above is inherited by a rule rather than re-argued for it.
/// </para>
/// </remarks>
public sealed class EntryExposure
{
    /// <summary>The most globs one server may be given.</summary>
    public const int MaximumGlobs = 16;

    /// <summary>The longest a single glob may be.</summary>
    public const int MaximumGlobLength = 128;

    /// <summary>The wildcard segment matching any number of segments, including none.</summary>
    internal const string DoubleStar = "**";

    /// <summary>A title pattern that constrains nothing.</summary>
    internal const string AnyTitle = "*";

    /// <summary>What starts a pattern that selects entries by project tag rather than by place.</summary>
    public const string TagSelectorPrefix = "tag:";

    /// <summary>
    /// The one pattern in force when nobody widened the exposure: every project's variables. Public
    /// because a front end has to be able to say "no <c>--expose</c> was given, so use this" without
    /// spelling out the string.
    /// </summary>
    public const string DefaultGlob = TagSelectorPrefix + ProjectTag.Prefix + "*";

    /// <summary>
    /// The wildcard character itself. <c>internal</c> rather than <c>private</c> because the
    /// naming rule in <c>.editorconfig</c> applies <c>_camelCase</c> to every private field,
    /// constants included, and this repository has no <c>private const</c> anywhere.
    /// </summary>
    internal const char Wildcard = '*';

    private static readonly string[] _defaultGlobs = [DefaultGlob];

    private readonly Rule[] _rules;
    private readonly Selector[] _selectors;

    private EntryExposure(string[] globs, Rule[] rules, Selector[] selectors)
    {
        Globs = globs;
        _rules = rules;
        _selectors = selectors;
    }

    /// <summary>
    /// What a server exposes when nobody said otherwise: the variables of every project, and nothing else.
    /// </summary>
    public static EntryExposure Default { get; } = Create(_defaultGlobs);

    /// <summary>The globs this exposure was built from, in the order given.</summary>
    /// <remarks>
    /// Recorded on every audit line. "What could this server ever have named?" is the first question
    /// a post-incident reader asks, and it cannot be reconstructed from a configuration file that
    /// has been edited since.
    /// </remarks>
    public IReadOnlyList<string> Globs { get; }

    /// <summary>Builds an exposure from globs, rejecting anything malformed.</summary>
    /// <param name="globs">The patterns, typically one per <c>--expose</c> argument.</param>
    /// <param name="exposure">The exposure, when the globs are usable.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="true"/> if every glob was usable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="globs"/> is null.</exception>
    /// <remarks>
    /// A malformed glob is a hard failure rather than something to skip, and the caller is expected
    /// to refuse to start. Skipping one would silently leave a <em>different</em> exposure in force
    /// than the one the human wrote — on this path, possibly a wider one.
    /// </remarks>
    public static bool TryCreate(
        IReadOnlyList<string> globs,
        [NotNullWhen(true)] out EntryExposure? exposure,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(globs);

        exposure = null;

        if (globs.Count > MaximumGlobs)
        {
            error = $"at most {MaximumGlobs} patterns are allowed, and {globs.Count} were given";
            return false;
        }

        var accepted = new string[globs.Count];
        List<Rule> rules = [];
        List<Selector> selectors = [];

        for (var i = 0; i < globs.Count; i++)
        {
            var glob = globs[i];

            if (string.IsNullOrWhiteSpace(glob))
            {
                error = "a pattern cannot be empty";
                return false;
            }

            if (glob.Length > MaximumGlobLength)
            {
                error = $"a pattern cannot be longer than {MaximumGlobLength} characters";
                return false;
            }

            foreach (var c in glob)
            {
                if (char.IsControl(c) || c == '\\')
                {
                    error = $"pattern {i + 1} contains a character that is not allowed in one";
                    return false;
                }
            }

            // Any case, so a selector written TAG:env:x is refused rather than read as a place.
            if (glob.StartsWith(TagSelectorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!Selector.TryParse(glob, out var selector))
                {
                    error = $"pattern {i + 1} is a tag selector, which is {TagSelectorPrefix}{ProjectTag.Prefix}<project> or {TagSelectorPrefix}{ProjectTag.Prefix}<project>:<environment>";
                    return false;
                }

                selectors.Add(selector);
            }
            else
            {
                rules.Add(Rule.Parse(glob));
            }

            accepted[i] = glob;
        }

        exposure = new EntryExposure(accepted, [.. rules], [.. selectors]);
        error = string.Empty;
        return true;
    }

    /// <summary>How much of an entry this exposure reaches.</summary>
    /// <param name="name">The real, unsanitized name.</param>
    /// <param name="tags">The entry's own tags, never its group's.</param>
    /// <returns><see cref="ExposureReach.Entry"/> when a place pattern matches, otherwise <see cref="ExposureReach.ProjectFields"/> when a selector matches one of its project tags.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// An exposure built from no globs reaches nothing. That is deliberate: "no patterns were given"
    /// must never collapse into "everything is allowed", so applying <see cref="Default"/> is
    /// something a caller does on purpose. A reserved group is never reached, whatever the globs
    /// say, <c>**</c> included: its entries are keypaste's own records (<see cref="ReservedGroups"/>).
    /// A malformed project tag adds the entry to no project, so no selector reaches it.
    /// </remarks>
    public ExposureReach Reach(EntryName name, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tags);

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            return ExposureReach.None;
        }

        var segments = Split(name.GroupPath);

        if (_rules.Any(rule => MatchSegments(rule.Group, 0, segments, 0) && MatchOne(rule.Title, name.Title)))
        {
            return ExposureReach.Entry;
        }

        return _selectors.Length > 0
            && tags.Select(ProjectTag.Read).Any(tag => tag.Kind == ProjectTagKind.Member
                && _selectors.Any(selector => selector.Matches(tag.Project!, tag.Environment!)))
            ? ExposureReach.ProjectFields
            : ExposureReach.None;
    }

    /// <summary>Whether this exposure lets one field of an entry be asked for.</summary>
    /// <param name="name">The real, unsanitized name.</param>
    /// <param name="tags">The entry's own tags.</param>
    /// <param name="field">The field asked for.</param>
    /// <returns><see langword="true"/> when the entry is reached and the reach covers the field.</returns>
    public bool Permits(EntryName name, IEnumerable<string> tags, string field) => Permits(Reach(name, tags), field);

    /// <summary>Whether a reach covers a field: the whole entry covers any, a project tag only a custom field named like a variable.</summary>
    /// <param name="reach">The reach.</param>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the field may be asked for.</returns>
    public static bool Permits(ExposureReach reach, string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return reach switch
        {
            ExposureReach.Entry => true,
            ExposureReach.ProjectFields => CredentialFields.IsCustom(field),
            _ => false,
        };
    }

    /// <summary>Whether a bridge, which cannot read tags, may forward a request for this field of this entry.</summary>
    /// <param name="name">The name as the agent wrote it.</param>
    /// <param name="field">The field asked for.</param>
    /// <returns><see langword="false"/> only when the owner would refuse it whatever the entry's tags.</returns>
    public bool MayPermit(EntryName name, string field)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        return Reach(name, []) == ExposureReach.Entry
            || (_selectors.Length > 0 && !ReservedGroups.IsReserved(name.GroupPath) && CredentialFields.IsCustom(field));
    }

    private static EntryExposure Create(string[] globs)
    {
        if (!TryCreate(globs, out var exposure, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return exposure;
    }

    private static string[] Split(string groupPath) =>
        groupPath.Length == 0 ? [] : groupPath.Split('/');

    /// <summary>
    /// Matches a group path's segments against a pattern's, where <c>**</c> stands for any number
    /// of segments including none.
    /// </summary>
    private static bool MatchSegments(string[] pattern, int p, string[] value, int v)
    {
        while (true)
        {
            if (p == pattern.Length)
            {
                return v == value.Length;
            }

            if (string.Equals(pattern[p], DoubleStar, StringComparison.Ordinal))
            {
                // "env/**" has to match the group "env" itself as well as everything under it, so
                // the tail is allowed to consume nothing.
                for (var k = v; k <= value.Length; k++)
                {
                    if (MatchSegments(pattern, p + 1, value, k))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (v == value.Length || !MatchOne(pattern[p], value[v]))
            {
                return false;
            }

            p++;
            v++;
        }
    }

    /// <summary>
    /// Matches one string against a pattern in which <c>*</c> stands for any run of characters.
    /// </summary>
    /// <remarks>
    /// Iterative with a single backtrack point rather than recursive, so a pattern of many stars
    /// against a long name cannot become a stack problem. Comparison is ordinal by construction:
    /// these are <see cref="char"/> comparisons, not culture-sensitive string ones.
    /// </remarks>
    private static bool MatchOne(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, resume = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == Wildcard)
            {
                star = p++;
                resume = v;
            }
            else if (p < pattern.Length && pattern[p] == value[v])
            {
                p++;
                v++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == Wildcard)
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>One parsed tag selector: a pattern for the project and, when given, one for the environment.</summary>
    private readonly record struct Selector(string Project, string? Environment)
    {
        /// <summary>Reads <c>tag:env:&lt;project&gt;[:&lt;environment&gt;]</c>; an omitted environment is any.</summary>
        internal static bool TryParse(string glob, out Selector selector)
        {
            selector = default;

            const string prefix = TagSelectorPrefix + ProjectTag.Prefix;

            if (!glob.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var parts = glob[prefix.Length..].Split(':');

            if (parts.Length > 2 || parts.Any(part => part.Length == 0))
            {
                return false;
            }

            selector = new Selector(parts[0], parts.Length == 2 ? parts[1] : null);
            return true;
        }

        internal bool Matches(string project, string environment) =>
            MatchOne(Project, project) && (Environment is null || MatchOne(Environment, environment));
    }

    /// <summary>One parsed glob: a pattern for the group path, and one for the title.</summary>
    private readonly record struct Rule(string[] Group, string Title)
    {
        /// <summary>
        /// Splits a glob into its two halves. A trailing <c>**</c> belongs to the group pattern and
        /// leaves the title unconstrained, so <c>env/**</c> means "anything under env" rather than
        /// "an entry named ** in group env". Otherwise the last segment is the title.
        /// </summary>
        internal static Rule Parse(string glob)
        {
            var segments = glob.Split('/');

            return string.Equals(segments[^1], DoubleStar, StringComparison.Ordinal)
                ? new Rule(segments, AnyTitle)
                : new Rule(segments[..^1], segments[^1]);
        }
    }
}
