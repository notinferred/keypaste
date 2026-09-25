using System.Globalization;

namespace Keypaste.Core.Ownership;

/// <summary>Which kind of keypaste process holds a vault unlocked.</summary>
public enum OwnerKind
{
    /// <summary>The desktop app.</summary>
    DesktopApp = 0,

    /// <summary>A terminal <c>keypaste agent</c>.</summary>
    TerminalAgent = 1,

    /// <summary>A keypaste command that saves the vault, held only while it runs.</summary>
    CommandLine = 2,
}

/// <summary>The process holding a vault, as another process is told about it.</summary>
/// <param name="Kind">What kind of process it is.</param>
/// <param name="ProcessId">Its process id.</param>
/// <param name="VaultPath">The vault it holds.</param>
public sealed record VaultOwner(OwnerKind Kind, int ProcessId, string VaultPath)
{
    /// <summary>Describes a process nothing more is known about than that it holds the claim.</summary>
    public const string Unknown = "another keypaste process";

    /// <summary>The owner as a refusal names it.</summary>
    /// <returns>For example "the keypaste desktop app (process 4120)".</returns>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name(Kind)} (process {ProcessId})");

    /// <summary>A fresh session identifier, minted for each unlock.</summary>
    /// <returns>32 lowercase hex characters from the system's random source.</returns>
    /// <remarks>
    /// It identifies an unlocked lifetime so a request can be refused once that lifetime ends. It is
    /// not a credential: reaching the endpoint at all is what the operating system authenticates.
    /// </remarks>
    public static string NewSession() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static string Name(OwnerKind kind) => kind switch
    {
        OwnerKind.DesktopApp => "the keypaste desktop app",
        OwnerKind.TerminalAgent => "keypaste agent",
        OwnerKind.CommandLine => "a keypaste command",
        _ => "a keypaste process",
    };
}
