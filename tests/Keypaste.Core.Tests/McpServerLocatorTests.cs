using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A client is registered at a path that outlives this run: the installed <c>keypaste</c>, the bundle a macOS link names,
/// or inside an AppImage G.5's link, made first when it must be; and it is pinned to the vault, log and owner a bridge
/// started without keypaste's variables would miss (D-0334, D-0389).
/// </summary>
public sealed class McpServerLocatorTests : IDisposable
{
    private const string _image = "/home/person/Applications/keypaste.AppImage";

    private const string _linkPath = "/home/person/.local/bin/keypaste";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-locator-").FullName;

    public static TheoryData<CliLinkState, bool> States => new()
    {
        { CliLinkState.Absent, false },
        { CliLinkState.ThisCopy, false },
        { CliLinkState.OtherCopy, false },
        { CliLinkState.OtherCopy, true },
        { CliLinkState.Foreign, false },
    };

    public static TheoryData<CliLinkResult, string> LinkFailures => new()
    {
        { new CliLinkResult(CliLinkOutcome.Failed, Reason: "Permission denied"), "Permission denied" },
        { new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.BackupExists), _linkPath + ".keypaste-backup already exists, and keypaste never overwrites it" },
        { new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.NotKeypaste), "it changed before keypaste linked it" },
        { new CliLinkResult(CliLinkOutcome.Refused, CliLinkRefusal.UnsafeProgram), "this copy's path holds a line break" },
        { new CliLinkResult(CliLinkOutcome.Cancelled), "the system's dialog was dismissed" },
        { new CliLinkResult(CliLinkOutcome.Linked), "it does not start this copy afterwards" },
    };

    private string Home => Path.Combine(_directory, "home");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_bridge_is_found_beside_the_program_before_path()
    {
        var beside = Directory.CreateDirectory(Path.Combine(_directory, "beside")).FullName;
        var onPath = Directory.CreateDirectory(Path.Combine(_directory, "path")).FullName;
        File.WriteAllText(Path.Combine(onPath, McpServerLocator.ExecutableName), "");

        Assert.Equal(Path.Combine(onPath, McpServerLocator.ExecutableName), McpServerLocator.Find(beside, onPath)!.Path);

        File.WriteAllText(Path.Combine(beside, McpServerLocator.ExecutableName), "");
        Assert.Equal(Path.Combine(beside, McpServerLocator.ExecutableName), McpServerLocator.Find(beside, onPath)!.Path);
        Assert.Equal(["mcp"], McpServerLocator.Find(beside, onPath)!.Arguments);
        Assert.Null(McpServerLocator.Find(Path.Combine(_directory, "nowhere"), null));
    }

    [Fact]
    public void The_desktop_registers_the_keypaste_beside_it_and_says_where_it_looked_when_there_is_none()
    {
        var beside = Directory.CreateDirectory(Path.Combine(_directory, "app")).FullName;

        Assert.False(McpServerLocator.TryForDesktop(beside, appImage: null, pathVariable: null, McpClientPlatform.Linux, out var target, out var error));
        Assert.Null(target);
        Assert.Equal($"keypaste was not found in {beside} and on PATH, so there is nothing an AI tool could start", error);

        File.WriteAllText(Path.Combine(beside, McpServerLocator.ExecutableName), "");

        Assert.True(McpServerLocator.TryForDesktop(beside, appImage: null, pathVariable: null, McpClientPlatform.Linux, out target, out error), error);
        Assert.Equal(Path.Combine(beside, McpServerLocator.ExecutableName), target.Server.Path);
        Assert.Null(target.Link);
        Assert.Null(target.Note);
    }

    [Fact]
    public void Setup_registers_the_keypaste_running_it_and_nothing_else()
    {
        var self = Path.Combine(_directory, McpServerLocator.ExecutableName);

        Assert.True(McpServerLocator.TryForSetup(self, appImage: null, McpClientPlatform.Linux, out var target, out var error), error);
        Assert.Equal(self, target.Server.Path);
        Assert.Equal(["mcp"], target.Server.Arguments);

        Assert.False(McpServerLocator.TryForSetup(Path.Combine(_directory, "dotnet"), appImage: null, out _, out error));
        Assert.EndsWith("run keypaste itself rather than through dotnet", error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Inside_an_appimage_a_client_is_registered_as_the_link_in_every_state(CliLinkState state, bool stale)
    {
        var appImage = new McpAppImage(_image, new ScriptedLink(state, "/elsewhere/keypaste.AppImage") { Stale = stale });

        Assert.True(McpServerLocator.TryForSetup("/tmp/.mount_x/usr/bin/keypaste", appImage, out var setup, out var error), error);
        Assert.True(McpServerLocator.TryForDesktop("/tmp/.mount_x/usr/bin", appImage, pathVariable: null, out var desktop, out error), error);

        Assert.Equal(_linkPath, setup.Server.Path);
        Assert.Equal(["mcp"], setup.Server.Arguments);
        Assert.Equal(setup.Server.Path, desktop.Server.Path);
    }

    [Fact]
    public void An_absent_link_is_created_first_and_one_for_this_copy_is_left_alone()
    {
        var absent = Target(new ScriptedLink(CliLinkState.Absent));

        Assert.Equal(McpLinkChange.Create, absent.Link!.Change);
        Assert.Equal($"create {_linkPath}, a script that starts {_image} cli", absent.Link.Display);

        var mine = Target(new ScriptedLink(CliLinkState.ThisCopy));
        Assert.Null(mine.Link);
        Assert.Null(mine.Note);
    }

    [Fact]
    public void A_link_to_a_newer_copy_is_left_to_it_and_said_so()
    {
        var target = Target(new ScriptedLink(CliLinkState.OtherCopy, "/opt/newer/keypaste.AppImage"));

        Assert.Null(target.Link);
        Assert.Equal($"{_linkPath} runs keypaste from /opt/newer/keypaste.AppImage", target.Note);
    }

    [Fact]
    public void A_link_to_a_missing_or_older_copy_is_re_pointed_as_this_copys_start_would()
    {
        var older = Path.Combine(_directory, "older.AppImage");
        File.WriteAllText(older, "");

        var missing = Target(new ScriptedLink(CliLinkState.OtherCopy, "/gone/keypaste.AppImage") { Stale = true });
        var present = Target(new ScriptedLink(CliLinkState.OtherCopy, older) { Stale = true });

        Assert.Equal(McpLinkChange.Repoint, missing.Link!.Change);
        Assert.Equal($"re-point {_linkPath} from the missing /gone/keypaste.AppImage to {_image}", missing.Link.Display);
        Assert.Equal($"re-point {_linkPath} from {older} to {_image}", present.Link!.Display);
        Assert.Null(missing.Note);
    }

    [Fact]
    public void A_file_keypaste_did_not_write_is_replaced_only_as_a_shown_step_that_names_it_and_its_backup()
    {
        var program = Target(new ScriptedLink(CliLinkState.Foreign));
        var link = Target(new ScriptedLink(CliLinkState.Foreign, "/usr/lib/other/keypaste"));

        Assert.Equal(McpLinkChange.Replace, program.Link!.Change);
        Assert.Equal(
            $"replace {_linkPath}, which is another program, with a script that starts {_image} cli; the old file is kept as {_linkPath}.keypaste-backup",
            program.Link.Display);
        Assert.Equal(
            $"replace {_linkPath}, which is a link to /usr/lib/other/keypaste, with a script that starts {_image} cli; the old file is kept as {_linkPath}.keypaste-backup",
            link.Link!.Display);
    }

    [Fact]
    public void A_file_keypaste_did_not_write_is_not_replaced_while_a_backup_is_already_there()
    {
        var appImage = new McpAppImage(_image, new ScriptedLink(CliLinkState.Foreign) { BackupExists = true });
        var expected = $"{_linkPath} is another program, and {_linkPath}.keypaste-backup already exists, which keypaste never overwrites. "
            + $"Move or delete it to replace {_linkPath}. Nothing was changed.";

        Assert.False(McpServerLocator.TryForSetup(null, appImage, out var setup, out var error));
        Assert.Null(setup);
        Assert.Equal(expected, error);

        Assert.False(McpServerLocator.TryForDesktop("/tmp/.mount_x/usr/bin", appImage, pathVariable: null, out var desktop, out error));
        Assert.Null(desktop);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void A_link_step_runs_before_the_clients_commands_and_a_replacement_is_a_replace()
    {
        List<string> log = [];
        var runner = new RecordingRunner(log);

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(Plan(Target(new ScriptedLink(CliLinkState.Absent) { Log = log }).Link!), runner).Status);
        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(Plan(Target(new ScriptedLink(CliLinkState.Foreign) { Log = log }).Link!), runner).Status);

        Assert.Equal(
            ["link", "/opt/codex mcp remove keypaste", $"/opt/codex mcp add keypaste -- {_linkPath} mcp --client-label codex --allow-run --expose **",
             "replace", "/opt/codex mcp remove keypaste", $"/opt/codex mcp add keypaste -- {_linkPath} mcp --client-label codex --allow-run --expose **"],
            log);
    }

    [Theory]
    [MemberData(nameof(LinkFailures))]
    public void A_link_that_is_not_made_registers_nothing(CliLinkResult answer, string reason)
    {
        List<string> log = [];

        var result = McpClientSetup.Apply(Plan(Target(new ScriptedLink(CliLinkState.Absent) { Answer = answer, Log = log }).Link!), new RecordingRunner(log));

        Assert.Equal(new McpSetupResult(McpSetupStatus.LinkFailed, reason, Backup: null), result);
        Assert.Equal(["link"], log);
    }

    [Fact]
    public void A_real_link_is_created_as_shown_and_starts_this_image()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the Linux link is a sh script");

        var image = Path.Combine(_directory, "Apps", "keypaste.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllText(image, "");
        Assert.True(SemanticVersion.TryParse("0.5.0", out var version));
        var link = new LinuxCliLink(Home, new CliTarget(image, ThroughAppImage: true), version, pathVariable: null);

        Assert.True(McpServerLocator.TryForSetup(null, new McpAppImage(image, link), out var target, out var error), error);
        Assert.Equal(Path.Combine(Home, ".local", "bin", "keypaste"), target.Server.Path);
        Assert.Equal(McpLinkChange.Create, target.Link!.Change);

        Assert.Equal(McpSetupStatus.Done, McpClientSetup.Apply(target.Link).Status);
        Assert.Equal(CliLinkState.ThisCopy, link.Read().State);
        Assert.Contains($"exec '{image}' cli \"$@\"", File.ReadAllText(target.Server.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void On_macos_a_keypaste_reached_through_a_link_is_registered_as_the_bundle_it_names()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "the macOS link names the bundle's keypaste");

        var bundled = Path.Combine(_directory, "keypaste.app", "Contents", "MacOS", "keypaste");
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        File.WriteAllText(bundled, "");
        var linked = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory, "bin")).FullName, "keypaste");
        File.CreateSymbolicLink(linked, bundled);

        Assert.True(McpServerLocator.TryForSetup(linked, appImage: null, out var target, out var error), error);
        Assert.Equal(PathIdentity.Canonical(bundled), target.Server.Path);
    }

    [Theory]
    [InlineData("/private/var/folders/x/T/AppTranslocation/0A1B/d/keypaste.app/Contents/MacOS/keypaste")]
    [InlineData("/Volumes/keypaste/keypaste.app/Contents/MacOS/keypaste")]
    public void On_macos_a_copy_that_is_gone_once_it_quits_is_refused(string path)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "these are macOS paths");

        Assert.False(McpServerLocator.TryForSetup(path, appImage: null, McpClientPlatform.MacOS, out var target, out var error));
        Assert.Null(target);
        Assert.Equal("move keypaste to Applications, then connect", error);
        Assert.True(McpServerLocator.TryForSetup(path, appImage: null, McpClientPlatform.Linux, out _, out error), error);
    }

    [Fact]
    public void On_macos_the_desktop_running_translocated_is_refused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "these are macOS paths");

        var macOS = Directory.CreateDirectory(Path.Combine(_directory, "AppTranslocation", "0A1B", "d", "keypaste.app", "Contents", "MacOS")).FullName;
        File.WriteAllText(Path.Combine(macOS, McpServerLocator.ExecutableName), "");

        Assert.False(McpServerLocator.TryForDesktop(macOS, appImage: null, pathVariable: null, McpClientPlatform.MacOS, out _, out var error));
        Assert.Equal("move keypaste to Applications, then connect", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{home}/.keypaste")]
    [InlineData("{home}/.keypaste/")]
    public void Under_the_default_home_only_a_named_vault_other_than_the_chosen_one_is_pinned(string? home)
    {
        var keypasteHome = home?.Replace("{home}", Home, StringComparison.Ordinal);
        var named = Path.Combine(_directory, "named.kdbx");
        var chosen = Path.Combine(_directory, "chosen.kdbx");

        Assert.True(McpServerLocator.TryPin(Machine(), keypasteHome, null, named, chosen, out var pins));
        Assert.Equal(new McpServerPins(named, AuditLogPath: null, ApproverName: null), pins);

        Assert.True(McpServerLocator.TryPin(Machine(), keypasteHome, null, chosen, chosen, out pins));
        Assert.Equal(McpServerPins.None, pins);

        Assert.True(McpServerLocator.TryPin(Machine(), keypasteHome, string.Empty, namedVault: null, chosenVault: null, out pins));
        Assert.Equal(McpServerPins.None, pins);
    }

    [Fact]
    public void Under_the_default_home_a_set_approver_alone_is_pinned()
    {
        var chosen = Path.Combine(_directory, "chosen.kdbx");

        Assert.True(McpServerLocator.TryPin(Machine(), Path.Combine(Home, ".keypaste"), "keypaste-test-pipe", namedVault: null, chosen, out var pins));
        Assert.Equal(new McpServerPins(VaultPath: null, AuditLogPath: null, "keypaste-test-pipe"), pins);
    }

    [Fact]
    public void Under_another_home_the_vault_the_log_the_ceilings_and_that_homes_owner_are_pinned()
    {
        var home = Path.Combine(_directory, "custom");
        var named = Path.Combine(_directory, "named.kdbx");
        var chosen = Path.Combine(_directory, "chosen.kdbx");

        Assert.True(McpServerLocator.TryPin(Machine(), home, null, namedVault: null, chosen, out var pins));
        Assert.Equal(new McpServerPins(chosen, Path.Combine(home, "audit.jsonl"), Owner(home, chosen), Path.Combine(home, "clients.toml")), pins);

        Assert.True(McpServerLocator.TryPin(Machine(), home, null, named, chosen, out pins));
        Assert.Equal(new McpServerPins(named, Path.Combine(home, "audit.jsonl"), Owner(home, named), Path.Combine(home, "clients.toml")), pins);

        Assert.True(McpServerLocator.TryPin(Machine(), home, "keypaste-test-pipe", named, chosen, out pins));
        Assert.Equal("keypaste-test-pipe", pins.ApproverName);
    }

    [Fact]
    public void A_machine_with_no_home_makes_any_set_home_another_one()
    {
        var home = Path.Combine(_directory, ".keypaste");
        var chosen = Path.Combine(_directory, "chosen.kdbx");
        var homeless = McpClientMachine.From(_ => null, McpClientMachine.CurrentPlatform);

        Assert.True(McpServerLocator.TryPin(homeless, home, null, namedVault: null, chosen, out var pins));
        Assert.Equal(Path.Combine(home, "audit.jsonl"), pins.AuditLogPath);
    }

    [Fact]
    public void Another_home_with_no_vault_named_or_chosen_cannot_be_pinned()
    {
        Assert.False(McpServerLocator.TryPin(Machine(), Path.Combine(_directory, "custom"), "keypaste-test-pipe", namedVault: null, chosenVault: null, out var pins));
        Assert.Null(pins);
    }

    private static string Owner(string home, string vault) =>
        ApproverEndpoint.Resolve(null, null, VaultIdentity.Of(KeypasteHome.Resolve(home), vault))!;

    private static McpBridgeTarget Target(ICliLink link)
    {
        Assert.True(McpServerLocator.TryForSetup(null, new McpAppImage(_image, link), out var target, out var error), error);
        return target;
    }

    private static McpSetupPlan Plan(McpBridgeLink link)
    {
        var codex = McpClientCatalog.Find("codex")!;
        var detection = new McpClientDetection(McpClientMachine.CurrentPlatform, Found: true, new McpClientProgram("/opt/codex", []), ConfigPath: null, Problem: null);
        Assert.True(McpServerRegistration.TryCreate(new McpServerCommand(_linkPath, ["mcp"]), McpServerPins.None, "codex", [], out var registration, out var error), error);
        Assert.True(McpClientSetup.TryConnect(codex, detection, registration, link, out var plan, out error), error);
        Assert.Same(link, plan.Steps[0]);
        return plan;
    }

    private McpClientMachine Machine()
    {
        string? Variable(string name) => name is "HOME" or "USERPROFILE" ? Home : null;

        return McpClientMachine.From(Variable, McpClientMachine.CurrentPlatform);
    }

    /// <summary>A link that answers as the test decided, so nothing is linked on the machine running the tests.</summary>
    private sealed class ScriptedLink(CliLinkState state, string? linkedTo = null) : ICliLink
    {
        private CliLinkState _state = state;

        public string LinkPath => _linkPath;

        public string BackupPath => _linkPath + ".keypaste-backup";

        internal bool Stale { get; init; }

        internal bool BackupExists { get; init; }

        internal CliLinkResult? Answer { get; init; }

        internal List<string> Log { get; init; } = [];

        public CliLinkStatus Read() =>
            new(_state, LinkPath, _state == CliLinkState.ThisCopy ? null : linkedTo, BackupPath, BackupExists, DirectoryOnPath: true, Translocated: false)
            {
                Stale = Stale,
            };

        public CliLinkResult Link()
        {
            Log.Add("link");
            return Change(CliLinkOutcome.Linked);
        }

        public CliLinkResult Replace()
        {
            Log.Add("replace");
            return Change(CliLinkOutcome.Replaced);
        }

        public bool KeepCurrent() => false;

        private CliLinkResult Change(CliLinkOutcome outcome)
        {
            if (Answer is { } answer)
            {
                return answer;
            }

            _state = CliLinkState.ThisCopy;
            return new CliLinkResult(outcome);
        }
    }

    private sealed class RecordingRunner(List<string> log) : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            log.Add(fileName + " " + string.Join(' ', arguments));
            return new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }
}
