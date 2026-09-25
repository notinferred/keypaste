using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One line of the Profiles card: a profile and whether this key is usable there. Never a value.</summary>
/// <param name="Profile">The profile's name.</param>
/// <param name="State">"set", "approval required", "missing" or "unusable".</param>
/// <param name="Problem">Why a key that is present cannot be used, or null.</param>
internal sealed record ProfileState(string Profile, string State, string? Problem)
{
    internal bool IsOk => State == "set";

    internal bool IsAmber => State == "approval required";

    internal bool IsDanger => !IsOk && !IsAmber;

    /// <summary>A protected profile's key is always asked about live, which is what its line says once it is set.</summary>
    internal static ProfileState Of(EnvCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var state = cell.State switch
        {
            EnvCellState.Set when EnvProfileNames.IsProtected(cell.Profile) => "approval required",
            EnvCellState.Set => "set",
            EnvCellState.Missing => "missing",
            _ => "unusable",
        };

        return new ProfileState(cell.Profile, state, cell.Problem);
    }
}
