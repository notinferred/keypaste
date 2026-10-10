using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Linking;

/// <summary>Links the CLI on Linux as <c>~/.local/bin/keypaste</c>, a script that starts this copy and names the version that wrote it.</summary>
/// <remarks>
/// A script rather than a symbolic link, because an AppImage starts its app unless told <c>cli</c>, and because the version
/// it names lets a newer copy re-point it without starting the copy it names.
/// </remarks>
public sealed class LinuxCliLink : ICliLink
{
    private const UnixFileMode _executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _directory;
    private readonly CliTarget _target;
    private readonly SemanticVersion _version;
    private readonly string? _pathVariable;

    /// <summary>Initializes the link for one copy.</summary>
    /// <param name="home">The person's home directory.</param>
    /// <param name="target">The program the link starts.</param>
    /// <param name="version">This copy's version, which the link records.</param>
    /// <param name="pathVariable">This session's <c>PATH</c>.</param>
    public LinuxCliLink(string home, CliTarget target, SemanticVersion version, string? pathVariable)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentNullException.ThrowIfNull(target);

        LinkPath = LinkPathFor(home);
        BackupPath = LinkPath + CliLinks.BackupSuffix;
        _directory = Path.GetDirectoryName(LinkPath)!;
        _target = target;
        _version = version;
        _pathVariable = pathVariable;
    }

    /// <inheritdoc/>
    public string LinkPath { get; }

    /// <inheritdoc/>
    public string BackupPath { get; }

    /// <inheritdoc/>
    public CliLinkStatus Read()
    {
        var found = Inspect();

        return new CliLinkStatus(found.State, LinkPath, found.LinkedTo, BackupPath, CliLinks.Occupied(BackupPath), DirectoryOnPath(), Translocated: false);
    }

    /// <inheritdoc/>
    public CliLinkResult Link()
    {
        var found = Inspect();

        return found.State switch
        {
            CliLinkState.Foreign => Refused(CliLinkRefusal.NotKeypaste),
            CliLinkState.ThisCopy when found.Version == _version => new CliLinkResult(CliLinkOutcome.Linked),
            _ => Write(CliLinkOutcome.Linked),
        };
    }

    /// <inheritdoc/>
    public CliLinkResult Replace()
    {
        if (Inspect().State != CliLinkState.Foreign)
        {
            return Link();
        }

        if (CliLinkScript.Document(_target, _version) is null)
        {
            return Refused(CliLinkRefusal.UnsafeProgram);
        }

        if (CliLinks.Occupied(BackupPath))
        {
            return Refused(CliLinkRefusal.BackupExists);
        }

        try
        {
            // Renames whatever is there, a symbolic link itself rather than what it names, and refuses a destination that exists.
            Directory.Move(LinkPath, BackupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CliLinkResult(CliLinkOutcome.Failed, Reason: ex.Message);
        }

        var written = Write(CliLinkOutcome.Replaced);

        return written.Outcome == CliLinkOutcome.Failed
            ? written with { Reason = $"{written.Reason}; what was there is kept as {BackupPath}" }
            : written;
    }

    /// <inheritdoc/>
    /// <remarks>Rewrites only a script keypaste wrote: one naming a program that is gone or an older version, or naming this program at another version.</remarks>
    public bool KeepCurrent()
    {
        var found = Inspect();
        var stale = found switch
        {
            { State: CliLinkState.ThisCopy } => found.Version != _version,
            { State: CliLinkState.OtherCopy, Named: { } named } => named.Program == _target.Program || !File.Exists(named.Program) || found.Version < _version,
            _ => false,
        };

        return stale && Write(CliLinkOutcome.Linked).Outcome == CliLinkOutcome.Linked;
    }

    /// <summary>Where the link is for one home.</summary>
    /// <param name="home">The person's home directory.</param>
    /// <returns><c>~/.local/bin/keypaste</c>.</returns>
    internal static string LinkPathFor(string home) => Path.Combine(home, ".local", "bin", McpServerLocator.FileName);

    private static CliLinkResult Refused(CliLinkRefusal refusal) => new(CliLinkOutcome.Refused, refusal);

    private Found Inspect()
    {
        var entry = new FileInfo(LinkPath);

        if (entry.LinkTarget is { } linkTarget)
        {
            return new Found(CliLinkState.Foreign, LinkedTo: linkTarget);
        }

        if (Directory.Exists(LinkPath))
        {
            return new Found(CliLinkState.Foreign);
        }

        if (!entry.Exists)
        {
            return new Found(CliLinkState.Absent);
        }

        try
        {
            if (entry.Length > CliLinkScript.MaximumLength
                || !CliLinkScript.TryRead(_strict.GetString(File.ReadAllBytes(LinkPath)), out var named, out var version))
            {
                return new Found(CliLinkState.Foreign);
            }

            return named == _target
                ? new Found(CliLinkState.ThisCopy, named, version)
                : new Found(CliLinkState.OtherCopy, named, version, named.Program);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return new Found(CliLinkState.Foreign);
        }
    }

    private CliLinkResult Write(CliLinkOutcome outcome)
    {
        if (CliLinkScript.Document(_target, _version) is not { } document)
        {
            return Refused(CliLinkRefusal.UnsafeProgram);
        }

        try
        {
            Directory.CreateDirectory(_directory);
            AtomicFile.Write(LinkPath, Encoding.UTF8.GetBytes(document), _executable);
            return new CliLinkResult(outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CliLinkResult(CliLinkOutcome.Failed, Reason: ex.Message);
        }
    }

    private bool DirectoryOnPath() =>
        (_pathVariable ?? string.Empty).Split(':').Any(entry =>
            string.Equals(entry.EndsWith('/') ? entry[..^1] : entry, _directory, StringComparison.Ordinal));

    private readonly record struct Found(CliLinkState State, CliTarget? Named = null, SemanticVersion Version = default, string? LinkedTo = null);
}
