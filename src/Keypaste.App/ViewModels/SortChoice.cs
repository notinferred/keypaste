namespace Keypaste.App.ViewModels;

/// <summary>One of the list header's sorts.</summary>
/// <param name="Column">The column it sorts by.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Spoken">Its column and direction, for a screen reader.</param>
/// <param name="Arrow">The icon of the direction in force, or null when it is not the sort in force.</param>
internal sealed record SortChoice(string Column, string Name, string Spoken, string? Arrow);
