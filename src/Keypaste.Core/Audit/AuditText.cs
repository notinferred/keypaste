using System.Globalization;

namespace Keypaste.Core.Audit;

/// <summary>
/// Says what the audit log holds, and what the chain says about it, in words a person can act on.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the core for the same reason <see cref="Policy.PolicyText"/> does: <c>keypaste log</c>
/// and the GUI's activity feed must say the same thing about the same file, and docs/PRODUCT.md law 4.3 does
/// not allow that sentence to be written twice.
/// </para>
/// <para>
/// <b>ASCII only, and no colour.</b> The output survives every terminal, code page and CI log, and
/// the text arriving here has already been sanitized by <see cref="AuditReader"/> — which matters,
/// because a column of this table is filled in by whoever wrote the entry name.
/// </para>
/// <para>
/// <b>A filtered view never looks like the whole log.</b> Every rendering states which filters were
/// applied and how many records they hid. A table that silently shows twelve of four hundred records
/// is a table that can be made to prove anything.
/// </para>
/// </remarks>
public static class AuditText
{
    /// <summary>
    /// The column headings, and what an absent value is shown as.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> because the naming rule in <c>.editorconfig</c>
    /// applies <c>_camelCase</c> to every private field, constants included, and this repository has
    /// no <c>private const</c> anywhere.
    /// </remarks>
    internal const string TimeHeader = "time (UTC)";

    /// <inheritdoc cref="TimeHeader"/>
    internal const string ClientHeader = "client";

    /// <inheritdoc cref="TimeHeader"/>
    internal const string EntryHeader = "entry";

    /// <inheritdoc cref="TimeHeader"/>
    internal const string DecisionHeader = "decision";

    /// <inheritdoc cref="TimeHeader"/>
    internal const string MethodHeader = "method";

    /// <inheritdoc cref="TimeHeader"/>
    internal const string Blank = "-";

    /// <summary>The mark put beside a release served under a reason nobody read.</summary>
    public const string UnreadMark = "(!)";

    /// <summary>The mark put in front of a row the hash chain does not vouch for.</summary>
    /// <remarks>
    /// A gutter rather than a suffix, because it is a statement about the row rather than about any
    /// column of it — and because a reader scanning for it should not have to reach the end of a
    /// line to find out whether the line can be believed.
    /// </remarks>
    public const string UnverifiedMark = "?";

    /// <summary>Renders records as a table.</summary>
    /// <param name="entries">The records to show, in file order.</param>
    /// <param name="unverified">
    /// The physical line numbers the chain does not vouch for — <see cref="AuditChainReport.Unverified"/>.
    /// </param>
    /// <returns>The lines, without trailing newlines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <remarks>
    /// <b>Marking the unverified rows is not decoration.</b> A line the chain cannot check — one
    /// predating it, one from a newer schema, one somebody inserted — parses as a record and renders
    /// as a record, so an unmarked table is a place where something that never happened can be made
    /// to look exactly like something that did. The chain's answer is per record, and so is this.
    /// </remarks>
    public static IReadOnlyList<string> Table(
        IReadOnlyList<AuditEntry> entries,
        IReadOnlySet<int>? unverified = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return [];
        }

        var doubted = unverified is { Count: > 0 };
        var rows = new List<string[]>(entries.Count + 1);
        rows.Add([" ", TimeHeader, ClientHeader, EntryHeader, DecisionHeader, MethodHeader]);

        foreach (var entry in entries)
        {
            rows.Add(
            [
                doubted && unverified!.Contains(entry.Line) ? UnverifiedMark : " ",
                When(entry),
                Or(entry.Client),
                Or(entry.Entry),
                Or(entry.Decision),
                entry.ReasonUnread ? $"{Or(entry.Method)} {UnreadMark}" : Or(entry.Method),
            ]);
        }

        // Widths come from the data. Nothing is truncated: an audit table whose entry column has
        // been cut short is a table that cannot answer the question it exists to answer, and the
        // writer already caps every field it records.
        var widths = new int[rows[0].Length];
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        var lines = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var built = new StringBuilder();

            for (var i = 0; i < row.Length; i++)
            {
                // The gutter is one character wide and takes a single space, so the table does not
                // shift when nothing is marked.
                var gap = i == 0 ? 1 : 2;
                built.Append(i == row.Length - 1 ? row[i] : row[i].PadRight(widths[i] + gap));
            }

            lines.Add(built.ToString().TrimEnd());
        }

        return lines;
    }

    /// <summary>States what a table is and is not showing.</summary>
    /// <param name="path">The log the records came from.</param>
    /// <param name="shown">How many records the table holds.</param>
    /// <param name="total">How many records the file holds.</param>
    /// <param name="filters">The filters that were applied, already in words.</param>
    /// <returns>The lines, without trailing newlines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="filters"/> is null.</exception>
    public static string Heading(string path, int shown, int total, IReadOnlyList<string> filters)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(filters);

        return filters.Count == 0
            ? $"{Count(total, "record")} in {path}"
            : $"{Count(shown, "record")} of {total} in {path}, {string.Join(", ", filters)}";
    }

    /// <summary>Explains anything in a table that is not self-evident.</summary>
    /// <param name="entries">The records the table holds.</param>
    /// <param name="unreadable">How many lines of the file were not records this version understands.</param>
    /// <param name="unverified">The physical line numbers the chain does not vouch for.</param>
    /// <returns>The lines, without trailing newlines. Empty when there is nothing to explain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public static IReadOnlyList<string> Notes(
        IReadOnlyList<AuditEntry> entries,
        int unreadable,
        IReadOnlySet<int>? unverified = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var lines = new List<string>();

        if (unverified is { Count: > 0 } && entries.Any(e => unverified.Contains(e.Line)))
        {
            lines.Add($"{UnverifiedMark}  the hash chain does not vouch for this row. Run 'keypaste log verify'.");
        }

        // THREATS.md T-12. A person approved one request and a later one was served from the same
        // grant under different words; the second reason was never in front of anybody. It is worth
        // a mark precisely because nothing else about such a line looks unusual.
        if (entries.Any(e => e.ReasonUnread))
        {
            lines.Add($"{UnreadMark} served from an earlier approval, under a reason that person never saw.");
        }

        if (unreadable > 0)
        {
            lines.Add($"{Count(unreadable, "line")} could not be read as a record. Run 'keypaste log verify'.");
        }

        return lines;
    }

    /// <summary>Says what the chain says about a whole file.</summary>
    /// <param name="report">What the verifier found.</param>
    /// <returns>The lines, without trailing newlines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public static IReadOnlyList<string> Verdict(AuditChainReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>();

        switch (report.Verdict)
        {
            case AuditChainVerdict.Unreadable:
                lines.Add($"{report.Path} could not be read: {report.Error}");
                return lines;

            case AuditChainVerdict.Empty:
                lines.Add($"{report.Path} holds no records. There is nothing to verify.");
                return lines;

            case AuditChainVerdict.Broken:
                lines.Add($"THE CHAIN IS BROKEN in {report.Path}.");
                lines.Add(string.Empty);
                lines.AddRange(Breaks(report));
                lines.Add(string.Empty);
                lines.Add("Every record before the first break is still intact and still verified.");
                lines.Add("What this means is that the file is not the file keypaste wrote.");

                // Said on a broken file too. The observations below are precisely the ones that
                // explain which rows of `keypaste log` cannot be believed, and withholding them
                // from the one person who is already looking for a problem would be backwards.
                AddForgiven(lines, report);
                AddAnchor(lines, report);
                return lines;

            default:
                lines.Add($"{Count(report.Records, "record")} verified in {report.Path}.");

                if (report.Records > 0)
                {
                    lines.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Latest: seq {report.LatestSequence}, hash {report.LatestHash}"));
                }

                AddForgiven(lines, report);
                AddAnchor(lines, report);
                lines.Add(string.Empty);
                lines.AddRange(Limits);
                return lines;
        }
    }

    private static void AddForgiven(List<string> lines, AuditChainReport report)
    {
        var forgiven = Forgiven(report).ToList();
        if (forgiven.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.AddRange(forgiven);
    }

    private static void AddAnchor(List<string> lines, AuditChainReport report)
    {
        if (report.Anchored is not { } found)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(found
            ? "The record you anchored to is here, and it verifies."
            : "THE RECORD YOU ANCHORED TO IS NOT IN THIS FILE. Records have been removed from it,");

        if (!found)
        {
            lines.Add("or it is not the same log. A hash that merely appears in the text does not");
            lines.Add("count: only a record whose own bytes still hash to it can answer for it.");
        }
    }

    /// <summary>What a passing check does not prove, said on every pass rather than never.</summary>
    /// <remarks>
    /// Not over-claiming on green is the mirror of not crying wolf on red, and it is what makes the
    /// answer worth anything to the person docs/PRODUCT.md section 2 says keypaste is for. THREATS.md T-5
    /// carries the same two sentences.
    /// </remarks>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "This detects careless edits: a record changed, removed, inserted, or written by something",
        $"else. A record it cannot check at all is marked {UnverifiedMark} rather than vouched for.",
        "It cannot detect a rewrite that recomputed the chain, because the chain holds no secret,",
        "and it cannot detect records deleted from the end, because nothing follows them. Record",
        "the latest hash somewhere else and pass it back with --expect to close that second one.",
    ];

    private static IEnumerable<string> Breaks(AuditChainReport report)
    {
        foreach (var finding in report.Findings)
        {
            if (finding.IsBreak)
            {
                yield return $"  line {finding.Line}{Named(finding)}: {Fault(finding.Fault)}";
            }
        }
    }

    private static IEnumerable<string> Forgiven(AuditChainReport report)
    {
        if (report.Legacy > 0)
        {
            yield return $"{Count(report.Legacy, "record")} predate the hash chain and cannot be checked.";
            yield return "That is not a sign of tampering; it is what a log written before 2.4 looks";
            yield return $"like. `keypaste log` marks them {UnverifiedMark}, because an unverifiable record";
            yield return "reads exactly like a verified one.";
        }

        if (report.Newer > 0)
        {
            yield return $"{Count(report.Newer, "record")} were written by a newer keypaste, so nothing here";
            yield return $"can vouch for them. `keypaste log` marks them {UnverifiedMark} too.";
        }

        if (report.Unfinished)
        {
            yield return "The file's last line was never finished. That is what an interrupted write";
            yield return "looks like - a crash, or a server appending while this ran. Not tampering.";
        }

        if (report.Rewritten)
        {
            yield return "The file's bytes are not the bytes keypaste wrote: its line endings or its";
            yield return "opening bytes have been changed. Something copied or re-saved this file.";
        }

        foreach (var finding in report.Findings)
        {
            switch (finding.Fault)
            {
                case AuditChainFault.SequenceGap:
                    yield return $"  line {finding.Line}: its position number does not follow the record before it.";
                    break;

                case AuditChainFault.Torn:
                    yield return $"  line {finding.Line}: a record that stops partway. An interrupted write, not an edit.";
                    break;

                default:
                    break;
            }
        }
    }

    private static string Named(AuditChainFinding finding) =>
        finding.Timestamp.Length == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" (seq {finding.Sequence}, {finding.Timestamp})");

    private static string Fault(AuditChainFault fault) => fault switch
    {
        AuditChainFault.Altered => "its own bytes have changed since it was written.",
        AuditChainFault.Unlinked => "it does not follow the record before it. One was removed or inserted.",
        AuditChainFault.Restarted => "the chain starts again here. The records before it were cut off.",
        AuditChainFault.Backdated => "a record from before the chain existed, sitting after records that"
            + " carry it. keypaste never writes one there.",
        _ => "it is not a record keypaste wrote. Something else wrote into the log.",
    };

    private static string When(AuditEntry entry) =>
        entry.At is { } at
            ? at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : Or(entry.Timestamp);

    private static string Or(string text) => text.Length == 0 ? Blank : text;

    private static string Count(int n, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{n} {noun}{(n == 1 ? string.Empty : "s")}");
}
