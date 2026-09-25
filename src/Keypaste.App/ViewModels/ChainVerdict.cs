using System.Globalization;
using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The hash chain's verdict as the Activity screen states it: a one-line status, the latest hash, the breaks, and
/// what the check does and does not prove.
/// </summary>
/// <remarks>
/// It reads the same <see cref="AuditChainReport"/> that <see cref="AuditText.Verdict"/> renders for
/// <c>keypaste log verify</c>. That rendering is hard-wrapped for a terminal and points at the command's flags, so the
/// window states the report in its own sentences; the one sentence per fault is still the core's.
/// </remarks>
internal sealed class ChainVerdict
{
    private ChainVerdict(AuditChainReport report) => Path = report.Path;

    /// <summary>How the status line is marked: ok when the chain holds, danger when it breaks, muted when it is empty.</summary>
    internal LogTone Tone { get; private init; }

    internal bool IsOk => Tone == LogTone.Ok;

    internal bool IsDanger => Tone == LogTone.Danger;

    internal string Headline { get; private init; } = string.Empty;

    /// <summary>The latest record's hash, or empty when there is none worth keeping.</summary>
    internal string Hash { get; private init; } = string.Empty;

    internal bool HasHash => Hash.Length > 0;

    /// <summary>One line per break, in file order.</summary>
    internal IReadOnlyList<string> Breaks { get; private init; } = [];

    internal bool HasBreaks => Breaks.Count > 0;

    /// <summary>What the verdict means, one unwrapped paragraph each.</summary>
    internal IReadOnlyList<string> Paragraphs { get; private init; } = [];

    /// <summary>The file that was checked.</summary>
    internal string Path { get; }

    /// <param name="report">What the verifier found.</param>
    /// <param name="zone">The zone the table's TIME column is in, so a break names the time the row shows.</param>
    internal static ChainVerdict From(AuditChainReport report, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(zone);

        return report.Verdict switch
        {
            AuditChainVerdict.Broken => Broken(report, zone),
            _ when report.Records == 0 => new ChainVerdict(report)
            {
                Tone = LogTone.Muted,
                Headline = "No records to verify",
                Paragraphs = [.. Forgiven(report)],
            },
            _ => new ChainVerdict(report)
            {
                Tone = LogTone.Ok,
                Headline = Invariant($"Chain verified · {Count(report.Records, "record")} · latest seq {report.LatestSequence}"),
                Hash = report.LatestHash,
                Paragraphs =
                [
                    .. Forgiven(report),
                    "This catches careless edits: a record changed, removed, inserted, or written by something else. A row it cannot check at all is marked rather than vouched for.",
                    "It cannot catch a rewrite that recomputed the chain, because the chain holds no secret, or records deleted from the end, because nothing follows them. Copy the latest hash and keep it somewhere else; checking a later log against it with keypaste log verify --expect on the command line closes that second gap.",
                ],
            },
        };
    }

    private static ChainVerdict Broken(AuditChainReport report, TimeZoneInfo zone)
    {
        var breaks = report.Findings.Where(finding => finding.IsBreak).ToList();
        var first = breaks.FirstOrDefault();

        return new ChainVerdict(report)
        {
            Tone = LogTone.Danger,
            Headline = first switch
            {
                { Timestamp.Length: > 0 } => Invariant($"Chain broken at seq {first.Sequence}"),
                not null => Invariant($"Chain broken at line {first.Line}"),
                null => "Chain broken",
            },
            Breaks = [.. breaks.Select(finding => Break(finding, zone))],
            Paragraphs =
            [
                "Every record before the first break is still intact and verified. This file is not the file keypaste wrote.",
                .. Forgiven(report),
            ],
        };
    }

    private static string Break(AuditChainFinding finding, TimeZoneInfo zone)
    {
        var fault = AuditText.Describe(finding.Fault);

        if (finding.Timestamp.Length == 0)
        {
            return Invariant($"Line {finding.Line}: {fault}");
        }

        var when = DateTimeOffset.TryParse(finding.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : finding.Timestamp;

        return Invariant($"Line {finding.Line} · seq {finding.Sequence} · {when} · {fault}");
    }

    private static IEnumerable<string> Forgiven(AuditChainReport report)
    {
        if (report.Legacy > 0)
        {
            yield return $"{Count(report.Legacy, "record")} predate the hash chain and cannot be checked, which is what a log written before 2.4 looks like rather than a sign of tampering. Their rows are marked.";
        }

        if (report.Newer > 0)
        {
            yield return $"{Count(report.Newer, "record")} were written by a newer keypaste, so nothing here can vouch for them. Their rows are marked.";
        }

        if (report.Unfinished)
        {
            yield return "The file's last line was never finished. That is what an interrupted write looks like, a crash or a server appending during the check, not tampering.";
        }

        if (report.Rewritten)
        {
            yield return "The file's bytes are not the bytes keypaste wrote: its line endings or its opening bytes have changed. Something copied or re-saved this file.";
        }

        foreach (var finding in report.Findings)
        {
            switch (finding.Fault)
            {
                case AuditChainFault.SequenceGap:
                    yield return $"Line {finding.Line}: its position number does not follow the record before it.";
                    break;

                case AuditChainFault.Torn:
                    yield return $"Line {finding.Line}: a record that stops partway. An interrupted write, not an edit.";
                    break;

                default:
                    break;
            }
        }
    }

    private static string Count(int n, string noun) => Invariant($"{n} {noun}{(n == 1 ? string.Empty : "s")}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
