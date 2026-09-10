using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Holds DECISIONS.md's ledger rows and docs/STEPS.md's completed evidence cells to a length
/// (DECISIONS.md D-0083's one-line rule).
/// </summary>
/// <remarks>
/// <para>
/// Both files grow the same way: a row is written short, then each later finding appends the
/// account of how it was found, and after a few passes the row is a paragraph nobody re-reads —
/// D-0108 reached 3,696 characters and F.4a 2,124 before the 2026-09-10 trim. A length is a
/// coarse rule that catches it early, and the honest repair when this test goes red is almost
/// always to move the reasoning into the D-row, the code comment or git, not to raise the limit.
/// </para>
/// <para>
/// <b>The limits come from the trimmed result rather than from taste.</b> After that pass the
/// longest ledger row was 613 (D-0090, five ratified constraints in one sentence) and the longest
/// completed evidence cell 868 (F.4b, which carries three dispatch IDs and a hand observation).
/// The limits sit just above each, so a row that states its decision and its evidence fits and a
/// row that also tells the story does not.
/// </para>
/// <para>
/// <b>Two things are deliberately out of scope.</b> DECISIONS' archive below the frozen line is
/// not maintained and not rewritten (D-0083), and an open STEPS task is where the thinking is
/// supposed to happen — F.6 and F.7 argue about what a probe would settle, which is the row doing
/// its job. Only rows claiming something is finished are held here.
/// </para>
/// </remarks>
public sealed class RecordRowsStaySkimmableTests
{
    private const int LedgerRowLimit = 650;
    private const int CompletedCellLimit = 900;

    [Fact]
    public void LedgerRows_StayOneDecisionLong()
    {
        var over = new List<string>();
        foreach (var row in LedgerRows())
        {
            var length = row.Length;
            if (length > LedgerRowLimit)
            {
                over.Add($"{Id(row)} is {length} characters");
            }
        }

        Assert.True(
            over.Count == 0,
            "DECISIONS.md ledger rows are one line, for architecture, security or money (D-0083): "
            + string.Join("; ", over)
            + $". The limit is {LedgerRowLimit}. Say the decision and the constraint it leaves "
            + "behind; the account of how it was found belongs in git and in the comment beside "
            + "the code it changed.");
    }

    [Fact]
    public void CompletedStepRows_StayOneLineOfEvidence()
    {
        var over = new List<string>();
        foreach (var row in CompletedStepRows())
        {
            var length = row.Length;
            if (length > CompletedCellLimit)
            {
                over.Add($"{Id(row)} is {length} characters");
            }
        }

        Assert.True(
            over.Count == 0,
            "A completed docs/STEPS.md row carries one line of evidence — the run IDs or commits "
            + "that observed it, and its D-row: "
            + string.Join("; ", over)
            + $". The limit is {CompletedCellLimit}. Move the story of how the defect was found to "
            + "its D-row or drop it; open rows are not held to this.");
    }

    [Fact]
    public void TheLimitsAreLoadBearing()
    {
        // A limit no row approaches is a limit that has never decided anything, and one already
        // exceeded is a red test somebody deletes. Both halves are checked so a later trim that
        // makes these vacuous, or an edit that outgrows them, is visible here rather than in a
        // rule nobody reads.
        Assert.NotEmpty(LedgerRows());
        Assert.NotEmpty(CompletedStepRows());
        Assert.True(LedgerRows().Max(r => r.Length) > LedgerRowLimit / 2, "no ledger row is near its limit");
        Assert.True(
            CompletedStepRows().Max(r => r.Length) > CompletedCellLimit / 2,
            "no completed STEPS row is near its limit");
    }

    /// <summary>Ledger rows only: the archive below the frozen line is not maintained (D-0083).</summary>
    private static List<string> LedgerRows()
    {
        var rows = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(RepoRoot(), "DECISIONS.md")))
        {
            if (line.Contains("Frozen on", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("| D-0", StringComparison.Ordinal))
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    /// <summary>
    /// Table rows whose status cell claims the work is finished. An unchecked task is a bullet
    /// rather than a table row, so nothing open is reached by this.
    /// </summary>
    private static List<string> CompletedStepRows()
    {
        var rows = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(RepoRoot(), "docs", "STEPS.md")))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < 4)
            {
                continue;
            }

            var status = cells[2].Trim();
            if (status.StartsWith("Complete", StringComparison.Ordinal)
                || status.StartsWith("Published", StringComparison.Ordinal))
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    private static string Id(string row) => row.Split('|')[1].Trim();

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException("could not find keypaste.slnx above the test binary");
            }

            directory = parent;
        }

        return directory;
    }
}
