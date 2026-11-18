using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One stage as a segment of the project's stage control.</summary>
/// <param name="Name">The stage's name.</param>
/// <param name="IsProtected">Whether every release of it is asked live.</param>
/// <param name="IsSelected">Whether the table, edits and the run command use it.</param>
/// <param name="Select">Makes it the selected stage.</param>
internal sealed record EnvProfileColumn(string Name, bool IsProtected, bool IsSelected, RelayCommand Select)
{
    /// <summary>The name as the segment draws it.</summary>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>Whether a variable another stage holds has no value here, which the segment marks with a dot.</summary>
    internal bool HasMissing { get; init; }

    /// <summary>What a screen reader hears for the segment, since the dot and protection are drawn, not written.</summary>
    internal string Spoken => string.Join(", ", new[]
    {
        DisplayName,
        IsProtected ? "protected" : string.Empty,
        HasMissing ? "missing values" : string.Empty,
    }.Where(part => part.Length > 0));

    /// <summary>What hovering the segment explains.</summary>
    internal string Tip => (IsProtected, HasMissing) switch
    {
        (true, true) => $"{DisplayName} asks before every release, and lacks a value another stage has.",
        (true, false) => $"{DisplayName} asks before every release.",
        (false, true) => $"{DisplayName} lacks a value another stage has.",
        _ => $"Show {DisplayName}'s variables",
    };
}

/// <summary>One variable of the selected stage, with how every stage holds it. Holds no value.</summary>
/// <param name="Key">The variable's name.</param>
/// <param name="Cells">How each stage holds it, in the stage control's order.</param>
/// <param name="Stage">The selected stage, whose cell the row draws.</param>
internal sealed record EnvKeyRow(string Key, IReadOnlyList<EnvProfileCell> Cells, string Stage)
{
    internal string DisplayKey => EntryNameSanitizer.Sanitize(Key).Text;

    /// <summary>The selected stage's cell.</summary>
    internal EnvProfileCell Selected => Cells.First(cell => string.Equals(cell.Profile, Stage, StringComparison.Ordinal));

    /// <summary>The selected stage's variable, which the row's reveal, copy, replace and remove act on.</summary>
    internal EnvVariableRow? Variable => Selected.Variable;

    internal bool HasVariable => Variable is not null;

    /// <summary>The stages holding no value for it.</summary>
    internal IReadOnlyList<string> MissingIn => [.. Cells.Where(cell => cell.IsMissing).Select(cell => EntryNameSanitizer.Sanitize(cell.Profile).Text)];

    /// <summary>Whether some stage lacks it, which draws its name in the warning colour.</summary>
    internal bool IsFlagged => MissingIn.Count > 0;

    /// <summary>"Missing in prod", under the name.</summary>
    internal string MissingNote => IsFlagged ? "Missing in " + Words.And(MissingIn) : string.Empty;

    /// <summary>Whether the selected stage has no value, so the row offers to set one.</summary>
    internal bool IsMissingHere => Selected.IsMissing;

    /// <summary>Whether the value repeats another stage's, which the row says in place of the mask.</summary>
    internal bool IsSameAs => Selected.State == EnvCellState.Set && Selected.SameValueAs.Count > 0;

    /// <summary>"Same as staging".</summary>
    internal string SameAsNote => IsSameAs ? "Same as " + Words.And([.. Selected.SameValueAs.Select(stage => EntryNameSanitizer.Sanitize(stage).Text)]) : string.Empty;

    /// <summary>How many dots stand for the value; none where the row says it repeats another stage's, so a shown value covers the words.</summary>
    internal int MaskedLength => IsSameAs ? 0 : 10;

    /// <summary>Whether the value is drawn, masked or shown.</summary>
    internal bool HasValue => !IsMissingHere;

    /// <summary>What <c>keypaste run</c> refuses in the value, short, in the warning colour beside it.</summary>
    internal string Problem => Selected.State == EnvCellState.Unusable ? Selected.Note : string.Empty;

    internal bool HasProblem => Problem.Length > 0;

    /// <summary>The item the value comes from, by title, or how many items hold it.</summary>
    internal string From => Selected.SourceTitles switch
    {
        [] => string.Empty,
        [var only] => only,
        var several => Invariant($"{several.Count} items"),
    };

    /// <summary>The items by path, and the other stages one item serves, for the tooltip and the screen reader.</summary>
    internal string FromTip => Selected.FullSourceNote;

    /// <summary>What a screen reader hears for the value.</summary>
    internal string Spoken => Selected.Spoken;
}

/// <summary>
/// One variable in one stage. Holds no value: the selected stage's cell carries the
/// <see cref="EnvVariableRow"/> that reads its value when revealed.
/// </summary>
/// <param name="Profile">The stage.</param>
/// <param name="State">Set, missing or unusable, from <see cref="EnvMatrix"/>.</param>
/// <param name="Problem">Why an unusable value cannot be used, or null.</param>
/// <param name="SameValueAs">The other stages holding the same value.</param>
/// <param name="Variable">The selected stage's row for this variable, or null in any other stage.</param>
/// <param name="Act">Selects the stage, and on a missing variable opens the add form for it.</param>
internal sealed record EnvProfileCell(
    string Profile,
    EnvCellState State,
    string? Problem,
    IReadOnlyList<string> SameValueAs,
    EnvVariableRow? Variable,
    RelayCommand Act)
{
    /// <summary>The items holding the variable in this stage, as a prompt shows them.</summary>
    internal IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>The same items by title, as the table's From item column names them.</summary>
    internal IReadOnlyList<string> SourceTitles { get; init; } = [];

    /// <summary>The other stages the one source item serves, which a change to its value reaches too.</summary>
    internal IReadOnlyList<string> Also { get; init; } = [];

    /// <summary>Where the value lives: its item, and the other stages that item serves.</summary>
    internal string FullSourceNote => Sources.Count == 0
        ? string.Empty
        : string.Join(", ", Sources) + (Also.Count > 0 ? " · also " + string.Join(", ", Also) : string.Empty);

    /// <summary>What a screen reader hears for the cell: the mask or "missing", and the item always.</summary>
    internal string Spoken => Sources.Count > 0 ? $"{Label}, from {string.Join(", ", Sources)}" : Label;

    internal bool IsMissing => State == EnvCellState.Missing;

    /// <summary>The cell in words: the mask, or that the variable is missing.</summary>
    internal string Label => IsMissing ? "missing" : "••••••";

    /// <summary>What run refuses in the value, or which stages it repeats.</summary>
    internal string Note => State switch
    {
        EnvCellState.Unusable => ShortProblem,
        EnvCellState.Set when SameValueAs.Count > 0 => "same as " + string.Join(", ", SameValueAs),
        _ => string.Empty,
    };

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
                _ when first.StartsWith("holds the KeePass placeholder", StringComparison.Ordinal) => "placeholder",
                _ when first.StartsWith("is on more than one item", StringComparison.Ordinal) => "on two items",
                _ => DisplayTextSanitizer.Sanitize(first).Text,
            };
        }
    }

    /// <summary>What hovering the value explains; names stages and rules, never a value.</summary>
    internal string? Tip => State switch
    {
        EnvCellState.Missing => $"Not set in {Profile}. Click to add it.",
        EnvCellState.Unusable => DisplayTextSanitizer.Sanitize($"keypaste run refuses this value: {Problem}.{From}").Text,
        _ when SameValueAs.Count > 0 => $"The {Profile} value is the same as in {string.Join(" and ", SameValueAs)}.{From}",
        _ => Sources.Count > 0 ? From.TrimStart() : null,
    };

    /// <summary>Where a set value lives, after what the tip says first.</summary>
    private string From => Sources.Count > 0 ? $" From {string.Join(", ", Sources)}." : string.Empty;
}

/// <summary>Joins names as a sentence lists them.</summary>
internal static class Words
{
    /// <summary>"dev", "dev and prod", "dev, staging and prod".</summary>
    internal static string And(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
