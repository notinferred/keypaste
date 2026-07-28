using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core;

/// <summary>
/// The set of entry names a bridge is permitted to talk about at all.
/// </summary>
/// <remarks>
/// <para>
/// docs/PRODUCT.md law 3.2 says the default is deny. That is usually read as being about credentials, but
/// entry names are themselves an asset — a complete inventory of a personal vault is what turns a
/// vague request into a targeted one, even with no secret attached (law 3.5, THREATS.md T-4). So
/// the listing surface gets the same treatment: <see cref="Default"/> covers the environment
/// variables the product is actually about, and anything wider has to be written down by a human in
/// the MCP client's configuration.
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

    /// <summary>
    /// The one glob in force when nobody widened the exposure. Public because a front end has to be
    /// able to say "no <c>--expose</c> was given, so use this" without spelling out the string.
    /// </summary>
    public const string DefaultGlob = EnvConvention.RootGroup + "/" + DoubleStar;

    /// <summary>
    /// The wildcard character itself. <c>internal</c> rather than <c>private</c> because the
    /// naming rule in <c>.editorconfig</c> applies <c>_camelCase</c> to every private field,
    /// constants included, and this repository has no <c>private const</c> anywhere.
    /// </summary>
    internal const char Wildcard = '*';

    private static readonly string[] _defaultGlobs = [DefaultGlob];

    private readonly Rule[] _rules;

    private EntryExposure(string[] globs, Rule[] rules)
    {
        Globs = globs;
        _rules = rules;
    }

    /// <summary>
    /// What a server exposes when nobody said otherwise: the <c>env</c> subtree and nothing else.
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
        var rules = new Rule[globs.Count];

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

            accepted[i] = glob;
            rules[i] = Rule.Parse(glob);
        }

        exposure = new EntryExposure(accepted, rules);
        error = string.Empty;
        return true;
    }

    /// <summary>Whether this exposure permits an entry name to be mentioned at all.</summary>
    /// <param name="name">The real, unsanitized name.</param>
    /// <returns><see langword="true"/> if any glob matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <remarks>
    /// An exposure built from no globs allows nothing. That is deliberate: "no patterns were given"
    /// must never collapse into "everything is allowed", so applying <see cref="Default"/> is
    /// something a caller does on purpose.
    /// </remarks>
    public bool Allows(EntryName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var segments = Split(name.GroupPath);

        foreach (var rule in _rules)
        {
            if (MatchSegments(rule.Group, 0, segments, 0) && MatchOne(rule.Title, name.Title))
            {
                return true;
            }
        }

        return false;
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
