namespace Keypaste.Core;

/// <summary>How two profiles differ on one key.</summary>
/// <remarks>Values of two profiles are expected to differ, so a difference in value is not one; a reused value is.</remarks>
public enum EnvDiffKind
{
    /// <summary>One profile has the key and <see cref="EnvDiffLine.Profile"/> does not.</summary>
    Missing = 0,

    /// <summary><see cref="EnvDiffLine.Profile"/> has the key and would refuse it; <see cref="EnvDiffLine.Detail"/> says why.</summary>
    Unusable = 1,

    /// <summary><see cref="EnvDiffLine.Profile"/> and the profile in <see cref="EnvDiffLine.Detail"/> hold the same value.</summary>
    SameValue = 2,
}

/// <summary>One difference between two profiles. Holds no value.</summary>
/// <param name="Key">The variable name.</param>
/// <param name="Kind">What differs.</param>
/// <param name="Profile">The profile the line is about.</param>
/// <param name="Detail">The reason for <see cref="EnvDiffKind.Unusable"/>, the other profile for <see cref="EnvDiffKind.SameValue"/>, else null.</param>
public sealed record EnvDiffLine(string Key, EnvDiffKind Kind, string Profile, string? Detail);

/// <summary>Compares two profiles of an <see cref="EnvMatrix"/>.</summary>
public static class EnvDiff
{
    /// <summary>Every difference between two profiles, ordinal by key.</summary>
    /// <param name="matrix">The project's matrix.</param>
    /// <param name="a">The first profile.</param>
    /// <param name="b">The second profile.</param>
    /// <returns>The lines; none when the two hold the same usable keys and share no value.</returns>
    /// <exception cref="ArgumentException">A profile is not one of the matrix's.</exception>
    public static IReadOnlyList<EnvDiffLine> Compare(EnvMatrix matrix, string a, string b)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var left = IndexOf(matrix, a, nameof(a));
        var right = IndexOf(matrix, b, nameof(b));
        List<EnvDiffLine> lines = [];

        foreach (var row in matrix.Rows)
        {
            var first = row.Cells[left];
            var second = row.Cells[right];

            if (first.State == EnvCellState.Missing && second.State != EnvCellState.Missing)
            {
                lines.Add(new EnvDiffLine(row.Key, EnvDiffKind.Missing, a, null));
            }

            if (second.State == EnvCellState.Missing && first.State != EnvCellState.Missing)
            {
                lines.Add(new EnvDiffLine(row.Key, EnvDiffKind.Missing, b, null));
            }

            foreach (var cell in new[] { first, second }.Where(cell => cell.State == EnvCellState.Unusable))
            {
                lines.Add(new EnvDiffLine(row.Key, EnvDiffKind.Unusable, cell.Profile, cell.Problem));
            }

            if (first.State == EnvCellState.Set && first.SameValueAs.Contains(b, StringComparer.Ordinal))
            {
                lines.Add(new EnvDiffLine(row.Key, EnvDiffKind.SameValue, a, b));
            }
        }

        return lines;
    }

    private static int IndexOf(EnvMatrix matrix, string profile, string parameter)
    {
        ArgumentNullException.ThrowIfNull(profile, parameter);

        for (var i = 0; i < matrix.Profiles.Count; i++)
        {
            if (string.Equals(matrix.Profiles[i].Name, profile, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException($"'{matrix.Project}' has no '{profile}' profile", parameter);
    }
}
