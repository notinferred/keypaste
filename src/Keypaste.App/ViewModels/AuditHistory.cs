using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>What one read of the audit log found.</summary>
internal enum AuditReadKind
{
    /// <summary>Nothing is at the path.</summary>
    Missing,

    /// <summary>The file could not be read at all; <see cref="AuditHistory.Error"/> says why.</summary>
    Unreadable,

    /// <summary>The file was read but its chain could not be checked, so nothing from it is shown.</summary>
    Unchecked,

    /// <summary>Read and checked, and the chain holds.</summary>
    Intact,

    /// <summary>Read and checked, and the chain is broken; the rows are still shown.</summary>
    Broken,
}

/// <summary>
/// The audit log read, checked and rendered the way <c>keypaste log</c> does it, for every screen that shows it.
/// </summary>
/// <remarks>
/// The reader and the verifier are two passes over the same file on purpose: the verifier works on
/// bytes and the reader on JSON, so a parser difference can drop a row from a table but can never
/// change a verdict. Each screen says what a kind means in its own words.
/// </remarks>
internal sealed record AuditHistory(AuditReadKind Kind, IReadOnlyList<string> Lines, IReadOnlyList<string> Verdict, string Error)
{
    /// <summary>The records the table holds, in file order, as the core's reader parsed and sanitized them.</summary>
    internal IReadOnlyList<AuditEntry> Entries { get; init; } = [];

    /// <summary>How many records the whole file holds.</summary>
    internal int Total { get; init; }

    /// <summary>How many lines of the file were not records this version understands.</summary>
    internal int Unreadable { get; init; }

    /// <summary>The physical line numbers the hash chain does not vouch for.</summary>
    internal IReadOnlySet<int> Unverified { get; init; } = new HashSet<int>();

    /// <summary>Reads the log at <paramref name="path"/>, keeping the records <paramref name="keep"/> accepts.</summary>
    /// <param name="path">The log.</param>
    /// <param name="keep">Which records the table holds, or null for all of them.</param>
    /// <param name="filters">The filter in words, which the heading states with its counts (D-0032).</param>
    internal static AuditHistory Read(string path, Func<AuditEntry, bool>? keep = null, IReadOnlyList<string>? filters = null)
    {
        if (!Path.Exists(path))
        {
            return new AuditHistory(AuditReadKind.Missing, [], [], string.Empty);
        }

        if (!AuditReader.TryRead(path, out var entries, out var unreadable, out var error))
        {
            return new AuditHistory(AuditReadKind.Unreadable, [], [], error);
        }

        var report = AuditChainVerifier.Verify(path);

        // A table drawn from a file nothing checked must not be handed over as though something had.
        if (report.Verdict == AuditChainVerdict.Unreadable)
        {
            return new AuditHistory(AuditReadKind.Unchecked, [], [], report.Error);
        }

        var kept = keep is null ? entries : entries.Where(keep).ToList();
        var unverified = report.Unverified;

        IReadOnlyList<string> lines =
        [
            AuditText.Heading(path, kept.Count, entries.Count, filters ?? []),
            .. AuditText.Table(kept, unverified),
            .. AuditText.Notes(kept, unreadable, unverified),
        ];

        var kind = report.Verdict == AuditChainVerdict.Broken ? AuditReadKind.Broken : AuditReadKind.Intact;

        return new AuditHistory(kind, lines, AuditText.Verdict(report), string.Empty)
        {
            Entries = kept,
            Total = entries.Count,
            Unreadable = unreadable,
            Unverified = unverified,
        };
    }
}
