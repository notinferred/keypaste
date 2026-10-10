using System.Globalization;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Linking;

/// <summary>Links the CLI on macOS as <c>/usr/local/bin/keypaste</c>, a symbolic link to the bundle's own <c>keypaste</c>.</summary>
/// <remarks>
/// <para>
/// <c>/usr/local/bin</c> is on every account's <c>PATH</c> and belongs to root, so the link is made through the system's
/// administrator dialog: <c>osascript</c> runs <see cref="Program"/> as root, and every path reaches it as an argument,
/// never as script text. It needs no Automation permission.
/// </para>
/// <para>
/// The program checks again as root what <see cref="Read"/> saw, because the dialog can stay open for minutes: it never
/// replaces a file keypaste did not link and never overwrites a backup.
/// </para>
/// </remarks>
public sealed class MacCliLink : ICliLink
{
    /// <summary>Where the link is.</summary>
    public const string DefaultLinkPath = "/usr/local/bin/keypaste";

    /// <summary>The program <c>osascript</c> runs as root, given the directory, the target, the link, the backup and the mode.</summary>
    internal const string Program = """
        dir=$1 target=$2 link=$3 backup=$4 mode=$5
        /bin/mkdir -p "$dir" || exit 1
        if [ "$mode" = replace ]; then
          if [ -e "$backup" ] || [ -L "$backup" ]; then exit 3; fi
          if [ -e "$link" ] || [ -L "$link" ]; then /bin/mv "$link" "$backup" || exit 1; fi
        elif [ -e "$link" ] || [ -L "$link" ]; then
          case "$(/usr/bin/readlink "$link")" in */Contents/MacOS/keypaste) ;; *) exit 4 ;; esac
        fi
        exec /bin/ln -sfn "$target" "$link"
        """;

    /// <summary>The words the administrator dialog shows.</summary>
    internal const string Prompt = "keypaste is linking its command into /usr/local/bin so a terminal can run it.";

    private const string _osascript = "/usr/bin/osascript";
    private const string _bundled = "/Contents/MacOS/keypaste";
    private const string _translocated = "/AppTranslocation/";
    private const string _errorMarker = "execution error: ";

    private const string _shellScript =
        "do shell script \"/bin/sh -c \" & quoted form of (item 1 of argv) & \" keypaste-link \" & quoted form of (item 2 of argv)"
        + " & \" \" & quoted form of (item 3 of argv) & \" \" & quoted form of (item 4 of argv)"
        + " & \" \" & quoted form of (item 5 of argv) & \" \" & quoted form of (item 6 of argv)";

    private const string _elevated = " with prompt \"" + Prompt + "\" with administrator privileges";

    private const int _userCancelled = -128;
    private const int _backupTaken = 3;
    private const int _notKeypaste = 4;

    private static readonly TimeSpan _answerWindow = TimeSpan.FromMinutes(5);

    // Nothing is written to osascript's input, not even a preamble, which fails the start once osascript has closed it.
    private static readonly Encoding _noPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _target;
    private readonly IProcessRunner _runner;
    private readonly bool _administrator;

    /// <summary>Initializes the link to one bundle's <c>keypaste</c>.</summary>
    /// <param name="target">The bundle's <c>Contents/MacOS/keypaste</c>.</param>
    /// <param name="runner">Runs <c>osascript</c>.</param>
    public MacCliLink(string target, IProcessRunner runner)
        : this(target, DefaultLinkPath, runner, administrator: true)
    {
    }

    internal MacCliLink(string target, string linkPath, IProcessRunner runner, bool administrator)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentException.ThrowIfNullOrEmpty(linkPath);
        ArgumentNullException.ThrowIfNull(runner);

        _target = target;
        LinkPath = linkPath;
        BackupPath = linkPath + CliLinks.BackupSuffix;
        _runner = runner;
        _administrator = administrator;
    }

    /// <inheritdoc/>
    public string LinkPath { get; }

    /// <inheritdoc/>
    public string BackupPath { get; }

    /// <summary>Whether <paramref name="path"/> is in the read-only copy Gatekeeper runs a downloaded app from, which is gone once it quits.</summary>
    /// <param name="path">A path inside the app.</param>
    /// <returns><see langword="true"/> for a translocated path.</returns>
    public static bool IsTranslocated(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Contains(_translocated, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public CliLinkStatus Read()
    {
        var (state, linkedTo) = Inspect();

        return new CliLinkStatus(state, LinkPath, linkedTo, BackupPath, CliLinks.Occupied(BackupPath), DirectoryOnPath: true, IsTranslocated(_target));
    }

    /// <inheritdoc/>
    public CliLinkResult Link()
    {
        if (IsTranslocated(_target))
        {
            return Refused(CliLinkRefusal.Translocated);
        }

        return Inspect().State switch
        {
            CliLinkState.Foreign => Refused(CliLinkRefusal.NotKeypaste),
            CliLinkState.ThisCopy => new CliLinkResult(CliLinkOutcome.Linked),
            _ => Ask(replace: false),
        };
    }

    /// <inheritdoc/>
    public CliLinkResult Replace()
    {
        if (IsTranslocated(_target))
        {
            return Refused(CliLinkRefusal.Translocated);
        }

        if (Inspect().State != CliLinkState.Foreign)
        {
            return Link();
        }

        return CliLinks.Occupied(BackupPath) ? Refused(CliLinkRefusal.BackupExists) : Ask(replace: true);
    }

    /// <inheritdoc/>
    /// <returns><see langword="false"/>: the link names the bundle's fixed path, which an upgrade replaces in place.</returns>
    public bool KeepCurrent() => false;

    /// <summary>What <c>osascript</c> is given: a fixed script, <see cref="Program"/>, then each path as an argument.</summary>
    /// <param name="target">The bundle's <c>keypaste</c>.</param>
    /// <param name="linkPath">Where the link goes.</param>
    /// <param name="replace">Whether to move what is there to the backup first.</param>
    /// <param name="administrator">Whether to run as root behind the dialog; only tests pass <see langword="false"/>.</param>
    /// <returns>The arguments.</returns>
    internal static IReadOnlyList<string> Arguments(string target, string linkPath, bool replace, bool administrator) =>
    [
        "-e", "on run argv",
        "-e", administrator ? _shellScript + _elevated : _shellScript,
        "-e", "end run",
        Program,
        Path.GetDirectoryName(linkPath)!,
        target,
        linkPath,
        linkPath + CliLinks.BackupSuffix,
        replace ? "replace" : "link",
    ];

    private static CliLinkResult Refused(CliLinkRefusal refusal) => new(CliLinkOutcome.Refused, refusal);

    // osascript reports a script's error as "<range>: execution error: <message> (<number>)", the number being the program's exit status.
    private static (int? Number, string Message) ScriptError(string standardError)
    {
        var text = standardError.Trim();
        var marker = text.IndexOf(_errorMarker, StringComparison.Ordinal);
        var open = text.LastIndexOf(" (", StringComparison.Ordinal);

        if (marker < 0
            || open < marker + _errorMarker.Length
            || !text.EndsWith(')')
            || !int.TryParse(text.AsSpan(open + 2, text.Length - open - 3), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return (null, text);
        }

        return (number, text[(marker + _errorMarker.Length)..open]);
    }

    private (CliLinkState State, string? LinkedTo) Inspect()
    {
        var linkTarget = new FileInfo(LinkPath).LinkTarget;

        if (linkTarget is null)
        {
            return (File.Exists(LinkPath) || Directory.Exists(LinkPath) ? CliLinkState.Foreign : CliLinkState.Absent, null);
        }

        if (string.Equals(linkTarget, _target, StringComparison.Ordinal))
        {
            return (CliLinkState.ThisCopy, null);
        }

        return (linkTarget.EndsWith(_bundled, StringComparison.Ordinal) ? CliLinkState.OtherCopy : CliLinkState.Foreign, linkTarget);
    }

    private CliLinkResult Ask(bool replace)
    {
        var result = _runner.Run(_osascript, Arguments(_target, LinkPath, replace, _administrator), stdin: null, _noPreamble, _answerWindow);

        if (result.Succeeded)
        {
            return Inspect().State == CliLinkState.ThisCopy
                ? new CliLinkResult(replace ? CliLinkOutcome.Replaced : CliLinkOutcome.Linked)
                : new CliLinkResult(CliLinkOutcome.Failed, Reason: "the link was not made");
        }

        if (!result.ToolFound)
        {
            return new CliLinkResult(CliLinkOutcome.Failed, Reason: $"{_osascript} was not found");
        }

        var (number, message) = ScriptError(result.StandardError);

        return number switch
        {
            _userCancelled => new CliLinkResult(CliLinkOutcome.Cancelled),
            _backupTaken => Refused(CliLinkRefusal.BackupExists),
            _notKeypaste => Refused(CliLinkRefusal.NotKeypaste),
            _ => new CliLinkResult(CliLinkOutcome.Failed, Reason: message.Length > 0 ? message : $"{_osascript} exited with {result.ExitCode}"),
        };
    }
}
