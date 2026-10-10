using System.Text;
using Keypaste.Core.Linking;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests.Linking;

/// <summary>
/// The macOS link is made by a fixed program run as root behind the administrator dialog, every path an argument, and it
/// replaces only what keypaste linked unless the person confirms (G.5).
/// </summary>
public sealed class MacCliLinkTests : IDisposable
{
    private const string _posix = "macOS link targets are POSIX paths.";
    private const string _macOS = "osascript exists on macOS only.";
    private const string _awkward = "/Applications/it's \"key\" $(touch pwned) `touch pwned` \\ é.app/Contents/MacOS/keypaste";

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-mac-link-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string LinkPath => Path.Combine(_root, "bin", "keypaste");

    private string BackupPath => LinkPath + ".keypaste-backup";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Every_path_reaches_osascript_as_an_argument_and_none_as_script_text(bool replace, bool administrator)
    {
        const string Link = "/usr/local/odd \"bin\" $(x)/keypaste";
        var directory = Path.GetDirectoryName(Link)!;

        var arguments = MacCliLink.Arguments(_awkward, Link, replace, administrator);

        Assert.Equal(12, arguments.Count);
        Assert.Equal(["-e", "on run argv", "-e"], arguments.Take(3));
        Assert.Equal(["-e", "end run"], arguments.Skip(4).Take(2));
        Assert.Equal(
            [MacCliLink.Program, directory, _awkward, Link, Link + ".keypaste-backup", replace ? "replace" : "link"],
            arguments.Skip(6));

        string[] scripts = [arguments[1], arguments[3], arguments[5]];
        Assert.All(scripts, script =>
        {
            Assert.DoesNotContain(_awkward, script, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, script, StringComparison.Ordinal);
            Assert.DoesNotContain("touch", script, StringComparison.Ordinal);
        });

        Assert.StartsWith("do shell script \"/bin/sh -c \" & quoted form of (item 1 of argv)", arguments[3], StringComparison.Ordinal);
        Assert.Contains("quoted form of (item 6 of argv)", arguments[3], StringComparison.Ordinal);
        Assert.Equal(administrator, arguments[3].EndsWith(" with administrator privileges", StringComparison.Ordinal));
    }

    [Fact]
    public void A_translocated_copy_is_refused_without_asking()
    {
        var runner = new Runner(Succeeds);
        var link = For("/private/var/folders/x/AppTranslocation/0A1B/d/keypaste.app/Contents/MacOS/keypaste", runner);

        Assert.True(MacCliLink.IsTranslocated("/private/var/folders/x/AppTranslocation/0A1B/d/keypaste.app/Contents/MacOS/keypaste"));
        Assert.False(MacCliLink.IsTranslocated("/Applications/keypaste.app/Contents/MacOS/keypaste"));
        Assert.True(link.Read().Translocated);
        Assert.Equal(Refused(CliLinkRefusal.Translocated), link.Link());

        Foreign();
        Assert.Equal(Refused(CliLinkRefusal.Translocated), link.Replace());
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void A_foreign_file_is_never_linked_over_and_a_backup_never_replaced_without_asking()
    {
        var runner = new Runner(Succeeds);
        var link = For(_awkward, runner);
        Foreign();

        Assert.Equal(Refused(CliLinkRefusal.NotKeypaste), link.Link());

        File.WriteAllText(BackupPath, "the person's original");
        Assert.Equal(Refused(CliLinkRefusal.BackupExists), link.Replace());
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData("0:412: execution error: User canceled. (-128)", CliLinkOutcome.Cancelled, null)]
    [InlineData("0:412: execution error: The command exited with a non-zero status. (3)", CliLinkOutcome.Refused, CliLinkRefusal.BackupExists)]
    [InlineData("0:412: execution error: The command exited with a non-zero status. (4)", CliLinkOutcome.Refused, CliLinkRefusal.NotKeypaste)]
    [InlineData("0:412: execution error: mkdir: /usr/local/bin: Permission denied (1)\n", CliLinkOutcome.Failed, null)]
    public void What_the_dialog_and_the_program_answered_is_reported(string standardError, CliLinkOutcome outcome, CliLinkRefusal? refusal)
    {
        var runner = new Runner(() => new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, standardError));
        var result = For(_awkward, runner).Link();

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(refusal, result.Refusal);
        Assert.Equal("/usr/bin/osascript", runner.FileName);
        Assert.Equal(1, runner.Calls);

        if (outcome == CliLinkOutcome.Failed)
        {
            Assert.Equal("mkdir: /usr/local/bin: Permission denied", result.Reason);
        }
    }

    [Fact]
    public void A_dialog_that_made_no_link_or_could_not_start_failed()
    {
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Failed, Reason: "the link was not made"), For(_awkward, new Runner(Succeeds)).Link());
        Assert.Equal(CliLinkOutcome.Failed, For(_awkward, new Runner(() => new ProcessResult(ToolFound: false, -1, string.Empty, string.Empty))).Link().Outcome);
        Assert.Equal(
            new CliLinkResult(CliLinkOutcome.Failed, Reason: "timed out"),
            For(_awkward, new Runner(() => new ProcessResult(ToolFound: true, -1, string.Empty, "timed out"))).Link());
    }

    [Fact]
    public void A_dialog_that_linked_or_replaced_says_so()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _posix);

        var linking = For(_awkward, new Runner(() => Linked(_awkward)));
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), linking.Link());
        Assert.Equal(CliLinkState.ThisCopy, linking.Read().State);
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), linking.Link());

        File.Delete(LinkPath);
        Foreign();
        var replacing = For(_awkward, new Runner(() =>
        {
            File.Move(LinkPath, BackupPath);
            return Linked(_awkward);
        }));
        Assert.Equal(new CliLinkResult(CliLinkOutcome.Replaced), replacing.Replace());
    }

    [Fact]
    public void Reading_names_this_copy_another_copy_and_anything_else()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), _posix);

        var link = For(_awkward, new Runner(Succeeds));
        Assert.Equal(new CliLinkStatus(CliLinkState.Absent, LinkPath, null, BackupPath, BackupExists: false, DirectoryOnPath: true, Translocated: false), link.Read());

        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);
        File.CreateSymbolicLink(LinkPath, _awkward);
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);

        Relink("/Applications/Old keypaste.app/Contents/MacOS/keypaste");
        Assert.Equal((CliLinkState.OtherCopy, "/Applications/Old keypaste.app/Contents/MacOS/keypaste"), StateOf(link));

        Relink("/opt/homebrew/bin/keypaste");
        Assert.Equal((CliLinkState.Foreign, "/opt/homebrew/bin/keypaste"), StateOf(link));

        File.Delete(LinkPath);
        Foreign();
        Assert.Equal((CliLinkState.Foreign, null), StateOf(link));

        File.Delete(LinkPath);
        Directory.CreateDirectory(LinkPath);
        Assert.Equal((CliLinkState.Foreign, null), StateOf(link));

        File.CreateSymbolicLink(BackupPath, Path.Combine(_root, "nowhere"));
        Assert.True(link.Read().BackupExists);
    }

    [Fact]
    public void The_bundled_keypaste_is_found_only_in_a_bundles_Contents_MacOS()
    {
        var macOS = Path.Combine(_root, "keypaste.app", "Contents", "MacOS");
        var plain = Path.Combine(_root, "bin");
        Directory.CreateDirectory(macOS);
        Directory.CreateDirectory(plain);

        Assert.Null(CliLinks.BundledProgram(macOS));

        File.WriteAllText(Path.Combine(macOS, "keypaste"), string.Empty);
        File.WriteAllText(Path.Combine(plain, "keypaste"), string.Empty);

        Assert.Equal(Path.Combine(macOS, "keypaste"), CliLinks.BundledProgram(macOS));
        Assert.Equal(Path.Combine(macOS, "keypaste"), CliLinks.BundledProgram(macOS + Path.DirectorySeparatorChar));
        Assert.Null(CliLinks.BundledProgram(plain));
    }

    [Fact]
    public void The_real_osascript_links_an_awkward_path_exactly_and_evaluates_none_of_it()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var (target, linkPath) = AwkwardBundle();
        var link = new MacCliLink(target, linkPath, Real(), administrator: false);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Linked), link.Link());
        Assert.Equal(target, new FileInfo(linkPath).LinkTarget);
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);
        AssertNothingEvaluated();
    }

    [Fact]
    public void The_real_osascript_replaces_a_foreign_file_and_keeps_it_byte_for_byte()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var (target, linkPath) = AwkwardBundle();
        byte[] original = [0xCA, 0xFE, 0xBA, 0xBE, 0, 1, 2];
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        File.WriteAllBytes(linkPath, original);
        var link = new MacCliLink(target, linkPath, Real(), administrator: false);

        Assert.Equal(new CliLinkResult(CliLinkOutcome.Replaced), link.Replace());
        Assert.Equal(original, File.ReadAllBytes(link.BackupPath));
        Assert.Equal(target, new FileInfo(linkPath).LinkTarget);
        AssertNothingEvaluated();
    }

    [Fact]
    public void The_real_program_keeps_a_backup_that_appeared_after_keypaste_looked()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var (target, linkPath) = AwkwardBundle();
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        File.WriteAllText(linkPath, "foreign");
        var backupPath = linkPath + ".keypaste-backup";
        var link = new MacCliLink(target, linkPath, Real(() => File.WriteAllText(backupPath, "the person's original")), administrator: false);

        Assert.Equal(Refused(CliLinkRefusal.BackupExists), link.Replace());
        Assert.Equal("foreign", File.ReadAllText(linkPath));
        Assert.Equal("the person's original", File.ReadAllText(backupPath));
        AssertNothingEvaluated();
    }

    [Fact]
    public void The_real_program_leaves_a_file_that_appeared_after_keypaste_looked()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var (target, linkPath) = AwkwardBundle();
        var link = new MacCliLink(
            target,
            linkPath,
            Real(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
                File.WriteAllText(linkPath, "foreign");
            }),
            administrator: false);

        Assert.Equal(Refused(CliLinkRefusal.NotKeypaste), link.Link());
        Assert.Equal("foreign", File.ReadAllText(linkPath));
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        AssertNothingEvaluated();
    }

    private static ProcessResult Succeeds() => new(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);

    private static CliLinkResult Refused(CliLinkRefusal refusal) => new(CliLinkOutcome.Refused, refusal);

    private static (CliLinkState, string?) StateOf(MacCliLink link)
    {
        var status = link.Read();
        return (status.State, status.LinkedTo);
    }

    private static Runner Real(Action? before = null) => new((fileName, arguments) =>
    {
        before?.Invoke();
        return new SystemProcessRunner().Run(fileName, arguments, stdin: null, Encoding.UTF8, TimeSpan.FromMinutes(1));
    });

    private MacCliLink For(string target, Runner runner) => new(target, LinkPath, runner, administrator: true);

    private void Foreign()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);
        File.WriteAllText(LinkPath, "#!/bin/sh\necho another keypaste\n");
    }

    private void Relink(string target)
    {
        File.Delete(LinkPath);
        File.CreateSymbolicLink(LinkPath, target);
    }

    private ProcessResult Linked(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath)!);
        File.CreateSymbolicLink(LinkPath, target);
        return Succeeds();
    }

    // Every awkward character, and a command substitution that would create a file in this directory were any shell to evaluate the path.
    private (string Target, string LinkPath) AwkwardBundle()
    {
        var awkward = Path.Combine(_root, $"it's \"key\" \\ é $(touch {_root}/pwned) `touch {_root}/pwned-too`");
        var macOS = Path.Combine(awkward, "keypaste.app", "Contents", "MacOS");
        Directory.CreateDirectory(macOS);

        var target = Path.Combine(macOS, "keypaste");
        File.WriteAllText(target, "#!/bin/sh\n");

        return (target, Path.Combine(awkward, "usr local bin", "keypaste"));
    }

    private void AssertNothingEvaluated()
    {
        Assert.False(File.Exists(Path.Combine(_root, "pwned")));
        Assert.False(File.Exists(Path.Combine(_root, "pwned-too")));
    }

    private sealed class Runner(Func<string, IReadOnlyList<string>, ProcessResult> run) : IProcessRunner
    {
        internal Runner(Func<ProcessResult> answer)
            : this((_, _) => answer())
        {
        }

        internal int Calls { get; private set; }

        internal string? FileName { get; private set; }

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            Calls++;
            FileName = fileName;
            return run(fileName, arguments);
        }
    }
}
