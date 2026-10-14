using System.Diagnostics;
using System.Runtime.Versioning;
using Keypaste.Core.Approval;
using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The app leaves Terminal's launcher each run as one owner-only file in an owner-only folder, one field per line in
/// printable ASCII and never a value, so a path holding a line break is carried like any other (E.1d, D-0451).
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class TerminalRequestsTests : IDisposable
{
    private const string _unix = "Terminal's requests are macOS files, checked here wherever Unix modes exist.";
    private const string _pass = "yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5uc";
    private const UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode _ownerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-terminal-requests-").FullName;
    private readonly ManualClock _clock = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder => Path.Combine(_root, TerminalRequests.FolderName);

    [Fact]
    public void A_request_is_one_owner_only_file_in_an_owner_only_folder_holding_one_field_per_line()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var requests = new TerminalRequests(Folder, _clock);

        Assert.True(requests.TryWrite(Request("npm run \"dev\" $HOME"), out var run, out var refusal), refusal);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(requests.TryWrite(Request(command: null), out var open, out refusal), refusal);

        Assert.Equal(Folder, Path.GetDirectoryName(run));
        Assert.Matches("^[0-9]{19}-[0-9a-f]{16}\\.request$", Path.GetFileName(run));
        Assert.Equal(_ownerOnlyFolder, File.GetUnixFileMode(Folder));
        Assert.Equal(_ownerOnlyFile, File.GetUnixFileMode(run));
        Assert.Equal(_ownerOnlyFile, File.GetUnixFileMode(open));
        Assert.Equal([run, open], Directory.GetFiles(Folder).Order(StringComparer.Ordinal));

        Assert.Equal(Lines("run", "npm run \"dev\" $HOME"), File.ReadAllText(run).Split('\n'));
        Assert.Equal(Lines("open", string.Empty), File.ReadAllText(open).Split('\n'));
    }

    [Fact]
    public void The_folder_is_made_owner_only_and_a_link_or_a_file_in_its_place_is_refused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var requests = new TerminalRequests(Folder, _clock);
        Directory.CreateDirectory(Folder);
        File.SetUnixFileMode(Folder, _ownerOnlyFolder | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Assert.True(requests.TryWrite(Request(), out _, out _));
        Assert.Equal(_ownerOnlyFolder, File.GetUnixFileMode(Folder));

        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
        Directory.Delete(Folder, recursive: true);
        Directory.CreateSymbolicLink(Folder, elsewhere);

        Assert.False(requests.TryWrite(Request(), out var path, out var refusal));
        Assert.Null(path);
        Assert.Equal($"the run could not be handed to Terminal: {Folder} is not a folder keypaste made. Delete it, then try again", refusal);
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));

        File.Delete(Folder);
        File.WriteAllText(Folder, "not a folder");

        Assert.False(requests.TryWrite(Request(), out _, out refusal));
        Assert.Equal($"the run could not be handed to Terminal: {Folder} is not a folder keypaste made. Delete it, then try again", refusal);
        Assert.Equal("not a folder", File.ReadAllText(Folder));
    }

    /// <summary>Every byte outside printable ASCII, and every backslash, is written as <c>\0</c> and three octal digits, which the launcher's <c>printf '%b'</c> turns back into it (D-0451).</summary>
    [Theory]
    [InlineData(2, "/Users/me/.keypaste\n", @"/Users/me/.keypaste\0012")]
    [InlineData(3, "/Users/me/a\rb\\c.kdbx", @"/Users/me/a\0015b\0134c.kdbx")]
    [InlineData(4, "kp\u007f", @"kp\0177")]
    [InlineData(5, "/var/x\t/", @"/var/x\0011/")]
    [InlineData(6, "web\u001b[2J", @"web\0033[2J")]
    [InlineData(7, "staging\u0001", @"staging\0001")]
    [InlineData(8, "/Users/me/web\napp\n", @"/Users/me/web\0012app\0012")]
    [InlineData(8, "/Users/me/café \u0085 日本", @"/Users/me/caf\0303\0251 \0302\0205 \0346\0227\0245\0346\0234\0254")]
    [InlineData(10, "printf '\\c\\0101' %b %s", @"printf '\0134c\01340101' %b %s")]
    public void A_field_is_written_as_one_line_of_printable_ASCII(int line, string value, string written)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var request = line switch
        {
            2 => Request() with { Home = value },
            3 => Request() with { Vault = value },
            4 => Request() with { Approver = value },
            5 => Request() with { TemporaryDirectory = value },
            6 => Request() with { Project = value },
            7 => Request() with { Profile = value },
            8 => Request() with { Directory = value },
            _ => Request(value),
        };

        Assert.True(new TerminalRequests(Folder, _clock).TryWrite(request, out var path, out var refusal), refusal);

        var lines = Lines("run", "npm start");
        lines[line] = written;
        Assert.Equal(lines, File.ReadAllText(path).Split('\n'));
        Assert.All(File.ReadAllBytes(path), b => Assert.True(b is (byte)'\n' or >= 0x20 and < 0x7f, $"byte {b} is not printable ASCII"));
    }

    [Fact]
    public void A_field_holding_U0000_which_no_path_holds_is_refused_and_nothing_is_written()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        Assert.Throws<ArgumentException>(() => new TerminalRequests(Folder, _clock).TryWrite(Request() with { Directory = "/Users/me/a\0b" }, out _, out _));
        Assert.Throws<ArgumentException>(() => new TerminalRequests(Folder, _clock).TryWrite(Request("make\0"), out _, out _));
        Assert.False(Directory.Exists(Folder));
    }

    /// <summary>A project whose name starts with <c>-</c> is a line like any other, which the launcher hands over as <c>--project=</c>'s value (D-0453).</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-p")]
    [InlineData("--")]
    [InlineData("-web")]
    [InlineData("--env-file=x")]
    public void A_project_whose_name_starts_with_a_dash_is_written_as_its_own_line(string project)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        Assert.True(new TerminalRequests(Folder, _clock).TryWrite(Request() with { Project = project }, out var path, out var refusal), refusal);

        var lines = Lines("run", "npm start");
        lines[6] = project;
        Assert.Equal(lines, File.ReadAllText(path).Split('\n'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5u")]
    [InlineData("yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5ucA")]
    [InlineData("yMnKy8zNzs+Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5uc")]
    [InlineData("yMnKy8zNzs/Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5uc")]
    [InlineData("yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5u=")]
    [InlineData("yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5u\n")]
    [InlineData("yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5ué")]
    public void A_pass_without_a_pass_shape_is_refused_and_nothing_is_written(string pass)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        Assert.False(LaunchPasses.IsWellFormed(pass));
        Assert.Throws<ArgumentException>(() => new TerminalRequests(Folder, _clock).TryWrite(Request() with { Pass = pass }, out _, out _));
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void Names_sort_oldest_first_and_two_writes_at_one_instant_keep_both()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var requests = new TerminalRequests(Folder, _clock);
        List<string> written = [];

        foreach (var command in new[] { "first", "second", "third" })
        {
            Assert.True(requests.TryWrite(Request(command), out var path, out _));
            written.Add(path);
            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.True(requests.TryWrite(Request("fourth"), out var fourth, out _));
        Assert.True(requests.TryWrite(Request("fifth"), out var fifth, out _));

        Assert.Equal(written, Directory.GetFiles(Folder).Order(StringComparer.Ordinal).Take(3));
        Assert.NotEqual(fourth, fifth);
        Assert.Equal("fourth", File.ReadAllText(fourth).Split('\n')[10]);
        Assert.Equal("fifth", File.ReadAllText(fifth).Split('\n')[10]);
    }

    [Fact]
    public void A_write_first_deletes_every_request_older_than_a_pass_lasts()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var requests = new TerminalRequests(Folder, _clock);
        Assert.True(requests.TryWrite(Request("stale"), out var stale, out _));
        var claimed = stale + ".4242";
        File.Copy(stale, claimed);
        File.WriteAllText(Path.Combine(Folder, "stray"), string.Empty);

        _clock.Advance(LaunchPasses.Lifetime - TimeSpan.FromSeconds(1));
        Assert.True(requests.TryWrite(Request("recent"), out var recent, out _));
        Assert.Equal([stale, claimed, recent], Directory.GetFiles(Folder).Order(StringComparer.Ordinal));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(requests.TryWrite(Request("fresh"), out var fresh, out _));
        Assert.Equal([recent, fresh], Directory.GetFiles(Folder).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Withdraw_deletes_one_request_in_the_folder_and_nothing_else()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var requests = new TerminalRequests(Folder, _clock);
        Assert.True(requests.TryWrite(Request("kept"), out var kept, out _));
        Assert.True(requests.TryWrite(Request("withdrawn"), out var withdrawn, out _));
        var outside = Path.Combine(_root, Path.GetFileName(kept));
        File.Copy(kept, outside);

        requests.Withdraw(withdrawn);
        requests.Withdraw(withdrawn);

        Assert.Equal([kept], Directory.GetFiles(Folder));
        Assert.Throws<ArgumentException>(() => requests.Withdraw(outside));
        Assert.Throws<ArgumentException>(() => requests.Withdraw(Path.Combine(Folder, "stray")));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void This_users_requests_are_in_the_folder_getconf_names_for_the_launcher_whatever_its_TMPDIR()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("DARWIN_USER_TEMP_DIR exists on macOS only.");
            return;
        }

        var start = new ProcessStartInfo("/usr/bin/getconf") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("DARWIN_USER_TEMP_DIR");
        start.Environment["TMPDIR"] = _root;
        using var getconf = Process.Start(start)!;
        var temporary = getconf.StandardOutput.ReadToEnd().TrimEnd('\n');
        getconf.WaitForExit();

        Assert.Equal(0, getconf.ExitCode);
        Assert.Equal(Path.Combine(temporary, TerminalRequests.FolderName), TerminalRequests.ForThisUser()?.Folder);
    }

    private static TerminalRequest Request(string? command = "npm start") =>
        new(_pass, "/Users/me/.keypaste", "/Users/me/vaults/it's \"mine\".kdbx", "keypaste-approver", "/var/folders/x/T/", "web app", "staging", "/Users/me/code/web app", command);

    private static string[] Lines(string kind, string command) =>
        ["keypaste-launch 2", _pass, "/Users/me/.keypaste", "/Users/me/vaults/it's \"mine\".kdbx", "keypaste-approver", "/var/folders/x/T/", "web app", "staging", "/Users/me/code/web app", kind, command, string.Empty];
}
