using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.Core.Approval;

namespace Keypaste.Core.Launch;

/// <summary>One run the app hands Terminal's launcher on macOS: the window's <c>keypaste run --session</c> arguments and its pass, never a value.</summary>
/// <param name="Pass">The owner's launch pass for exactly this run.</param>
/// <param name="Home">keypaste's home folder, given to the run as <c>KEYPASTE_HOME</c>.</param>
/// <param name="Vault">The vault's path, given as <c>--vault</c>.</param>
/// <param name="Approver">The app's launch endpoint, given as <c>--approver</c>.</param>
/// <param name="TemporaryDirectory">The app's temporary folder, given as <c>TMPDIR</c>, under which .NET puts the endpoint's socket.</param>
/// <param name="Project">The project.</param>
/// <param name="Profile">The project's environment, given as <c>-p</c>.</param>
/// <param name="Directory">Where the run starts.</param>
/// <param name="Command">The person's command, or null to open their shell.</param>
public sealed record TerminalRequest(
    string Pass,
    string Home,
    string Vault,
    string Approver,
    string TemporaryDirectory,
    string Project,
    string Profile,
    string Directory,
    string? Command);

/// <summary>
/// The requests the app leaves for Terminal's launcher, one private file per run in a private folder
/// (D-0451).
/// </summary>
/// <remarks>
/// <para>
/// A request is one field per line: the format, the pass, the home, vault, endpoint and temporary
/// folder, the project, environment and directory, <c>run</c> or <c>open</c>, and the command. Each
/// line is printable ASCII: every byte of a field's UTF-8 outside it, and every backslash, is written as
/// <c>\0</c> and three octal digits, which the launcher's <c>printf '%b'</c> turns back into that
/// byte, so a path holding a line break still takes one line. The launcher claims the oldest request
/// by renaming it, deletes it, and hands every field to <c>keypaste run</c> as an argument or a
/// variable, never as script text.
/// </para>
/// <para>
/// It is written under another name and renamed into place, so a launcher never reads half of one.
/// Its name starts with the time it was written, so names sort oldest first.
/// </para>
/// </remarks>
public sealed class TerminalRequests
{
    /// <summary>The folder's name inside the per-user temporary folder.</summary>
    public const string FolderName = "keypaste-launch";

    private const string _format = "keypaste-launch 2";
    private const string _extension = ".request";
    private const string _partialExtension = ".partial";
    private const int _ticksDigits = 19;
    private const int _darwinUserTempDir = 65537;
    private const UnixFileMode _ownerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TimeProvider _clock;

    /// <summary>Keeps requests in one folder.</summary>
    /// <param name="folder">The folder, which is created owner-only when the first request is written.</param>
    /// <param name="clock">The clock a request's age is read on.</param>
    public TerminalRequests(string folder, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        Folder = folder;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The folder the requests are in.</summary>
    public string Folder { get; }

    /// <summary>
    /// The requests of the person this runs as, in the per-user temporary folder macOS gives every
    /// process whatever its <c>TMPDIR</c>, which the launcher reads with <c>getconf</c>.
    /// </summary>
    /// <returns>The requests, or null when macOS names no such folder.</returns>
    /// <remarks>Its parent is the person's own and owner-only, so no one else can create anything in it.</remarks>
    [SupportedOSPlatform("macos")]
    public static TerminalRequests? ForThisUser() =>
        DarwinUserTempDir() is { } temporary ? new TerminalRequests(Path.Combine(temporary, FolderName)) : null;

    /// <summary>Writes a request, first deleting every request older than a pass lasts.</summary>
    /// <param name="request">The request.</param>
    /// <param name="path">The request's file, when this returns true.</param>
    /// <param name="refusal">Why nothing was written, when this returns false.</param>
    /// <returns>Whether the request was written.</returns>
    /// <exception cref="ArgumentException">The pass is not well formed, or a field is empty or holds U+0000, which no path, argument or variable can.</exception>
    [UnsupportedOSPlatform("windows")]
    public bool TryWrite(TerminalRequest request, [NotNullWhen(true)] out string? path, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!LaunchPasses.IsWellFormed(request.Pass))
        {
            throw new ArgumentException("a launch pass is 43 base64url characters", nameof(request));
        }

        path = null;
        string[] fields = [request.Home, request.Vault, request.Approver, request.TemporaryDirectory, request.Project, request.Profile, request.Directory];

        if (fields.Any(string.IsNullOrEmpty) || request.Command is { Length: 0 })
        {
            throw new ArgumentException("a request's fields are never empty", nameof(request));
        }

        if (fields.Append(request.Command ?? string.Empty).Any(field => field.Contains('\0', StringComparison.Ordinal)))
        {
            throw new ArgumentException("no path, argument or variable holds U+0000", nameof(request));
        }

        var bytes = _utf8.GetBytes(Text(request));
        string? partial = null;

        try
        {
            if (!TryPrepareFolder(out refusal))
            {
                return false;
            }

            Sweep();

            var name = $"{_clock.GetUtcNow().UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}";
            partial = Path.Combine(Folder, name + _partialExtension);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = _ownerOnlyFile,
            };

            using (var stream = new FileStream(partial, options))
            {
                stream.Write(bytes);
            }

            var whole = Path.Combine(Folder, name + _extension);
            File.Move(partial, whole);
            partial = null;

            path = whole;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = $"the run could not be handed to Terminal: {ex.Message}";
            return false;
        }
        finally
        {
            if (partial is not null)
            {
                Delete(partial);
            }
        }
    }

    /// <summary>Deletes a request the launcher has not claimed, such as one whose Terminal did not open.</summary>
    /// <param name="path">The request's file, as <see cref="TryWrite"/> gave it.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a request in this folder.</exception>
    public void Withdraw(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!string.Equals(Path.GetDirectoryName(path), Folder, StringComparison.Ordinal) || !path.EndsWith(_extension, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{path} is not a request in {Folder}", nameof(path));
        }

        Delete(path);
    }

    private static string Text(TerminalRequest request)
    {
        string[] lines =
        [
            _format,
            request.Pass,
            Line(request.Home),
            Line(request.Vault),
            Line(request.Approver),
            Line(request.TemporaryDirectory),
            Line(request.Project),
            Line(request.Profile),
            Line(request.Directory),
            request.Command is null ? "open" : "run",
            Line(request.Command ?? string.Empty),
        ];

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>A field as printable ASCII, every byte of its UTF-8 outside it and every backslash written as <c>\0</c> and three octal digits.</summary>
    private static string Line(string field)
    {
        var line = new StringBuilder(field.Length);

        foreach (var b in _utf8.GetBytes(field))
        {
            if (b is >= 0x20 and < 0x7f && b != '\\')
            {
                line.Append((char)b);
            }
            else
            {
                line.Append(@"\0").Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            }
        }

        return line.ToString();
    }

    [UnsupportedOSPlatform("windows")]
    private bool TryPrepareFolder(out string refusal)
    {
        refusal = string.Empty;

        if (IsLink(Folder) || File.Exists(Folder))
        {
            refusal = $"the run could not be handed to Terminal: {Folder} is not a folder keypaste made. Delete it, then try again";
            return false;
        }

        Directory.CreateDirectory(Folder, _ownerOnlyFolder);

        if (IsLink(Folder))
        {
            refusal = $"the run could not be handed to Terminal: {Folder} is not a folder keypaste made. Delete it, then try again";
            return false;
        }

        File.SetUnixFileMode(Folder, _ownerOnlyFolder);
        return true;
    }

    /// <summary>Deletes every file older than a pass lasts, and any whose name does not start with its time, so no stale request sits ahead of a fresh one.</summary>
    private void Sweep()
    {
        if (IsLink(Folder))
        {
            return;
        }

        var oldest = _clock.GetUtcNow().UtcTicks - LaunchPasses.Lifetime.Ticks;

        foreach (var file in Directory.EnumerateFiles(Folder).ToList())
        {
            var name = Path.GetFileName(file);

            if (name.Length < _ticksDigits
                || !long.TryParse(name.AsSpan(0, _ticksDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < oldest)
            {
                Delete(file);
            }
        }
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next write's sweep.
        }
    }

    private static string? DarwinUserTempDir()
    {
        try
        {
            var length = confstr(_darwinUserTempDir, null, 0);

            if (length is 0 or > 4096)
            {
                return null;
            }

            var buffer = new byte[length];

            return confstr(_darwinUserTempDir, buffer, length) == length && length > 1
                ? Encoding.UTF8.GetString(buffer, 0, (int)length - 1)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "confstr")]
    private static extern nuint confstr(int name, byte[]? buffer, nuint length);
}
