using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste</c> alone on a terminal: each row, each next step, and that it asks for nothing, starts nothing and
/// changes nothing (D-0467, D-0471).
/// </summary>
/// <remarks>
/// Each case builds its machine under the harness's directory as <c>DoctorVerbTests</c> does: a vault that is only the
/// twelve bytes read of one, the AI tools' files under the harness's home, and owners that are real listeners on pipes
/// of their own.
/// </remarks>
public sealed class StatusTests : IDisposable
{
    private const string _indent = "            ";

    private readonly CliHarness _harness = new() { StdoutIsTerminal = true };

    public void Dispose() => _harness.Dispose();

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private string This => _harness.ProcessPath!;

    [Fact]
    public void With_nothing_set_up_it_prints_the_version_three_rows_and_says_to_choose_a_vault()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal(
            Lines(
                $"keypaste {CoreInfo.Version}",
                "  vault     none chosen",
                "  held by   nothing, as no vault is chosen",
                "  AI tools  none installed",
                "› run `keypaste use <path>` to choose your vault"),
            _harness.Out);
        Assert.Empty(_harness.Err);
    }

    [Fact]
    public void The_chosen_vault_with_a_tool_connected_and_nothing_holding_it_says_to_open_the_app()
    {
        Healthy();
        Register(McpConfigLocation.Cursor, This, "mcp");

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        var vault = Pieces("vault");
        Assert.Equal(2, vault.Count);
        AssertShows(_harness.VaultPath, vault[0]);
        Assert.Equal("chosen in the app", vault[1]);
        Assert.Equal("nothing holds it unlocked", Said("held by"));
        Assert.Equal("Cursor connected", Said("AI tools"));
        Assert.Equal("› open the keypaste app, or run `keypaste approve`", Step());
        Assert.Empty(_harness.Err);
    }

    [Fact]
    public void A_missing_vault_named_by_the_variable_says_so_and_says_to_choose_a_vault()
    {
        Healthy();
        var missing = Path.Combine(_harness.Directory, "gone.kdbx");
        _harness.Environment[VaultLocation.EnvironmentVariable] = missing;
        Register(McpConfigLocation.Cursor, This, "mcp");

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        var vault = Pieces("vault");
        Assert.Equal(3, vault.Count);
        AssertShows(missing, vault[0]);
        Assert.Equal(["from KEYPASTE_VAULT", "missing"], vault.Skip(1));
        Assert.Equal("› run `keypaste use <path>` to choose your vault", Step());
    }

    [Fact]
    public async Task An_owner_on_another_version_says_to_see_doctor()
    {
        Healthy();
        Register(McpConfigLocation.Cursor, This, "mcp");
        var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        done.CancelAfter(TimeSpan.FromSeconds(30));
        var owner = AnswerOnceAsync(pipe, AttachReply.To("status-session") with { OwnerVersion = "0.0.1" }, done.Token);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());
        await owner;

        Assert.Equal("› run `keypaste doctor` to see what to fix", Step());
    }

    [Fact]
    public void An_unreadable_app_toml_with_no_tool_connected_says_to_see_doctor()
    {
        var settings = KeypasteHome.SettingsPath(Home);
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "[[settings]]\nnot a pair\n");

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal("none chosen; app.toml could not be read", Said("vault"));
        Assert.Equal("› run `keypaste doctor` to see what to fix", Step());
    }

    [Fact]
    public void Each_tool_is_connected_not_connected_or_unknown_and_none_connected_says_to_run_setup()
    {
        Healthy();
        Directory.CreateDirectory(McpConfigLocation.Cursor.Folders(Machine())[0]);
        var vscode = McpConfigLocation.VsCode.PathOn(Machine())!;
        Directory.CreateDirectory(Path.GetDirectoryName(vscode)!);
        File.WriteAllText(vscode, "{ this is not json");

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal("Cursor not connected · VS Code unknown", Said("AI tools"));
        Assert.Equal("› run `keypaste setup` to connect your AI tools", Step());

        Register(McpConfigLocation.ClaudeCode, This, "mcp");
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal("Claude Code connected · Cursor not connected · VS Code unknown", Said("AI tools"));
    }

    [Theory]
    [InlineData(OwnerKind.DesktopApp, "the keypaste app, unlocked")]
    [InlineData(OwnerKind.TerminalAgent, "keypaste approve")]
    [InlineData(OwnerKind.CommandLine, "a keypaste process, unlocked")]
    public async Task Who_holds_the_vault_unlocked_is_named_and_with_nothing_to_fix_it_points_at_the_help(OwnerKind kind, string held)
    {
        Healthy();
        Register(McpConfigLocation.Cursor, This, "mcp");
        await using var owner = SessionOwner.Start(_harness);
        Assert.True(VaultClaim.TryAcquire(KeypasteHome.Resolve(Home), _harness.VaultPath, kind, out var claim, out var refusal), refusal);

        using (claim)
        {
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());
        }

        Assert.Equal(held, Said("held by"));
        Assert.Equal("› `keypaste --help` lists every command", Step());
        Assert.Empty(owner.Authority.Clients);
    }

    [Fact]
    public async Task A_vault_held_locked_says_to_unlock_it_in_the_app()
    {
        Healthy();
        Register(McpConfigLocation.Cursor, This, "mcp");
        await using var owner = SessionOwner.Start(_harness);
        owner.Lifetime.End();

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal("a keypaste process, locked", Said("held by"));
        Assert.Equal("› unlock your vault in the keypaste app", Step());
    }

    [Fact]
    public async Task Something_doctor_would_fix_says_to_run_doctor()
    {
        Healthy();
        Register(McpConfigLocation.Cursor, McpServerLocator.FileName, "mcp");
        await using var owner = SessionOwner.Start(_harness);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal("Cursor connected", Said("AI tools"));
        Assert.Equal("a keypaste process, unlocked", Said("held by"));
        Assert.Equal("› run `keypaste doctor` to see what to fix", Step());
    }

    /// <summary>
    /// The founder's rule, as for doctor: it asks for nothing, starts no process, not even the keypaste a tool registers
    /// that doctor would ask its version, and every byte under the harness is as it was.
    /// </summary>
    [Fact]
    public async Task It_asks_nothing_starts_nothing_and_changes_no_byte_under_home()
    {
        Healthy();
        await using var owner = SessionOwner.Start(_harness);
        var old = Plant(Path.Combine(_harness.Directory, "old"), McpServerLocator.FileName);
        _harness.ProcessRunner.Installed.Add(old);
        _harness.ProcessRunner.Says[old] = "0.0.1\n";
        Register(McpConfigLocation.Cursor, old, "mcp");
        Register(McpConfigLocation.ClaudeCode, This, "mcp", "--client-label", "claude-code");
        File.WriteAllText(KeypasteHome.ClientsPath(Home), "this is not toml\n");

        _harness.Prompt.Interactive = true;
        var before = Snapshot(_harness.Directory);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.Equal(before, Snapshot(_harness.Directory));
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.ProcessRunner.Calls);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(owner.Authority.Clients);
        Assert.False(File.Exists(KeypasteHome.AuditPath(Home)));
        Assert.False(Directory.Exists(Path.Combine(Home, VaultClaim.DirectoryName)));
        Assert.Equal("Claude Code, Cursor connected", Said("AI tools"));
    }

    [Fact]
    public void A_vault_path_holding_escapes_and_a_line_break_prints_as_text_on_its_own_row()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a Windows file name cannot hold a control character");

        var hostile = Path.Combine(Path.GetPathRoot(_harness.Directory)!, "\u001b]0;owned\u0007\u202Ev\n› forged.kdbx");
        _harness.Environment[VaultLocation.EnvironmentVariable] = hostile;

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.DoesNotContain("\u001b", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", _harness.Out, StringComparison.Ordinal);
        Assert.Single(_harness.Out.Split(Environment.NewLine), line => line.StartsWith("› ", StringComparison.Ordinal));
        Assert.Contains("missing", Pieces("vault"));
        Assert.Equal("› run `keypaste use <path>` to choose your vault", Step());
    }

    [Fact]
    public void A_long_vault_path_keeps_its_start_and_end_within_eighty_columns()
    {
        var path = Path.Combine(_harness.Directory, new string('v', 120) + ".kdbx");
        _harness.Environment[VaultLocation.EnvironmentVariable] = path;

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        Assert.All(_harness.Out.Split(Environment.NewLine), line => Assert.True(line.Length <= 80, $"{line.Length} columns: {line}"));
        var shown = Pieces("vault")[0];
        Assert.Contains("…", shown, StringComparison.Ordinal);
        AssertShows(path, shown);
    }

    [Fact]
    public void A_vault_path_longer_than_doctor_shows_keeps_its_real_end_and_splits_no_character()
    {
        var path = Path.Combine(_harness.Directory, new string('v', 200), new string('v', 200), new string('v', 200), "\U0001F511" + new string('w', 28) + ".kdbx");
        _harness.Environment[VaultLocation.EnvironmentVariable] = path;

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run());

        var shown = Pieces("vault")[0];
        Assert.True(shown.Length <= 68, $"{shown.Length} columns: {shown}");
        AssertShows(path, shown);
        Assert.DoesNotContain(shown, char.IsSurrogate);
    }

    [Fact]
    public void A_row_too_wide_breaks_between_pieces_under_the_first_value()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        List<string> names = [.. Enumerable.Range(1, 9).Select(i => $"Tool Number {i},")];

        StatusCommand.Row(writer, new FakeConsoleStyle(), "AI tools", [[.. names, "Last connected"], ["Other not connected"]]);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.StartsWith("  AI tools  Tool Number 1,", lines[0], StringComparison.Ordinal);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"{line.Length} columns: {line}"));
        Assert.All(lines.Skip(1), line => Assert.StartsWith(_indent, line, StringComparison.Ordinal));
        var listed = string.Join(" ", names.Append("Last connected"));
        var read = string.Join(" ", lines.Select(line => line[_indent.Length..]));
        Assert.True(read == $"{listed} · Other not connected" || read == $"{listed} Other not connected", read);
    }

    /// <summary>This keypaste, and a chosen vault that is a KeePass database.</summary>
    private void Healthy()
    {
        _harness.ProcessPath = Plant(Path.Combine(_harness.Directory, "bin"), McpServerLocator.FileName);
        _harness.ProcessRunner.Installed.Add(This);
        PlantVault(_harness.VaultPath);
        Assert.Equal(ChooseOutcome.Chosen, ChosenVault.Choose(Home, _harness.VaultPath, onlyIfNone: false));
    }

    /// <summary>A row's value, each line it takes with the margin dropped.</summary>
    private List<string> Row(string label)
    {
        var lines = _harness.Out.Split(Environment.NewLine);
        var start = Array.FindIndex(lines, line => line.StartsWith("  " + label.PadRight(10), StringComparison.Ordinal));
        Assert.True(start >= 0, $"no {label} row in: {_harness.Out}");

        return [.. lines.Skip(start).Take(1).Concat(lines.Skip(start + 1).TakeWhile(line => line.StartsWith(_indent, StringComparison.Ordinal))).Select(line => line[_indent.Length..])];
    }

    /// <summary>A row read as one line, for a row too short to break.</summary>
    private string Said(string label) => Assert.Single(Row(label));

    /// <summary>A row's pieces, which a dot separates on one line and a break separates across lines.</summary>
    private List<string> Pieces(string label) => [.. Row(label).SelectMany(line => line.Split(" · "))];

    private string Step() => _harness.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[^1];

    private McpClientMachine Machine() =>
        McpClientMachine.From(name => _harness.Environment.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);

    /// <summary>Writes keypaste's entry into a tool's own JSON file.</summary>
    private void Register(McpConfigLocation location, string command, params string[] arguments)
    {
        var path = location.PathOn(Machine())!;
        var servers = new JsonObject { ["keypaste"] = new JsonObject { ["command"] = command, ["args"] = new JsonArray(arguments.Select(argument => (JsonNode?)argument).ToArray()) } };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonObject { ["mcpServers"] = servers }.ToJsonString());
    }

    /// <summary>Listens on <paramref name="pipe"/> for one attach naming no AI tool and answers it with <paramref name="reply"/>.</summary>
    private static async Task AnswerOnceAsync(string pipe, AttachReply reply, CancellationToken cancellationToken)
    {
        await using var server = new NamedPipeServerStream(
            pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(cancellationToken);
        using var framer = new MessageFramer(server, ownsStream: false);

        var frame = await framer.ReadAsync(cancellationToken);
        Assert.True(ApproverProtocol.TryDecode(frame, out AttachRequest? attach));
        Assert.Null(attach!.Client);
        await framer.WriteAsync(ApproverProtocol.Encode(reply), cancellationToken);
    }

    /// <summary>The path as printed: itself, or its start and end around an ellipsis.</summary>
    private static void AssertShows(string path, string shown)
    {
        var cut = shown.IndexOf('…', StringComparison.Ordinal);

        if (cut < 0)
        {
            Assert.Equal(path, shown);
            return;
        }

        Assert.StartsWith(shown[..cut], path, StringComparison.Ordinal);
        Assert.EndsWith(shown[(cut + 1)..], path, StringComparison.Ordinal);
    }

    private static string Plant(string folder, string name)
    {
        var path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    /// <summary>A file holding only the twelve bytes a KeePass database starts with, which is all that is read of one.</summary>
    private static void PlantVault(string path)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, KdbxFormat.FileSignature1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), KdbxFormat.FileSignature2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), 4);
        File.WriteAllBytes(path, header);
    }

    /// <summary>Every file and folder under <paramref name="root"/>, each file with its SHA-256.</summary>
    private static List<string> Snapshot(string root) =>
        [.. Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(entry => File.Exists(entry) ? $"{entry} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}" : entry)
            .Order(StringComparer.Ordinal)];

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
