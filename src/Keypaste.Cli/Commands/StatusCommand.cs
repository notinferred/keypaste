using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Clients;
using Keypaste.Core.Health;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste</c> alone on a terminal: this keypaste's version, the vault it would use and who holds it, the AI tools
/// connected, and one next step (D-0471).
/// </summary>
/// <remarks>
/// It reads what <c>doctor</c> reads and starts no process (D-0467): the vault's header, each AI tool's file, and the
/// holder's answer to an attach naming no AI tool. The vault's path is made safe for the terminal as doctor's lines are.
/// </remarks>
internal static class StatusCommand
{
    private const int _width = 80;

    private const int _labelWidth = 10;

    private static readonly string _underValue = new(' ', 2 + _labelWidth);

    private static readonly (McpEntryPresence Presence, string Says)[] _presences =
    [
        (McpEntryPresence.Present, "connected"),
        (McpEntryPresence.Absent, "not connected"),
        (McpEntryPresence.Unknown, "unknown"),
    ];

    internal static int Execute(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var scope = new HealthScope(
            McpClientMachine.From(context.Environment.Get, McpClientMachine.CurrentPlatform),
            context.Environment.Get,
            NamedVault: null,
            context.ProcessPath,
            context.AppImage());
        var vault = VaultReading.Read(scope);
        var asking = OwnerReading.AskAsync(scope, vault.Path, CancellationToken.None);
        var tools = ToolReading.ReadAll(scope, runner: null);
        var owner = asking.GetAwaiter().GetResult();

        var writer = context.Stdout;
        var style = context.ConsoleStyle;

        writer.WriteLine($"keypaste {CoreInfo.Version}");
        Row(writer, style, "vault", Vault(writer, vault));
        Row(writer, style, "held by", [[HeldBy(owner)]]);
        Row(writer, style, "AI tools", Tools(tools));
        writer.WriteLine($"{style.Paint(writer, Tone.Accent, style.Glyph(writer, Mark.Prompt))} {NextStep(vault, owner, tools)}");
        return CliApp.ExitSuccess;
    }

    /// <summary>The first of: choose a vault, see doctor for an unreadable app.toml, connect an AI tool, hold the vault unlocked, see doctor, or the help.</summary>
    private static string NextStep(VaultReading vault, OwnerReading owner, IReadOnlyList<ToolReading> tools)
    {
        const string Doctor = "run `keypaste doctor` to see what to fix";

        if (vault.State is VaultState.None or VaultState.Missing or VaultState.NotKeePass)
        {
            return "run `keypaste use <path>` to choose your vault";
        }

        if (vault.State == VaultState.AppTomlUnreadable)
        {
            return Doctor;
        }

        if (!tools.Any(tool => tool.Reading.Presence == McpEntryPresence.Present))
        {
            return "run `keypaste setup` to connect your AI tools";
        }

        if (owner.Answer == OwnerAnswer.Locked)
        {
            return "unlock your vault in the keypaste app";
        }

        if (owner.Answer == OwnerAnswer.NothingListening)
        {
            return "open the keypaste app, or run `keypaste approve`";
        }

        var findings = vault.Findings.Concat(tools.Select(tool => tool.Finding)).Append(owner.Finding);

        return findings.Any(finding => finding.Status == HealthStatus.Fix)
            ? Doctor
            : "`keypaste --help` lists every command";
    }

    private static List<List<string>> Vault(TextWriter writer, VaultReading vault)
    {
        if (vault.Path is not { } path)
        {
            return [[vault.State == VaultState.AppTomlUnreadable ? "none chosen; app.toml could not be read" : "none chosen"]];
        }

        List<List<string>> said =
        [
            [Fitted(writer, DoctorCommand.Shown(path, int.MaxValue))],
            [vault.Source == VaultSource.Variable ? $"from {VaultLocator.EnvironmentVariable}" : "chosen in the app"],
        ];

        var state = vault.State switch
        {
            VaultState.Missing => "missing",
            VaultState.NotKeePass => "not a KeePass database",
            VaultState.Unreadable => "unreadable",
            _ => null,
        };

        if (state is not null)
        {
            said.Add([state]);
        }

        return said;
    }

    private static string HeldBy(OwnerReading owner) => owner.Answer switch
    {
        OwnerAnswer.Unlocked when owner.Kind == OwnerKind.TerminalAgent => "keypaste approve",
        OwnerAnswer.Unlocked => $"{OwnerReading.Holder(owner.Kind)}, unlocked",
        OwnerAnswer.Locked => $"{OwnerReading.Holder(owner.Kind)}, locked",
        OwnerAnswer.NothingListening => "nothing holds it unlocked",
        OwnerAnswer.NoVault => "nothing, as no vault is chosen",
        OwnerAnswer.OtherVault => "a keypaste process holding another vault",
        OwnerAnswer.InvalidPipe => $"cannot tell: {ApproverEndpoint.EnvironmentVariable} is not a pipe name keypaste accepts",
        _ => $"{OwnerReading.Holder(owner.Kind)}, not answering",
    };

    private static List<List<string>> Tools(IReadOnlyList<ToolReading> tools)
    {
        if (tools.Count == 0)
        {
            return [["none installed"]];
        }

        List<List<string>> groups = [];

        foreach (var (presence, says) in _presences)
        {
            var names = tools.Where(tool => tool.Reading.Presence == presence).Select(tool => tool.Tool.DisplayName).ToList();

            if (names.Count > 0)
            {
                groups.Add([.. names.Take(names.Count - 1).Select(name => name + ","), $"{names[^1]} {says}"]);
            }
        }

        return groups;
    }

    /// <summary>
    /// One row: its label, then each group's pieces, a space within a group and a dot between groups, starting a line
    /// under the first value where the next piece would pass the eightieth column.
    /// </summary>
    internal static void Row(TextWriter writer, IConsoleStyle style, string label, List<List<string>> groups)
    {
        var between = $" {style.Glyph(writer, Mark.Dot)} ";
        List<string> lines = [];
        var line = string.Empty;

        foreach (var group in groups)
        {
            for (var i = 0; i < group.Count; i++)
            {
                var joiner = line.Length == 0 ? string.Empty : i > 0 ? " " : between;

                if (line.Length > 0 && line.Length + joiner.Length + group[i].Length > _width - _underValue.Length)
                {
                    lines.Add(line);
                    line = group[i];
                }
                else
                {
                    line += joiner + group[i];
                }
            }
        }

        lines.Add(line);
        writer.WriteLine($"  {style.Paint(writer, Tone.Muted, label)}{new string(' ', _labelWidth - label.Length)}{lines[0]}");

        foreach (var more in lines.Skip(1))
        {
            writer.WriteLine(_underValue + more);
        }
    }

    /// <summary>The path, or its start and end around an ellipsis when it would pass the eightieth column on its own.</summary>
    private static string Fitted(TextWriter writer, string path)
    {
        var room = _width - _underValue.Length;

        if (path.Length <= room)
        {
            return path;
        }

        var ellipsis = ConsoleMarks.Carried(writer, "…", "...");
        var head = (room - ellipsis.Length) / 2;
        var tailStart = path.Length - (room - ellipsis.Length - head);

        if (char.IsLowSurrogate(path[head]))
        {
            head--;
        }

        if (char.IsLowSurrogate(path[tailStart]))
        {
            tailStart++;
        }

        return string.Concat(path.AsSpan(0, head), ellipsis, path.AsSpan(tailStart));
    }
}
