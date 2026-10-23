using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Health;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Core.Processes;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What <c>keypaste doctor</c>'s checks say: which registrations upgrades keep on each platform, which copies differ in
/// version, which Argon2 derives vault keys, how each settings file is fixed and which <c>keypaste</c> a terminal runs;
/// nothing but a registered <c>keypaste</c> started as <c>mcp</c> is ever started (D-0467, D-0525).
/// </summary>
/// <remarks>Every registration is Cursor's, written under a temporary home, and every command is answered by a fake runner.</remarks>
public sealed class HealthCheckTests : IDisposable
{
    private const string _here = "0.5.1";

    private const string _setup = "run `keypaste setup --client cursor`";

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-health-").FullName;

    private string Home => Path.Combine(_root, "home");

    private string KeypasteHomeDirectory => Path.Combine(_root, "keypaste-home");

    private string This => Path.Combine(_root, "this", McpServerLocator.ExecutableName);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(McpClientPlatform.MacOS, "AppTranslocation/0A1B2C/d/keypaste", "starts keypaste from a disk image, gone once it is ejected", "move keypaste to Applications, then run `keypaste setup --client cursor`")]
    [InlineData(McpClientPlatform.Linux, ".mount_keypaA1b2C3/usr/bin/keypaste", "starts keypaste inside an AppImage, which an upgrade replaces", "run `keypaste setup --client cursor`, which registers ~/.local/bin/keypaste")]
    [InlineData(McpClientPlatform.Linux, "keypaste-0.5.1-x86_64.AppImage", "starts keypaste inside an AppImage, which an upgrade replaces", "run `keypaste setup --client cursor`, which registers ~/.local/bin/keypaste")]
    public void A_registration_an_eject_or_an_upgrade_removes_is_a_fix_and_is_never_started(McpClientPlatform platform, string program, string says, string fix)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "macOS and Linux paths start with a slash");

        var command = PlantAt(Path.Combine(_root, program));
        var scope = Scope(platform);
        Register(scope.Machine, command, "mcp");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, says) { Where = command, Fix = fix }, cursor.Finding);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(McpClientPlatform.Linux, "AppTranslocation/0A1B2C/d/keypaste")]
    [InlineData(McpClientPlatform.MacOS, ".mount_keypaA1b2C3/usr/bin/keypaste")]
    public void A_place_only_another_platform_loses_is_one_upgrades_keep(McpClientPlatform platform, string program)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "macOS and Linux paths start with a slash");

        var command = PlantAt(Path.Combine(_root, program));
        var scope = Scope(platform);
        Register(scope.Machine, command, "mcp", "--client-label", "cursor");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Ok, $"starts keypaste {_here} from a place upgrades keep") { Where = command }, cursor.Finding);
        Assert.Equal(_here, cursor.Version);
        Assert.Equal([$"{command} --version", $"{command} mcp --help --client-label cursor"], runner.Calls);
    }

    [Fact]
    public void A_linux_link_naming_an_appimage_that_is_gone_is_a_fix_and_is_never_started()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the link is Linux's");

        var gone = Path.Combine(_root, "Downloads", "keypaste-0.5.0-x86_64.AppImage");
        Assert.True(SemanticVersion.TryParse("0.5.0", out var version));
        var link = Path.Combine(Home, ".local", "bin", "keypaste");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.WriteAllText(link, CliLinkScript.Document(new CliTarget(gone, ThroughAppImage: true), version)!);
        var scope = Scope(McpClientPlatform.Linux);
        Register(scope.Machine, link, "mcp");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(
            new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, $"{link} names {gone}, which is gone")
            {
                Where = link,
                Fix = "start the keypaste app you kept; it re-points ~/.local/bin/keypaste",
            },
            cursor.Finding);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("keypaste-mcp", "mcp", "starts keypaste-mcp, which keypaste no longer ships")]
    [InlineData("keypaste", "", "starts keypaste, but not `keypaste mcp`")]
    [InlineData("keypaste", "serve", "starts keypaste, but not `keypaste mcp`")]
    [InlineData("keypaste", "mcp serve", "starts keypaste, but not `keypaste mcp`")]
    [InlineData("keypaste", "mcp setup --client cursor", "starts keypaste, but not `keypaste mcp`")]
    [InlineData("keypaste", "mcp policy", "starts keypaste, but not `keypaste mcp`")]
    public void Only_keypaste_started_as_its_mcp_bridge_is_ever_started(string program, string arguments, string says)
    {
        var command = PlantAt(Path.Combine(_root, "bin", Executable(program)));
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, command, arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, says) { Where = command, Fix = _setup }, cursor.Finding);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void A_registration_starting_another_program_is_a_fix_and_is_never_started()
    {
        var command = PlantAt(Path.Combine(_root, "bin", Executable("node")));
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, command, "mcp");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(
            new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, $"starts {Path.GetFileName(command)}, which is not keypaste") { Where = command, Fix = _setup },
            cursor.Finding);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void A_registration_whose_launch_keypaste_cannot_read_is_a_note_and_starts_nothing()
    {
        var scope = Scope(McpClientMachine.CurrentPlatform);
        var path = McpConfigLocation.Cursor.PathOn(scope.Machine)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"mcpServers": {"keypaste": {"command": 7}}}""");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(
            new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Note, "keypaste is registered, but keypaste cannot read what it starts") { Where = path, Fix = _setup },
            cursor.Finding);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Help_follows_mcp_at_once_and_a_registration_that_refuses_its_arguments_is_a_fix()
    {
        var command = PlantAt(Path.Combine(_root, "bin", McpServerLocator.ExecutableName));
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, command, "mcp", "--client-label", "cursor", "--frobnicate");
        var runner = new Runner();
        runner.Answers[$"{command} mcp --help --client-label cursor --frobnicate"] =
            new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, "keypaste mcp: unknown option '--frobnicate'\n");

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(
            new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, "refuses its arguments: keypaste mcp: unknown option '--frobnicate'") { Where = command, Fix = _setup },
            cursor.Finding);
        Assert.Equal(_here, cursor.Version);
        Assert.Equal([$"{command} --version", $"{command} mcp --help --client-label cursor --frobnicate"], runner.Calls);
    }

    [Fact]
    public void A_registration_of_this_keypaste_is_asked_only_for_its_help()
    {
        PlantAt(This);
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, This, "mcp");
        var runner = new Runner();

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner));

        Assert.Equal(HealthStatus.Ok, cursor.Finding.Status);
        Assert.Equal(CoreInfo.Version, cursor.Version);
        Assert.Equal([$"{This} mcp --help"], runner.Calls);
    }

    [Fact]
    public void With_no_runner_nothing_is_started_and_no_version_is_known()
    {
        var command = PlantAt(Path.Combine(_root, "bin", McpServerLocator.ExecutableName));
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, command, "mcp");

        var cursor = Assert.Single(ToolReading.ReadAll(scope, runner: null));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Ok, "starts keypaste from a place upgrades keep") { Where = command }, cursor.Finding);
        Assert.Null(cursor.Version);
    }

    [Fact]
    public void A_registration_pinned_to_a_vault_that_is_not_there_is_a_fix()
    {
        var command = PlantAt(Path.Combine(_root, "bin", McpServerLocator.ExecutableName));
        var vault = Path.Combine(_root, "moved.kdbx");
        var scope = Scope(McpClientMachine.CurrentPlatform);
        Register(scope.Machine, command, "mcp", "--vault", vault, "--client-label", "cursor");

        var cursor = Assert.Single(ToolReading.ReadAll(scope, new Runner()));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Fix, "names a vault that is not there") { Where = vault, Fix = _setup }, cursor.Finding);
    }

    [Fact]
    public void A_tool_installed_but_not_connected_is_a_note_and_one_neither_installed_nor_registered_is_left_out()
    {
        var scope = Scope(McpClientMachine.CurrentPlatform);
        var config = McpConfigLocation.Cursor.PathOn(scope.Machine)!;
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{"mcpServers": {}}""");

        var cursor = Assert.Single(ToolReading.ReadAll(scope, new Runner()));

        Assert.Equal(new HealthFinding(HealthArea.Tool, "cursor", HealthStatus.Note, "installed but not connected") { Fix = _setup }, cursor.Finding);
    }

    [Theory]
    [InlineData(OwnerKind.DesktopApp, "0.5.0", "0.5.1", "0.5.1", "the keypaste app holding the vault is 0.5.0, this is 0.5.1", "quit the keypaste app and open it again")]
    [InlineData(OwnerKind.TerminalAgent, "0.5.0", "0.5.1", "0.5.1", "keypaste approve holding the vault is 0.5.0, this is 0.5.1", "stop keypaste approve and start it again")]
    [InlineData(OwnerKind.DesktopApp, null, "0.5.1", "0.5.1", "the keypaste app holding the vault is older than this keypaste", "quit the keypaste app and open it again")]
    [InlineData(OwnerKind.DesktopApp, "zero", "0.5.1", "0.5.1", "the keypaste app holding the vault gives a version keypaste cannot read", "quit the keypaste app and open it again")]
    [InlineData(OwnerKind.DesktopApp, "0.5.2", "0.5.1", "0.5.1", "the keypaste app holding the vault is 0.5.2, this is 0.5.1", "update this keypaste, or run the one the app installed")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "0.5.0", "0.5.1", "Codex starts keypaste 0.5.0, this is 0.5.1", "run `keypaste setup --client codex` from the keypaste you keep")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "0.5.1-rc.1", "0.5.1", "Codex starts keypaste 0.5.1-rc.1, this is 0.5.1", "run `keypaste setup --client codex` from the keypaste you keep")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "keypaste 0.5.1", "0.5.1", "Codex starts a keypaste whose version keypaste cannot read", "run `keypaste setup --client codex` from the keypaste you keep")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "0.6.0", "0.5.1", "Codex starts keypaste 0.6.0, this is 0.5.1", "update this keypaste, or run the one the app installed")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "0.5.1", "0.4.0", "a terminal runs keypaste 0.4.0, this is 0.5.1", "a terminal runs that one: update or remove it")]
    [InlineData(OwnerKind.DesktopApp, "0.5.1", "0.5.1", "0.6.0", "a terminal runs keypaste 0.6.0, this is 0.5.1", "update this keypaste, or run the one the app installed")]
    public void Each_copy_whose_version_differs_is_one_fix(OwnerKind kind, string? owner, string tool, string onPath, string says, string fix)
    {
        var findings = HealthCheck.Versions(_here, Owner(kind, owner), [Registered("codex", tool)], OnPath(onPath));

        var only = Assert.Single(findings);
        Assert.Equal((HealthArea.Versions, HealthStatus.Fix, says, fix), (only.Area, only.Status, only.Says, only.Fix));
    }

    [Fact]
    public void The_library_beside_this_keypaste_that_derived_the_key_is_named_with_its_path()
    {
        var carried = Path.Combine(_root, "argon2.dll");

        Assert.Equal(
            new HealthFinding(HealthArea.Argon2, "argon2", HealthStatus.Ok, "this keypaste derives vault keys with argon2.dll") { Where = carried },
            Argon2Reading.Finding(carried, carried, present: true));
    }

    [Fact]
    public void On_Linux_the_system_s_library_is_named_and_none_is_a_note_saying_what_to_install()
    {
        Assert.Equal(
            new HealthFinding(HealthArea.Argon2, "argon2", HealthStatus.Ok, "this keypaste derives vault keys with the system's libargon2.so.1"),
            Argon2Reading.Finding("libargon2.so.1", carried: null, present: false));
        Assert.Equal(
            new HealthFinding(HealthArea.Argon2, "argon2", HealthStatus.Note, "no libargon2 is installed, so unlocking and saving take longer")
            {
                Fix = "install your distribution's libargon2 package",
            },
            Argon2Reading.Finding(bound: null, carried: null, present: false));
    }

    [Theory]
    [InlineData(false, "libargon2.1.dylib is not beside this keypaste, so unlocking and saving take longer", "keep libargon2.1.dylib beside keypaste")]
    [InlineData(true, "libargon2.1.dylib beside this keypaste did not work, so unlocking and saving take longer", "reinstall keypaste")]
    public void A_carried_library_that_derived_nothing_is_a_fix_at_its_path(bool present, string says, string fix)
    {
        var carried = Path.Combine(_root, "libargon2.1.dylib");

        Assert.Equal(
            new HealthFinding(HealthArea.Argon2, "argon2", HealthStatus.Fix, says) { Where = carried, Fix = fix },
            Argon2Reading.Finding(bound: null, carried, present));
    }

    [Fact]
    public void Every_copy_that_matches_is_named_in_one_line()
    {
        var findings = HealthCheck.Versions(_here, Owner(OwnerKind.DesktopApp, _here), [Registered("codex", _here), Registered("cursor", _here)], OnPath(_here));

        Assert.Equal([new HealthFinding(HealthArea.Versions, "versions", HealthStatus.Ok, "0.5.1 here, in the keypaste app, in each AI tool and on PATH")], findings);
    }

    [Fact]
    public void Only_what_was_compared_is_named()
    {
        var nothing = HealthCheck.Versions(
            _here,
            Owner(OwnerKind.DesktopApp, version: null, OwnerAnswer.NothingListening),
            [Registered("cursor", version: null)],
            OnPath(_here, thisCopy: true));
        var approver = HealthCheck.Versions(_here, Owner(OwnerKind.TerminalAgent, _here, OwnerAnswer.Locked), [], OnPath(version: null));

        Assert.Equal("0.5.1 here", Assert.Single(nothing).Says);
        Assert.Equal("0.5.1 here and in keypaste approve", Assert.Single(approver).Says);
    }

    [Fact]
    public void Each_settings_file_that_does_not_parse_says_how_to_fix_it_and_clients_toml_is_never_to_be_deleted()
    {
        var home = KeypasteHomeDirectory;
        Directory.CreateDirectory(home);
        File.WriteAllText(KeypasteHome.SettingsPath(home), "idle_timeout_seconds = 900\n");
        File.WriteAllText(KeypasteHome.ClientsPath(home), "this is not toml\n");
        File.WriteAllText(KeypasteHome.RecentPath(home), "[[vault]]\nthis is not a pair at all\n");
        File.WriteAllText(KeypasteHome.ProjectsPath(home), "{");
        File.WriteAllText(KeypasteHome.RecommendationsPath(home), "[]");
        File.WriteAllText(KeypasteHome.PolicyPath(home), "[[agent]]\n");

        var findings = SettingsReading.Findings(SettingsReading.ReadAll(home));

        Assert.Equal(["app.toml", "clients.toml", "recent.toml", "projects.json", "recommendations.json", "policy.toml"], findings.Select(finding => finding.Subject));
        Assert.All(findings, finding => Assert.Equal(HealthArea.Settings, finding.Area));

        var app = Of("app.toml");
        Assert.Equal(HealthStatus.Fix, app.Status);
        Assert.StartsWith("line 1: ", app.Says, StringComparison.Ordinal);
        Assert.Equal(KeypasteHome.SettingsPath(home), app.Where);
        Assert.Equal("fix line 1, or delete it and run `keypaste use <path>`; deleting also resets the app's preferences", app.Fix);

        var clients = Of("clients.toml");
        Assert.Equal(HealthStatus.Fix, clients.Status);
        Assert.StartsWith("line 1: ", clients.Says, StringComparison.Ordinal);
        Assert.EndsWith("; agents are refused until it is fixed", clients.Says, StringComparison.Ordinal);
        Assert.Equal($"fix line 1 of {KeypasteHome.ClientsPath(home)}, and keep the file: it holds this computer's limits on every agent", clients.Fix);
        Assert.DoesNotContain("delete", clients.Fix, StringComparison.OrdinalIgnoreCase);

        var recent = Of("recent.toml");
        Assert.StartsWith("line 2: ", recent.Says, StringComparison.Ordinal);
        Assert.Equal("delete it; the app forgets only its list of recent vaults", recent.Fix);

        Assert.Equal(
            new HealthFinding(HealthArea.Settings, "projects.json", HealthStatus.Fix, "could not be read")
            {
                Where = KeypasteHome.ProjectsPath(home),
                Fix = "fix or delete it; deleting forgets which folder each project runs in",
            },
            Of("projects.json"));
        Assert.Equal(
            new HealthFinding(HealthArea.Settings, "recommendations.json", HealthStatus.Fix, "could not be read")
            {
                Where = KeypasteHome.RecommendationsPath(home),
                Fix = "delete it; recommendations you dismissed show again",
            },
            Of("recommendations.json"));
        Assert.Equal(
            new HealthFinding(HealthArea.Settings, "policy.toml", HealthStatus.Note, "keypaste no longer reads it; agents' settings are kept in the vault") { Where = KeypasteHome.PolicyPath(home) },
            Of("policy.toml"));

        HealthFinding Of(string subject) => findings.Single(finding => finding.Subject == subject);
    }

    [Fact]
    public void Settings_files_that_parse_are_named_in_one_line_and_none_at_all_is_said_so()
    {
        var home = KeypasteHomeDirectory;

        Assert.Equal(
            [new HealthFinding(HealthArea.Settings, "settings", HealthStatus.Ok, "no settings files yet")],
            SettingsReading.Findings(SettingsReading.ReadAll(home)));

        Assert.True(AppSettings.Save(KeypasteHome.SettingsPath(home), AppSettings.Default));
        File.WriteAllText(KeypasteHome.ClientsPath(home), string.Empty);

        Assert.Equal(
            [new HealthFinding(HealthArea.Settings, "settings", HealthStatus.Ok, "app.toml and clients.toml parse")],
            SettingsReading.Findings(SettingsReading.ReadAll(home)));
    }

    [Fact]
    public void A_path_entry_that_is_not_a_full_path_is_never_searched()
    {
        var skipped = Path.Combine(_root, "relative-bin");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, skipped);
        Assert.SkipWhen(Path.IsPathFullyQualified(relative), "the temporary folder is on another drive than the one the tests run in");

        PlantAt(Path.Combine(skipped, McpServerLocator.ExecutableName));
        var other = PlantAt(Path.Combine(_root, "bin", McpServerLocator.ExecutableName));
        var runner = new Runner();

        var path = PathReading.Read(Scope(McpClientMachine.CurrentPlatform, relative + Path.PathSeparator + Path.GetDirectoryName(other)), runner);

        Assert.Equal(other, path.Found);
        Assert.False(path.ThisCopy);
        Assert.Equal(_here, path.Version);
        Assert.Equal(new HealthFinding(HealthArea.OnPath, "PATH", HealthStatus.Ok, "a terminal runs another keypaste") { Where = other }, path.Finding);
        Assert.Equal([$"{other} --version"], runner.Calls);
    }

    [Fact]
    public void A_terminal_that_runs_this_keypaste_starts_nothing()
    {
        PlantAt(This);
        var runner = new Runner();

        var path = PathReading.Read(Scope(McpClientMachine.CurrentPlatform, Path.GetDirectoryName(This)), runner);

        Assert.True(path.ThisCopy);
        Assert.Equal(CoreInfo.Version, path.Version);
        Assert.Equal(new HealthFinding(HealthArea.OnPath, "PATH", HealthStatus.Ok, "a terminal runs this keypaste") { Where = This }, path.Finding);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void With_no_keypaste_on_path_and_no_install_to_name_the_folder_holding_it_goes_on_path()
    {
        Assert.Equal(
            new HealthFinding(HealthArea.OnPath, "PATH", HealthStatus.Fix, "a terminal finds no keypaste on PATH") { Fix = "put the folder holding keypaste on PATH" },
            PathReading.Read(Scope(McpClientMachine.CurrentPlatform, Path.Combine(_root, "empty")), new Runner()).Finding);
    }

    [Fact]
    public void With_no_keypaste_on_path_the_linux_links_folder_goes_on_path()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the link is Linux's");

        PlantAt(Path.Combine(Home, ".local", "bin", "keypaste"));

        Assert.Equal(
            new HealthFinding(HealthArea.OnPath, "PATH", HealthStatus.Fix, "a terminal finds no keypaste on PATH") { Fix = "add ~/.local/bin to PATH in your shell's startup file" },
            PathReading.Read(Scope(McpClientPlatform.Linux, Path.Combine(_root, "empty")), new Runner()).Finding);
    }

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string PlantAt(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\n");
        return path;
    }

    private static void Register(McpClientMachine machine, string command, params string[] arguments)
    {
        var path = McpConfigLocation.Cursor.PathOn(machine)!;
        var entry = new JsonObject
        {
            ["command"] = command,
            ["args"] = new JsonArray([.. arguments.Select(argument => (JsonNode?)JsonValue.Create(argument))]),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonObject { ["mcpServers"] = new JsonObject { ["keypaste"] = entry } }.ToJsonString());
    }

    private static OwnerReading Owner(OwnerKind kind, string? version, OwnerAnswer answer = OwnerAnswer.Unlocked) =>
        new(answer, kind, version, new HealthFinding(HealthArea.Owner, "owner", HealthStatus.Ok, "answers"));

    private static ToolReading Registered(string id, string? version) =>
        new(McpClientCatalog.Find(id)!, new McpEntryReading(McpEntryPresence.Present, Path: null, Reason: null), version, new HealthFinding(HealthArea.Tool, id, HealthStatus.Ok, "starts keypaste"));

    private static PathReading OnPath(string? version, bool thisCopy = false) =>
        new("/usr/local/bin/keypaste", thisCopy, version, new HealthFinding(HealthArea.OnPath, "PATH", HealthStatus.Ok, "a terminal runs keypaste"));

    private HealthScope Scope(McpClientPlatform platform, string? pathVariable = null)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = Home,
            ["USERPROFILE"] = Home,
            ["APPDATA"] = Path.Combine(Home, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(Home, "AppData", "Local"),
            [KeypasteHome.EnvironmentVariable] = KeypasteHomeDirectory,
        };

        if (pathVariable is not null)
        {
            variables["PATH"] = pathVariable;
        }

        string? Variable(string name) => variables.GetValueOrDefault(name);

        return new HealthScope(McpClientMachine.From(Variable, platform), Variable, NamedVault: null, This, AppImage: null);
    }

    /// <summary>Answers each <c>--version</c> with <c>0.5.1</c> and anything else with success, unless told otherwise, recording every call.</summary>
    private sealed class Runner : IProcessRunner
    {
        internal List<string> Calls { get; } = [];

        internal Dictionary<string, ProcessResult> Answers { get; } = new(StringComparer.Ordinal);

        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout)
        {
            var call = fileName + " " + string.Join(' ', arguments);
            Calls.Add(call);

            if (Answers.TryGetValue(call, out var answer))
            {
                return answer;
            }

            return arguments is ["--version"]
                ? new ProcessResult(ToolFound: true, ExitCode: 0, _here + "\n", string.Empty)
                : new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
        }
    }
}
