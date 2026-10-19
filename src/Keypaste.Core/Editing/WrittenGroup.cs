namespace Keypaste.Core.Editing;

/// <summary>The group a new entry lands in, spelled the way the vault will store it.</summary>
public static class WrittenGroup
{
    /// <summary>Drops empty segments, as the vault does when it creates the group.</summary>
    /// <param name="groupPath">The group as typed.</param>
    /// <returns>The group as the vault stores it.</returns>
    /// <remarks>The reserved-group check and the existing-entry lookup must see the path the write will reach, or <c>/.keypaste/x</c> passes one and lands in the other.</remarks>
    public static string Normalize(string groupPath)
    {
        ArgumentNullException.ThrowIfNull(groupPath);

        return string.Join('/', groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
