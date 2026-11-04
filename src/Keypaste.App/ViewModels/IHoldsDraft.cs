namespace Keypaste.App.ViewModels;

/// <summary>A screen that can hold what the person typed and has not saved.</summary>
internal interface IHoldsDraft
{
    /// <summary>What leaving would lose, as "Discard …?" names it, or null when it would lose nothing typed.</summary>
    string? UnsavedDraft { get; }
}
