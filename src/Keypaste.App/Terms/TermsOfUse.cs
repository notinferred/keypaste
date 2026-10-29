using System.Globalization;
using System.Text;

namespace Keypaste.App.Terms;

/// <summary>One heading of the terms of use and its paragraphs.</summary>
internal sealed record TermsSection(string Heading, IReadOnlyList<string> Paragraphs);

/// <summary>The terms of use the first run shows, read from the one copy the app carries, <c>terms-of-use.txt</c> (D-0597).</summary>
/// <param name="Title">The first line.</param>
/// <param name="Status">The line under it, which ends in their date, such as <c>Draft version 2026-10-28</c>.</param>
/// <param name="Version">That date, which <c>app.toml</c> records beside the person's choice.</param>
/// <param name="Sections">The headings and their paragraphs, in order.</param>
internal sealed record TermsOfUse(string Title, string Status, string Version, IReadOnlyList<TermsSection> Sections)
{
    private const string _resource = "Keypaste.App.Terms.terms-of-use.txt";
    private const string _note = "//";
    private const string _heading = "## ";

    private static readonly Lazy<TermsOfUse> _carried = new(ReadCarried);

    /// <summary>The terms this build carries.</summary>
    internal static TermsOfUse Carried => _carried.Value;

    /// <summary>Reads terms in the carried file's form: notes left out, a title, a dated line, then headed paragraphs.</summary>
    /// <exception cref="FormatException">The text is not in that form.</exception>
    internal static TermsOfUse Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith(_note, StringComparison.Ordinal))
            .ToList();

        if (lines.Count < 3 || lines.Take(2).Any(IsHeading) || !IsHeading(lines[2]))
        {
            throw new FormatException("the terms of use start with a title, a dated line and a heading");
        }

        var date = lines[1].Split(' ')[^1];

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new FormatException("the terms of use's dated line ends in no date");
        }

        List<(string Heading, List<string> Paragraphs)> sections = [];

        foreach (var line in lines.Skip(2))
        {
            if (IsHeading(line))
            {
                sections.Add((line[_heading.Length..], []));
            }
            else
            {
                sections[^1].Paragraphs.Add(line);
            }
        }

        return new TermsOfUse(lines[0], lines[1], date, [.. sections.Select(section => new TermsSection(section.Heading, section.Paragraphs))]);
    }

    private static bool IsHeading(string line) => line.StartsWith(_heading, StringComparison.Ordinal);

    private static TermsOfUse ReadCarried()
    {
        using var stream = typeof(TermsOfUse).Assembly.GetManifestResourceStream(_resource)
            ?? throw new InvalidOperationException("the app carries no terms of use");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return Parse(reader.ReadToEnd());
    }
}
