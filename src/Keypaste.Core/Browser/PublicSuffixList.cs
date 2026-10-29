namespace Keypaste.Core.Browser;

/// <summary>A domain name's registrable domain, by Mozilla's Public Suffix List (D-0615).</summary>
/// <remarks>
/// Both sections are read: the ICANN suffixes such as <c>co.uk</c> and the private ones such as <c>github.io</c>, which keep
/// one platform's tenants apart. The list is embedded in this assembly, its internationalised rules written as A-labels when
/// it was vendored, and read once, with no network; third_party/publicsuffix/UPSTREAM.md records its version and commit. The
/// algorithm is the list's own: an exception rule prevails, else the matching rule with the most labels, else the last label
/// alone is the suffix.
/// </remarks>
internal static class PublicSuffixList
{
    private const string _resource = "Keypaste.Core.public_suffix_list.dat";

    private static readonly Lazy<Rules> _rules = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A domain name's public suffix with the label before it.</summary>
    /// <param name="domain">A domain name in A-labels, in any case; never an IP address, which has no registrable domain.</param>
    /// <returns>
    /// The registrable domain in lowercase, or null for a public suffix, a name that is not ASCII and a name with an empty label,
    /// such as one that starts or ends with a dot.
    /// </returns>
    /// <exception cref="InvalidOperationException">The embedded list is absent or holds a rule that is not lowercase A-labels.</exception>
    internal static string? RegistrableDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain) || !Ascii.IsValid(domain))
        {
            return null;
        }

        var labels = domain.ToLowerInvariant().Split('.');

        if (Array.Exists(labels, label => label.Length == 0))
        {
            return null;
        }

        var suffixLabels = SuffixLabels(labels, _rules.Value);

        return labels.Length > suffixLabels ? Suffix(labels, labels.Length - suffixLabels - 1) : null;
    }

    private static int SuffixLabels(string[] labels, Rules rules)
    {
        for (var first = 0; first < labels.Length; first++)
        {
            if (rules.Exceptions.Contains(Suffix(labels, first)))
            {
                return labels.Length - first - 1;
            }
        }

        for (var first = 0; first < labels.Length; first++)
        {
            if (rules.Names.Contains(Suffix(labels, first))
                || (first + 1 < labels.Length && rules.Wildcards.Contains(Suffix(labels, first + 1))))
            {
                return labels.Length - first;
            }
        }

        return 1;
    }

    private static string Suffix(string[] labels, int first) => string.Join('.', labels[first..]);

    private static Rules Load()
    {
        using var stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream(_resource)
            ?? throw new InvalidOperationException($"{_resource} is not embedded in Keypaste.Core");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var rules = new Rules();

        while (reader.ReadLine() is { } line)
        {
            var end = line.AsSpan().IndexOfAny(' ', '\t');
            var rule = end < 0 ? line : line[..end];

            if (rule.Length > 0 && !rule.StartsWith("//", StringComparison.Ordinal))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    /// <summary>The list's rules by kind, each held by the name it covers.</summary>
    private sealed class Rules
    {
        /// <summary>Rules such as <c>co.uk</c>.</summary>
        internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        /// <summary>Rules such as <c>*.ck</c>, held as <c>ck</c>: any one label before it.</summary>
        internal HashSet<string> Wildcards { get; } = new(StringComparer.Ordinal);

        /// <summary>Rules such as <c>!www.ck</c>, held as <c>www.ck</c>, whose suffix is all but their first label.</summary>
        internal HashSet<string> Exceptions { get; } = new(StringComparer.Ordinal);

        internal void Add(string rule)
        {
            var (set, name) = rule switch
            {
                ['!', .. var rest] => (Exceptions, rest),
                ['*', '.', .. var rest] => (Wildcards, rest),
                _ => (Names, rule),
            };

            if (!Ascii.IsValid(name)
                || !string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal)
                || name.AsSpan().IndexOfAny('*', '!') >= 0
                || Array.Exists(name.Split('.'), label => label.Length == 0))
            {
                throw new InvalidOperationException($"the public suffix rule '{rule}' is not a name in lowercase A-labels");
            }

            set.Add(name);
        }
    }
}
