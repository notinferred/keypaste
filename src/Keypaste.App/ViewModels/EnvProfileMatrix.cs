using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One profile as a matrix column and a segment of the preview's toggle.</summary>
/// <param name="Name">The profile's name.</param>
/// <param name="IsProtected">Whether every release of it is asked live (D-0348).</param>
/// <param name="IsSelected">Whether edits, the preview and the run command use it.</param>
/// <param name="Select">Makes it the selected profile.</param>
internal sealed record EnvProfileColumn(string Name, bool IsProtected, bool IsSelected, RelayCommand Select)
{
    /// <summary>The column header, in capitals as the design writes table headers.</summary>
    internal string Header => EntryNameSanitizer.Sanitize(Name).Text.ToUpperInvariant();

    /// <summary>The name as the toggle draws it.</summary>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;
}

/// <summary>One key of the matrix: its name and a cell per profile. Holds no value.</summary>
internal sealed record EnvKeyRow(string Key, IReadOnlyList<EnvProfileCell> Cells)
{
    internal string DisplayKey => EntryNameSanitizer.Sanitize(Key).Text;

    /// <summary>The selected profile's variable for this key, which the row's copy, replace and remove act on.</summary>
    internal EnvVariableRow? Variable => Cells.FirstOrDefault(cell => cell.Variable is not null)?.Variable;

    internal bool HasVariable => Variable is not null;
}

/// <summary>One line of the <c>.env.keypaste</c> preview; the file's header comments are drawn quieter.</summary>
internal sealed record EnvPreviewLine(string Text)
{
    internal bool IsComment => Text.StartsWith('#');
}

/// <summary>
/// One key in one profile, as the matrix draws it. Holds no value: a set cell in the selected
/// profile carries the <see cref="EnvVariableRow"/> that reads its value on a hold.
/// </summary>
/// <param name="Profile">The profile.</param>
/// <param name="State">Set, missing or unusable, from <see cref="EnvMatrix"/>.</param>
/// <param name="Problem">Why an unusable value cannot be used, or null.</param>
/// <param name="SameValueAs">The other profiles holding the same value.</param>
/// <param name="Variable">The selected profile's row for this key, or null in any other profile.</param>
/// <param name="Act">Selects the profile, and on a missing key opens the add form for it.</param>
internal sealed record EnvProfileCell(
    string Profile,
    EnvCellState State,
    string? Problem,
    IReadOnlyList<string> SameValueAs,
    EnvVariableRow? Variable,
    RelayCommand Act)
{
    internal bool HasVariable => Variable is not null;

    internal bool ShowsLabel => Variable is null;

    /// <summary>Draws the hold target's cell above its row neighbours, which a held value may cover.</summary>
    internal int Layer => HasVariable ? 1 : 0;

    internal bool IsMissing => State == EnvCellState.Missing;

    internal bool IsSet => State != EnvCellState.Missing;

    /// <summary>What the cell draws in every column: the mask, or that the key is missing.</summary>
    internal string Label => IsMissing ? "missing" : "••••••";

    /// <summary>The design's amber "differs", after the mask: what run refuses in the value, or which profile it repeats.</summary>
    internal string Note => State switch
    {
        EnvCellState.Unusable => ShortProblem,
        EnvCellState.Set when SameValueAs.Count > 0 => "same as " + string.Join(", ", SameValueAs),
        _ => string.Empty,
    };

    internal bool HasNote => Note.Length > 0;

    /// <summary>The first thing wrong with an unusable value, short enough for a cell; the tip has the rest.</summary>
    private string ShortProblem
    {
        get
        {
            var first = Problem?.Split("; ")[0];

            return first switch
            {
                null or "" => "unusable",
                _ when first.StartsWith("expired", StringComparison.Ordinal) => "expired",
                _ => DisplayTextSanitizer.Sanitize(first).Text,
            };
        }
    }

    /// <summary>What hovering the cell explains; names profiles and rules, never a value.</summary>
    internal string? Tip => State switch
    {
        EnvCellState.Missing => $"Not set in {Profile}. Click to add it.",
        EnvCellState.Unusable => DisplayTextSanitizer.Sanitize($"keypaste run refuses this value: {Problem}").Text,
        _ when SameValueAs.Count > 0 => $"The {Profile} value is the same as in {string.Join(" and ", SameValueAs)}.",
        _ => null,
    };
}
