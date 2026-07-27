using System.Globalization;

namespace Keypaste.Core.Policy;

/// <summary>Says what a policy rule means, in words a person can check against what they meant.</summary>
/// <remarks>
/// <para>
/// <b>It renders what each pattern parsed to, never the line the user wrote.</b> That is the whole
/// reason this type exists, and the reason is a trap in the glob syntax that the specification for
/// this feature walked straight into: unless a pattern's last segment is exactly <c>**</c>, the last
/// segment is the <b>title</b>. So <c>env/dev*</c> — the obvious way to write "the dev environment"
/// — means <em>group exactly <c>env</c>, title starting <c>dev</c></em>. It matches nothing under
/// <c>env/dev/</c>, and it does match an entry sitting directly in <c>env</c> called
/// <c>devops_ROOT_TOKEN</c>, which the person writing the rule never pictured. Echoing their own
/// text back would confirm a belief instead of testing it; printing the two halves separately is
/// what lets them notice.
/// </para>
/// <para>
/// It lives in the core because <c>keypaste policy ls</c> and the approver's startup banner must say
/// the same thing about the same file, and CORE.md law 4.3 does not allow that sentence to be
/// written twice.
/// </para>
/// </remarks>
public static class PolicyText
{
    /// <summary>What every rendering ends with, so nothing about the rules has to be inferred.</summary>
    public static IReadOnlyList<string> Footer { get; } =
    [
        "Rules are tried in order; the first one that matches decides.",
        "A request that matches no rule is shown to you as usual.",
        "A bridge started without --client-label matches no rule, including one written \"*\".",
        "These rules cannot widen what a bridge was started with --expose.",
    ];

    /// <summary>Describes one rule as a block of lines.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The lines, without trailing newlines and without indentation of their own.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public static IReadOnlyList<string> Describe(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{rule.Ordinal}. {Who(rule)}"),
            $"   may read the {Fields(rule)} of entries whose",
        };

        for (var i = 0; i < rule.Scope.Globs.Count; i++)
        {
            // Patterns are alternatives, and four unbroken lines read as one four-part condition
            // rather than as two two-part ones. On a rule that grants a credential silently, "which
            // of these did I actually write" is not a question to leave to the layout.
            if (i > 0)
            {
                lines.Add("   ...or whose");
            }

            var (group, title) = Halves(rule.Scope.Globs[i]);
            lines.Add($"     group path matches   {group}");
            lines.Add($"     title matches        {title}");
        }

        lines.Add($"   for up to {Duration(rule.MaximumTtlSeconds)}, without asking you.");
        lines.Add($"   {Limit(rule)}");

        return lines;
    }

    /// <summary>Names the client a rule applies to.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>A phrase that begins a sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    /// <remarks>
    /// <c>"*"</c> renders as "Any <b>labelled</b> client", never "any client". A bridge started
    /// without <c>--client-label</c> matches no rule at all, and the difference between those two
    /// sentences is the difference between what the star does and what a reader would assume it did.
    /// </remarks>
    public static string Who(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return string.Equals(rule.Client, PolicyRule.AnyClient, StringComparison.Ordinal)
            ? "Any labelled client"
            : $"The client labelled \"{rule.Client}\"";
    }

    /// <summary>Lists a rule's fields as English.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>Something like <c>username or password</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public static string Fields(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Fields.Count switch
        {
            1 => rule.Fields[0],
            2 => $"{rule.Fields[0]} or {rule.Fields[1]}",
            _ => $"{string.Join(", ", rule.Fields.Take(rule.Fields.Count - 1))} or {rule.Fields[^1]}",
        };
    }

    /// <summary>States a rule's hourly allowance, including when it has none.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>A whole sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    /// <remarks>
    /// An unlimited rule says so out loud rather than saying nothing. "No limit on how often" is
    /// what the person actually signed, and a blank line where a number would go reads as a number
    /// nobody happened to mention.
    /// </remarks>
    public static string Limit(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.MaximumPerHour is { } cap
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"At most {cap} times an hour; after that, requests are refused until the hour rolls.")
            : "No limit on how often.";
    }

    /// <summary>Turns a number of seconds into a phrase.</summary>
    /// <param name="seconds">The number of seconds.</param>
    /// <returns>Something like <c>5 minutes</c> or <c>1 minute 30 seconds</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> is not positive.</exception>
    public static string Duration(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        if (seconds == 3600)
        {
            return "1 hour";
        }

        var minutes = seconds / 60;
        var rest = seconds % 60;

        if (minutes == 0)
        {
            return Plural(rest, "second");
        }

        return rest == 0
            ? Plural(minutes, "minute")
            : $"{Plural(minutes, "minute")} {Plural(rest, "second")}";
    }

    /// <summary>
    /// Splits a glob the same way <see cref="EntryExposure"/> does, for display.
    /// </summary>
    /// <param name="glob">The pattern.</param>
    /// <returns>The group pattern and the title pattern, as they will be matched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glob"/> is null.</exception>
    /// <remarks>
    /// A second implementation of a rule this repository has exactly one of, which is normally
    /// forbidden — so it is pinned by a test that runs every pattern through both this and a real
    /// <see cref="EntryExposure"/> and requires the verdicts to agree. Making the matcher's own split
    /// public instead would put a second contract on a type whose entire doc comment is an argument
    /// about not widening by accident.
    /// </remarks>
    public static (string Group, string Title) Halves(string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);

        var cut = glob.LastIndexOf('/');

        if (glob.EndsWith("/**", StringComparison.Ordinal) || string.Equals(glob, "**", StringComparison.Ordinal))
        {
            return (glob, "anything");
        }

        return cut < 0
            ? ("(the top level)", glob)
            : (glob[..cut], glob[(cut + 1)..]);
    }

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");
}
