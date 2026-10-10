namespace Keypaste.Core.Linking;

/// <summary>What is at the path a terminal finds <c>keypaste</c> through.</summary>
public enum CliLinkState
{
    /// <summary>Nothing is there.</summary>
    Absent = 0,

    /// <summary>The link keypaste writes, starting this copy.</summary>
    ThisCopy = 1,

    /// <summary>The link keypaste writes, starting another copy.</summary>
    OtherCopy = 2,

    /// <summary>Something keypaste did not write.</summary>
    Foreign = 3,
}

/// <summary>What a link or a replacement did.</summary>
public enum CliLinkOutcome
{
    /// <summary>The path now starts this copy.</summary>
    Linked = 0,

    /// <summary>What was there is kept at the backup path and the path now starts this copy.</summary>
    Replaced = 1,

    /// <summary>The person dismissed the system's dialog; nothing changed.</summary>
    Cancelled = 2,

    /// <summary>keypaste would not make the change; nothing changed.</summary>
    Refused = 3,

    /// <summary>The change was attempted and did not happen.</summary>
    Failed = 4,
}

/// <summary>Why a link or a replacement was refused.</summary>
public enum CliLinkRefusal
{
    /// <summary>Something keypaste did not write is at the path, and only a confirmed replacement moves it.</summary>
    NotKeypaste = 0,

    /// <summary>Something is already at the backup path, which a replacement never overwrites.</summary>
    BackupExists = 1,

    /// <summary>macOS runs this copy from a temporary location that is gone once it quits.</summary>
    Translocated = 2,

    /// <summary>The program's path cannot be written into the link safely.</summary>
    UnsafeProgram = 3,
}

/// <summary>What is at the link path, read when asked.</summary>
/// <param name="State">What is there.</param>
/// <param name="LinkPath">Where a terminal finds <c>keypaste</c>.</param>
/// <param name="LinkedTo">The other copy's program, or a foreign symbolic link's target; otherwise null.</param>
/// <param name="BackupPath">Where a replacement keeps what was there.</param>
/// <param name="BackupExists">Whether anything, a dangling symbolic link included, is at <paramref name="BackupPath"/>.</param>
/// <param name="DirectoryOnPath">Whether the link's directory is on this session's <c>PATH</c>.</param>
/// <param name="Translocated">Whether macOS runs this copy from a temporary location.</param>
public sealed record CliLinkStatus(
    CliLinkState State,
    string LinkPath,
    string? LinkedTo,
    string BackupPath,
    bool BackupExists,
    bool DirectoryOnPath,
    bool Translocated);

/// <summary>What a link or a replacement did.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Refusal">Why it was refused, when it was.</param>
/// <param name="Reason">What went wrong, when it failed.</param>
public sealed record CliLinkResult(CliLinkOutcome Outcome, CliLinkRefusal? Refusal = null, string? Reason = null);

/// <summary>The <c>keypaste</c> a terminal runs, linked to this copy where no installer puts it on <c>PATH</c>.</summary>
public interface ICliLink
{
    /// <summary>Gets where a terminal finds <c>keypaste</c>.</summary>
    string LinkPath { get; }

    /// <summary>Gets where a replacement keeps what was at <see cref="LinkPath"/>.</summary>
    string BackupPath { get; }

    /// <summary>Reads what is at the link path now.</summary>
    /// <returns>What is there.</returns>
    CliLinkStatus Read();

    /// <summary>Links this copy, unless something keypaste did not write is there.</summary>
    /// <returns>What happened. On macOS this waits for the person to answer the system's dialog.</returns>
    CliLinkResult Link();

    /// <summary>Moves what keypaste did not write to <see cref="BackupPath"/> and links this copy; links as <see cref="Link"/> does when nothing foreign is there.</summary>
    /// <returns>What happened. Nothing moves while anything is at <see cref="BackupPath"/>.</returns>
    /// <remarks>Called only after the person confirmed the replacement.</remarks>
    CliLinkResult Replace();

    /// <summary>Re-points a link keypaste wrote when it names a missing or older copy.</summary>
    /// <returns>Whether the link was rewritten.</returns>
    bool KeepCurrent();
}
