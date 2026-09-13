using Xunit;

namespace Keypaste.Core.Tests;

public sealed class RecordRowsStaySkimmableTests
{
    private const int _ledgerRowLimit = 650;
    private const int _completedCellLimit = 350;

    [Fact]
    public void LedgerRows_StayOneDecisionLong()
    {
        var rows = LedgerRows();
        Assert.NotEmpty(rows);
        var over = RowsOverLimit(rows, _ledgerRowLimit)
            .Select(row => $"{Id(row)} is {row.Length} characters")
            .ToList();

        Assert.True(
            over.Count == 0,
            $"DECISIONS.md rows exceed {_ledgerRowLimit} characters: " + string.Join("; ", over));
    }

    [Fact]
    public void CompletedStepRows_StayOneLineOfEvidence()
    {
        var rows = CompletedStepRows();
        Assert.NotEmpty(rows);
        var over = RowsOverLimit(rows, _completedCellLimit)
            .Select(row => $"{Id(row)} is {row.Length} characters")
            .ToList();

        Assert.True(
            over.Count == 0,
            $"Completed STEPS rows exceed {_completedCellLimit} characters: " + string.Join("; ", over));
    }

    [Theory]
    [InlineData(_ledgerRowLimit)]
    [InlineData(_completedCellLimit)]
    public void RowLengthLimit_RejectsOnlyRowsAboveTheBoundary(int limit)
    {
        var atLimit = new string('x', limit);
        var overLimit = new string('x', limit + 1);

        Assert.Equal([overLimit], RowsOverLimit(["| short |", atLimit, overLimit], limit));
    }

    private static IEnumerable<string> RowsOverLimit(IEnumerable<string> rows, int limit) =>
        rows.Where(row => row.Length > limit);

    private static List<string> LedgerRows()
    {
        var rows = new List<string>();
        var inside = false;
        foreach (var line in File.ReadLines(Path.Combine(RepoRoot(), "DECISIONS.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = line == "## Ledger";
                continue;
            }

            if (inside && line.StartsWith("| D-0", StringComparison.Ordinal))
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    private static List<string> CompletedStepRows()
    {
        var rows = new List<string>();
        var inside = false;
        foreach (var line in File.ReadLines(Path.Combine(RepoRoot(), "docs", "STEPS.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = line.Trim() == "## Completed steps";
                continue;
            }

            if (!inside || !line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("| Step |", StringComparison.Ordinal)
                || line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(line);
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
