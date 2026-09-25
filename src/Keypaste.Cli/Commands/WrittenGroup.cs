namespace Keypaste.Cli.Commands;

/// <summary>The group a new entry lands in, spelled the way the vault will store it.</summary>
internal static class WrittenGroup
{
    /// <summary>Drops empty segments, as the vault does when it creates the group.</summary>
    /// <remarks>The reserved-group check and the existing-entry lookup must see the path the write will reach, or <c>/.keypaste/x</c> passes one and lands in the other.</remarks>
    internal static string Normalize(string groupPath) =>
        string.Join('/', groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
}
