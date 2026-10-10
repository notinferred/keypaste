using System.Diagnostics;
using System.Runtime.Versioning;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Linking;
using Xunit;

namespace Keypaste.Core.Tests.Linking;

/// <summary>The Linux link starts this copy with every argument unchanged, and replaces only what keypaste wrote unless the person confirms (G.5).</summary>
[UnsupportedOSPlatform("windows")]
public sealed class LinuxCliLinkTests : IDisposable
{
    private const string _unix = "The Linux link is a sh script; Windows puts the CLI on PATH from its installer.";

    private const UnixFileMode _executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly SemanticVersion _current = Version("0.5.0");

    private static readonly string[] _awkwardArguments = ["a'b", "\"c d\"", "$HOME", "`x`", "\\", string.Empty, "-leading"];

    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-link-").FullName;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string LinkPath => Path.Combine(_home, ".local", "bin", "keypaste");

    private string BackupPath => LinkPath + ".keypaste-backup";

    [Fact]
    public void The_script_starts_an_awkwardly_named_image_with_cli_and_every_argument_unchanged()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var image = Program("Apps/key paste's $HOME `x` \\ é/keypaste.AppImage");
        var link = For(image, throughAppImage: true);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), link.Link());
        Assert.Equal(LinkPath, link.LinkPath);
        Assert.Equal(_executable, File.GetUnixFileMode(LinkPath));
        Assert.Equal(["cli", .. _awkwardArguments], Run(LinkPath, _awkwardArguments));
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);
    }

    [Fact]
    public void Without_an_AppImage_the_script_starts_the_keypaste_beside_the_app_with_no_cli()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(Program("opt/keypaste/keypaste"), throughAppImage: false);

        Assert.Equal(CliLinkOutcome.Linked, link.Link().Outcome);
        Assert.Equal(_awkwardArguments, Run(LinkPath, _awkwardArguments));
    }

    [Theory]
    [InlineData('\n')]
    [InlineData('\r')]
    [InlineData('\0')]
    [InlineData(null)]
    public void A_program_path_the_script_cannot_hold_is_refused_and_nothing_is_written(char? breaker)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(breaker is { } c ? $"/opt/key{c}paste/keypaste" : "opt/keypaste/keypaste", throughAppImage: true);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.UnsafeProgram), link.Link());
        Assert.False(Directory.Exists(Path.Combine(_home, ".local")));
    }

    [Fact]
    public void Nothing_there_reads_as_absent_and_another_copys_script_names_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var mine = For(Program("new/keypaste.AppImage"), throughAppImage: true);
        var other = Program("old/keypaste.AppImage");

        Assert.Equal(
            new CliLinkStatus(CliLinkState.Absent, LinkPath, null, BackupPath, BackupExists: false, DirectoryOnPath: false, Translocated: false),
            mine.Read());

        Assert.Equal(CliLinkOutcome.Linked, For(other, throughAppImage: true).Link().Outcome);

        var status = mine.Read();
        Assert.Equal(CliLinkState.OtherCopy, status.State);
        Assert.Equal(other, status.LinkedTo);
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("edited")]
    [InlineData("symlink")]
    [InlineData("dangling")]
    [InlineData("directory")]
    public void Anything_keypaste_did_not_write_reads_as_foreign_and_linking_leaves_it_alone(string kind)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(Program("app/keypaste.AppImage"), throughAppImage: true);
        var linkedTo = Plant(kind, link);
        var before = Snapshot(LinkPath);

        var status = link.Read();
        Assert.Equal(CliLinkState.Foreign, status.State);
        Assert.Equal(linkedTo, status.LinkedTo);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.NotKeypaste), link.Link());
        Assert.False(link.KeepCurrent());
        Assert.Equal(before, Snapshot(LinkPath));
        Assert.False(CliLinks.Occupied(BackupPath));
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("symlink")]
    [InlineData("dangling")]
    [InlineData("directory")]
    public void Replacing_keeps_what_was_there_as_the_backup_and_links_this_copy(string kind)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(Program("app/keypaste.AppImage"), throughAppImage: true);
        Plant(kind, link);
        var before = Snapshot(LinkPath);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Replaced), link.Replace());
        Assert.Equal(before, Snapshot(BackupPath));
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);
        Assert.True(link.Read().BackupExists);
        Assert.Equal(["cli", "--version"], Run(LinkPath, "--version"));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("dangling")]
    public void Replacing_never_overwrites_a_backup_and_moves_nothing(string kind)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(Program("app/keypaste.AppImage"), throughAppImage: true);
        Plant("binary", link);

        if (kind == "file")
        {
            File.WriteAllText(BackupPath, "the person's original");
        }
        else
        {
            File.CreateSymbolicLink(BackupPath, Path.Combine(_home, "nowhere"));
        }

        var linkBefore = Snapshot(LinkPath);
        var backupBefore = Snapshot(BackupPath);

        Assert.True(link.Read().BackupExists);
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.BackupExists), link.Replace());
        Assert.Equal(linkBefore, Snapshot(LinkPath));
        Assert.Equal(backupBefore, Snapshot(BackupPath));
    }

    [Fact]
    public void Replacing_where_nothing_foreign_is_there_links_and_keeps_no_backup()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = For(Program("new/keypaste.AppImage"), throughAppImage: true);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), link.Replace());
        Assert.Equal(CliLinkOutcome.Linked, For(Program("old/keypaste.AppImage"), throughAppImage: true).Link().Outcome);
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), link.Replace());
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);
        Assert.False(CliLinks.Occupied(BackupPath));
    }

    [Theory]
    [InlineData("0.4.0", true)]
    [InlineData("0.5.0-rc.1", true)]
    [InlineData("0.5.0", false)]
    [InlineData("0.6.0", false)]
    public void A_newer_copy_starting_re_points_a_script_naming_an_older_one(string written, bool rewritten)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var other = Program("other/keypaste.AppImage");
        var mine = For(Program("mine/keypaste.AppImage"), throughAppImage: true);
        For(other, throughAppImage: true, Version(written)).Link();

        Assert.Equal(rewritten, mine.KeepCurrent());
        Assert.Equal(rewritten ? CliLinkState.ThisCopy : CliLinkState.OtherCopy, mine.Read().State);
        Assert.Contains($"# keypaste {(rewritten ? "0.5.0" : written)} wrote this", File.ReadAllText(LinkPath), StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_naming_a_program_that_is_gone_is_re_pointed_whatever_version_wrote_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var mine = For(Program("mine/keypaste.AppImage"), throughAppImage: true);
        For(Path.Combine(_home, "gone", "keypaste.AppImage"), throughAppImage: true, Version("9.0.0")).Link();

        Assert.True(mine.KeepCurrent());
        Assert.Equal(CliLinkState.ThisCopy, mine.Read().State);
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.6.0")]
    public void A_script_naming_this_program_at_another_version_is_rewritten(string written)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var image = Program("mine/keypaste.AppImage");
        For(image, throughAppImage: true, Version(written)).Link();
        var mine = For(image, throughAppImage: true);

        Assert.Equal(CliLinkState.ThisCopy, mine.Read().State);
        Assert.True(mine.KeepCurrent());
        Assert.Contains("# keypaste 0.5.0 wrote this", File.ReadAllText(LinkPath), StringComparison.Ordinal);
        Assert.False(mine.KeepCurrent());
    }

    [Fact]
    public void Starting_creates_no_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        Assert.False(For(Program("mine/keypaste.AppImage"), throughAppImage: true).KeepCurrent());
        Assert.False(CliLinks.Occupied(LinkPath));
    }

    [Theory]
    [InlineData("/usr/bin:{0}/.local/bin:/bin", true)]
    [InlineData("{0}/.local/bin/", true)]
    [InlineData("/usr/bin:{0}/.local/bin//:/bin", false)]
    [InlineData("/usr/bin:{0}/.local:/bin", false)]
    [InlineData("", false)]
    public void The_status_says_whether_the_links_directory_is_on_PATH(string path, bool onPath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var link = new LinuxCliLink(_home, new CliTarget("/opt/keypaste/keypaste", ThroughAppImage: false), _current, path.Replace("{0}", _home, StringComparison.Ordinal));

        Assert.Equal(onPath, link.Read().DirectoryOnPath);
    }

    [Fact]
    public void Inside_an_AppImage_the_link_starts_the_image_and_only_when_both_variables_agree()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var image = Program("Applications/keypaste.AppImage");
        var mount = Path.Combine(_home, "mount");
        var app = Path.GetDirectoryName(Program("mount/usr/bin/keypaste"))!;
        var beside = new CliTarget(Path.Combine(app, "keypaste"), ThroughAppImage: false);

        Assert.Equal(new CliTarget(image, ThroughAppImage: true), CliLinks.TargetFor(_home, app, image, mount));
        Assert.Equal(beside, CliLinks.TargetFor(_home, app, image, Path.Combine(_home, "elsewhere")));
        Assert.Equal(beside, CliLinks.TargetFor(_home, app, Path.Combine(_home, "missing.AppImage"), mount));
        Assert.Equal(beside, CliLinks.TargetFor(_home, app, null, null));
        Assert.Null(CliLinks.TargetFor(_home, Path.Combine(_home, "Applications"), null, null));
    }

    [Fact]
    public void A_program_at_the_links_own_path_gets_no_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var bin = Path.GetDirectoryName(Program(".local/bin/keypaste"))!;
        Assert.Null(CliLinks.TargetFor(_home, bin, null, null));

        var mount = Path.Combine(_home, "mount");
        var app = Path.GetDirectoryName(Program("mount/usr/bin/keypaste"))!;
        Assert.Null(CliLinks.TargetFor(_home, app, LinkPath, mount));
    }

    [Fact]
    public void A_program_reached_through_a_linked_bin_directory_gets_no_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _unix);

        var real = Path.GetDirectoryName(Program("real/bin/keypaste"))!;
        Directory.CreateDirectory(Path.Combine(_home, ".local"));
        Directory.CreateSymbolicLink(Path.Combine(_home, ".local", "bin"), real);

        Assert.Null(CliLinks.TargetFor(_home, real, null, null));
    }

    private static SemanticVersion Version(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        return version;
    }

    private static List<string> Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, UseShellExecute = false };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[1..^1])];
    }

    // What is at a path, without following a link there: its kind, its target or bytes, its mode and what a directory holds.
    private static string Snapshot(string path)
    {
        var entry = new FileInfo(path);

        if (entry.LinkTarget is { } target)
        {
            return $"link {target}";
        }

        if (Directory.Exists(path))
        {
            return $"directory {string.Join(',', Directory.GetFileSystemEntries(path).Select(Path.GetFileName).Order(StringComparer.Ordinal))}";
        }

        return $"file {File.GetUnixFileMode(path)} {Convert.ToHexString(File.ReadAllBytes(path))}";
    }

    private LinuxCliLink For(string program, bool throughAppImage, SemanticVersion? version = null) =>
        new(_home, new CliTarget(program, throughAppImage), version ?? _current, pathVariable: null);

    // A program that prints each argument it was given on a line of its own, in brackets.
    private string Program(string relative)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\nfor argument in \"$@\"; do printf '[%s]\\n' \"$argument\"; done\n");
        File.SetUnixFileMode(path, _executable);
        return path;
    }

    private string? Plant(string kind, LinuxCliLink link)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);

        switch (kind)
        {
            case "binary":
                File.WriteAllBytes(LinkPath, [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0]);
                File.SetUnixFileMode(LinkPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return null;
            case "edited":
                Assert.Equal(CliLinkOutcome.Linked, link.Link().Outcome);
                File.WriteAllText(LinkPath, File.ReadAllText(LinkPath).Replace("re-points", "re-pointz", StringComparison.Ordinal));
                return null;
            case "symlink":
                var program = Program("elsewhere/keypaste");
                File.CreateSymbolicLink(LinkPath, program);
                return program;
            case "dangling":
                var gone = Path.Combine(_home, "gone", "keypaste");
                File.CreateSymbolicLink(LinkPath, gone);
                return gone;
            case "directory":
                Directory.CreateDirectory(LinkPath);
                File.WriteAllText(Path.Combine(LinkPath, "README"), "the person's");
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
