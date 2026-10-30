using System.Text;
using System.Text.Json;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste clients</c>: the one place to read and change agents' settings.</summary>
/// <remarks>
/// Those that give an agent more are kept in the vault, so it asks the process holding the vault: a change
/// that gives nothing more applies at once, and any other waits for the person to confirm it there. With
/// nothing holding the vault it can only lower this computer's ceilings in <c>clients.toml</c>, which can
/// never give more. Any program the person runs can run this command, so the confirmation is the boundary,
/// and it says the request came from a terminal without claiming the person typed it.
/// </remarks>
internal static class ClientsCommand
{
    private const string _verb = "keypaste clients";
    private const string _projectOption = "project";
    private const string _modeOption = "mode";
    private const string _seeOption = "see";
    private const string _valuesOption = "values";
    private const string _runOption = "run";
    private const string _perHourOption = "per-hour";
    private const string _untilOption = "until";
    private const string _resetOption = "reset";

    private const string _noAnswer =
        "the keypaste process holding the vault did not answer; it may be older than this keypaste";

    private static readonly string[] _changeOptions = [_projectOption, _modeOption, _valuesOption, _runOption, _perHourOption, _untilOption];

    private static readonly TimeSpan _listBound = TimeSpan.FromSeconds(10);

    // Long enough for the person to answer the longest window the owner can have, and a little more.
    private static readonly TimeSpan _changeBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    private static readonly OptionSpec[] _options =
    [
        new(CliJson.Option, TakesValue: false),
        new("vault", TakesValue: true),
        new(SessionPipe.ApproverOption, TakesValue: true),
        new(_projectOption, TakesValue: true),
        new(_modeOption, TakesValue: true),
        new(_seeOption, TakesValue: true, Repeats: true),
        new(_valuesOption, TakesValue: true),
        new(_runOption, TakesValue: true),
        new(_perHourOption, TakesValue: true),
        new(_untilOption, TakesValue: true),
        new(_resetOption, TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            return Usage(context, error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        var clients = KeypasteHome.ClientsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (line.Operands.Count == 0)
        {
            return HasChange(line) ? Usage(context, "name the agent to change") : List(line, clients, context);
        }

        if (line.Operands.Count != 1 || line.HasFlag(CliJson.Option))
        {
            return Usage(context, "give one agent's name and what to change, or nothing to list them");
        }

        var agent = line.Operands[0];

        if (!AgentNames.IsValid(agent, out var nameError))
        {
            return Usage(context, nameError);
        }

        var see = line.Values(_seeOption);

        if (!UntilTime.TryRecord(line.Value(_untilOption), context.Clock, out var until, out var untilError))
        {
            return Usage(context, untilError);
        }

        if (!AccessEdit.TryCreate(
                line.Value(_projectOption),
                line.Value(_modeOption),
                see.Count == 0 ? null : see,
                line.Value(_valuesOption),
                line.Value(_runOption),
                line.Value(_perHourOption),
                until,
                line.HasFlag(_resetOption),
                out var edit,
                out var editError))
        {
            return Usage(context, editError);
        }

        var request = new AccessChangeRequest(agent, string.Empty)
        {
            Origin = AccessOrigin.Terminal,
            Project = line.Value(_projectOption),
            Mode = line.Value(_modeOption),
            See = see.Count == 0 ? null : see,
            Values = line.Value(_valuesOption),
            Run = line.Value(_runOption),
            PerHour = line.Value(_perHourOption),
            Until = until,
            Reset = line.HasFlag(_resetOption),
        };

        return ChangeAsync(line, clients, agent, edit, request, context).GetAwaiter().GetResult();
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine($"usage: {_verb} [--json] [--vault <path>] [--approver <name>]");
        writer.WriteLine($"       {_verb} <agent> [--project <project>[:<environment>]]");
        writer.WriteLine("                        [--mode ask|allow-for-a-while|automatic] [--see <selector>]...");
        writer.WriteLine("                        [--values yes|no] [--run on|off] [--per-hour <n>|none]");
        writer.WriteLine("                        [--until <date>|<time>|none] [--reset]");
        writer.WriteLine("                        [--vault <path>] [--approver <name>]");
        writer.WriteLine();
        writer.WriteLine("shows each agent's settings, from the vault through the app or `keypaste approve` holding it,");
        writer.WriteLine("and this computer's ceilings in clients.toml; with an agent's name, changes them. Settings");
        writer.WriteLine("follow the name an agent's bridge was given with --client-label; \"*\" is every other agent.");
        writer.WriteLine();
        writer.WriteLine("A change that gives the agent nothing more applies at once. One that gives more waits for");
        writer.WriteLine("you to confirm it where the vault is unlocked; with nothing holding it, only a change that");
        writer.WriteLine("lowers this computer's ceilings can be made.");
        writer.WriteLine();
        writer.WriteLine("--until ends Automatic: a date such as 2026-12-31 ends at the close of that day, and a time");
        writer.WriteLine("such as 2026-12-31 18:00 is on this computer's clock unless it ends in Z or an offset such");
        writer.WriteLine("as +02:00.");
    }

    private static bool HasChange(CommandLine line) =>
        _changeOptions.Any(option => line.Value(option) is not null)
        || line.Values(_seeOption).Count > 0
        || line.HasFlag(_resetOption);

    private static int List(CommandLine line, string clients, CliContext context)
    {
        if (!AccessCaps.TryLoad(clients, out var caps, out var problem))
        {
            return Malformed(context, clients, problem);
        }

        var (agents, note, exit) = ListAsync(line, context).GetAwaiter().GetResult();

        if (exit != CliApp.ExitSuccess)
        {
            return exit;
        }

        if (line.HasFlag(CliJson.Option))
        {
            WriteJson(context.Stdout, agents, caps);
        }
        else
        {
            WriteTable(agents, caps, context);
        }

        foreach (var agent in agents.Where(agent => agent.ProjectsNotShown > 0))
        {
            context.Stderr.WriteLine(
                $"{_verb}: {EntryNameSanitizer.Sanitize(agent.Name, AgentNames.MaximumLength).Text} has {agent.ProjectsNotShown} project rows not shown here, "
                + "more than one reply carries; the app's Agents screen shows them all");
        }

        if (note is not null)
        {
            context.Stderr.WriteLine($"{_verb}: {note}");
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>
    /// The vault's records through its owner, asked for in turns until the listing ends, or none with a
    /// note saying why they are not shown.
    /// </summary>
    private static async Task<(IReadOnlyList<AccessRow> Agents, string? Note, int Exit)> ListAsync(CommandLine line, CliContext context)
    {
        if (!TryPipe(line, context, out var vault, out var pipe, out var why))
        {
            return ([], $"{why}, so only this computer's ceilings are shown", CliApp.ExitSuccess);
        }

        using var bound = new CancellationTokenSource(_listBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            if (attachment.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"{_verb}: {Shown(refusal)}");
                return ([], null, CliApp.ExitInternalError);
            }

            return ([], $"nothing holds {vault} unlocked, so the vault's settings are not shown: unlock it in the app or run `keypaste approve` to see them", CliApp.ExitSuccess);
        }

        List<AccessRow> agents = [];
        string? note = null;

        while (true)
        {
            var reply = await client.ListAccessAsync(new AccessListRequest { Vault = vault, Session = session, From = agents.Count }, bound.Token);

            if (reply is null)
            {
                context.Stderr.WriteLine($"{_verb}: {_noAnswer}");
                return ([], null, CliApp.ExitInternalError);
            }

            if (!reply.Answered)
            {
                context.Stderr.WriteLine($"{_verb}: {Shown(reply.Reason)}");
                return ([], null, CliApp.ExitInternalError);
            }

            note ??= reply.Reason.Length > 0 ? Shown(reply.Reason) : null;
            agents.AddRange(reply.Agents);

            if (reply.Complete)
            {
                return (agents, note, CliApp.ExitSuccess);
            }

            // A reply that carries nothing and says more follows would ask the same question forever.
            if (reply.Agents.Count == 0)
            {
                return (agents, $"the keypaste process holding the vault stopped listing agents' settings after {agents.Count}; these are all it sent", CliApp.ExitSuccess);
            }
        }
    }

    private static async Task<int> ChangeAsync(
        CommandLine line,
        string clients,
        string agent,
        AccessEdit edit,
        AccessChangeRequest request,
        CliContext context)
    {
        if (TryPipe(line, context, out var vault, out var pipe, out _))
        {
            using var bound = new CancellationTokenSource(_changeBound);
            await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

            if (attachment is { Client: { } client, Session: { } session })
            {
                var reply = await client.ChangeAccessAsync(request with { Vault = vault, Session = session }, bound.Token);

                if (reply is null)
                {
                    context.Stderr.WriteLine($"{_verb}: {_noAnswer}");
                    return CliApp.ExitInternalError;
                }

                var said = Shown(edit.OnThisClock(reply.Reason, context.Clock.LocalTimeZone));

                if (reply.Decision != AuditDecision.Granted)
                {
                    context.Stderr.WriteLine($"{_verb}: not changed: {said}");
                    return CliApp.ExitInternalError;
                }

                Done(context, agent, said);
                return CliApp.ExitSuccess;
            }

            if (attachment.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"{_verb}: {Shown(refusal)}");
                return CliApp.ExitInternalError;
            }
        }

        return Lower(clients, agent, edit, context);
    }

    /// <summary>With nothing holding the vault: lowers this computer's ceilings, the only change that can be made without it.</summary>
    private static int Lower(string clients, string agent, AccessEdit edit, CliContext context)
    {
        CapsChange changed;
        string problem;

        try
        {
            changed = AccessCaps.TryChange(clients, current => current.TryLower(agent, edit, out var lowered) ? lowered : null, out problem);
        }
        catch (ArgumentException tooMany)
        {
            context.Stderr.WriteLine($"{_verb}: {clients} could not hold it: {tooMany.Message}");
            return CliApp.ExitInternalError;
        }

        switch (changed)
        {
            case CapsChange.Unreadable:
                return Malformed(context, clients, problem);
            case CapsChange.Declined:
                context.Stderr.WriteLine(
                    $"{_verb}: nothing holds the vault unlocked, and this could give {agent} more or removes what the vault's settings hold. Unlock the vault in the app or run `keypaste approve`, then run this again.");
                return CliApp.ExitInternalError;
            case CapsChange.Unwritten:
                context.Stderr.WriteLine($"{_verb}: {clients} could not be written: {problem}");
                return CliApp.ExitInternalError;
        }

        Record(context, agent, edit);
        Done(context, agent, $"applied on this computer, since it gives the agent nothing more ({edit}); applies to its next request");
        return CliApp.ExitSuccess;
    }

    /// <summary>Records a change made with nothing holding the vault, as the bridge records a call nothing answered.</summary>
    private static void Record(CliContext context, string agent, AccessEdit edit)
    {
        var path = KeypasteHome.AuditPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!AuditLog.TryOpen(path, context.Clock, out var log, out var error))
        {
            context.Stderr.WriteLine($"{_verb}: the change was made and could not be recorded: {error}");
            return;
        }

        using (log)
        {
            var record = RequestRecords.AccessChange(
                AuditClient.Of(_verb, CoreInfo.Version, agent),
                string.Empty,
                AuditDecision.Granted,
                AuditMethod.Narrowing,
                $"applied on this computer with nothing holding the vault, since it gives the agent nothing more ({agent}: {edit})",
                session: null,
                vault: null);

            if (!log.TryAppend(record, out var appendError))
            {
                context.Stderr.WriteLine($"{_verb}: the change was made and could not be recorded: {appendError}");
            }
        }
    }

    private static void Done(CliContext context, string agent, string what)
    {
        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        var dot = style.Glyph(context.Stderr, Mark.Dot);
        context.Stderr.WriteLine($"  {done} {agent} {dot} {what}");
    }

    private static void WriteTable(IReadOnlyList<AccessRow> agents, AccessCaps caps, CliContext context)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;
        var zone = context.Clock.LocalTimeZone;

        List<string[]> rows = [];

        foreach (var agent in agents)
        {
            var read = AcafRecord.Read(agent.Record);
            var access = read.Access;
            var name = EntryNameSanitizer.Sanitize(agent.Name, AgentNames.MaximumLength).Text + (agent.Understood ? string.Empty : " (not all understood)");

            rows.Add([name, string.Empty, Mode(access.Mode, access.Limit, zone), access.Values ? "yes" : "no", access.Run ? "on" : "off", string.Join(' ', access.See.Globs)]);
            rows.AddRange(access.Projects.Select(project => new[] { string.Empty, project.Scope.Name, Mode(project.Mode, project.Limit, zone), string.Empty, string.Empty, string.Empty }));
        }

        if (!agents.Any(agent => string.Equals(agent.Name, AgentNames.Any, StringComparison.Ordinal)))
        {
            var defaults = AgentAccess.Defaults;
            rows.Add([AgentNames.Any + "   (default)", string.Empty, Mode(defaults.Mode, defaults.Limit, zone), "yes", "off", string.Join(' ', defaults.See.Globs)]);
        }

        Table(writer, style, ["AGENT", "PROJECT", "MODE", "VALUES", "RUN", "SEES"], rows);

        if (caps.Rows.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine(style.Paint(writer, Tone.Muted, "  this computer's ceilings, in clients.toml: nothing above gives more than these"));
        Table(
            writer,
            style,
            ["AGENT", "PROJECT", "MODE", "VALUES", "RUN", "SEES"],
            [
                .. caps.Rows.Select(row => new[]
                {
                    row.Label,
                    row.Project?.Name ?? string.Empty,
                    Ceiling(row.Mode, row.Limit, zone),
                    row.Values is { } values ? values ? "yes" : "no" : string.Empty,
                    row.Run is { } run ? run ? "on" : "off" : string.Empty,
                    row.See is { } see ? string.Join(' ', see.Globs) : string.Empty,
                }),
            ]);
    }

    /// <summary>A ceiling's mode and limit as the table shows them, such as <c>automatic, until 2026-12-31 18:00 +02:00</c>.</summary>
    private static string Ceiling(AgentMode? mode, AccessLimit? limit, TimeZoneInfo zone) => (mode, limit) switch
    {
        (null, null) => string.Empty,
        (null, { } held) => AccessChangePrompt.Words(held, zone),
        ({ } held, _) => Mode(held, limit ?? AccessLimit.None, zone),
    };

    private static string Mode(AgentMode mode, AccessLimit limit, TimeZoneInfo zone) =>
        mode == AgentMode.Automatic && !limit.IsNone
            ? $"{AgentModes.Name(mode)}, {AccessChangePrompt.Words(limit, zone)}"
            : AgentModes.Name(mode);

    private static void Table(TextWriter writer, IConsoleStyle style, string[] headings, List<string[]> rows)
    {
        var widths = headings[..^1]
            .Select((heading, column) => Math.Max(heading.Length, rows.Count == 0 ? 0 : rows.Max(row => row[column].Length)))
            .ToArray();

        string Line(string[] cells) =>
            ("  " + string.Concat(widths.Select((width, column) => cells[column].PadRight(width) + "  ")) + cells[^1]).TrimEnd();

        writer.WriteLine(style.Paint(writer, Tone.Muted, Line(headings)));

        foreach (var row in rows)
        {
            writer.WriteLine(Line(row));
        }
    }

    /// <summary>One array, one object per row, as every <c>--json</c> is: each agent's record as ACAF writes it, then each ceiling.</summary>
    private static void WriteJson(TextWriter writer, IReadOnlyList<AccessRow> agents, AccessCaps caps)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            foreach (var agent in agents)
            {
                json.WriteStartObject();
                json.WriteString("kind", "agent");
                json.WriteString("name", agent.Name);
                json.WriteBoolean("understood", agent.Understood);

                if (agent.ProjectsNotShown > 0)
                {
                    json.WriteNumber("projects_not_shown", agent.ProjectsNotShown);
                }

                json.WritePropertyName("record");

                using (var record = JsonDocument.Parse(AcafRecord.Write(AcafRecord.Read(agent.Record).Access)))
                {
                    record.RootElement.WriteTo(json);
                }

                json.WriteEndObject();
            }

            foreach (var row in caps.Rows)
            {
                json.WriteStartObject();
                json.WriteString("kind", "ceiling");
                json.WriteString("label", row.Label);

                if (row.Project is { } project)
                {
                    json.WriteString("project", project.Name);
                }

                if (row.Mode is { } mode)
                {
                    json.WriteString("mode", AgentModes.Name(mode));
                }

                if (row.Limit?.PerHour is { } perHour)
                {
                    json.WriteNumber("per_hour", perHour);
                }

                if (row.Limit?.Until is { } until)
                {
                    json.WriteString("until", UntilTime.Recorded(until));
                }

                if (row.Values is { } values)
                {
                    json.WriteString("values", values ? "yes" : "no");
                }

                if (row.Run is { } run)
                {
                    json.WriteString("run", run ? "on" : "off");
                }

                if (row.See is { } see)
                {
                    json.WriteStartArray("see");

                    foreach (var glob in see.Globs)
                    {
                        json.WriteStringValue(glob);
                    }

                    json.WriteEndArray();
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static bool TryPipe(CommandLine line, CliContext context, out string vault, out string pipe, out string why)
    {
        pipe = string.Empty;

        if (!VaultLocator.TryResolve(line, context.Environment, out vault, out var locateError))
        {
            why = locateError;
            return false;
        }

        if (!SessionPipe.TryResolve(line, vault, context, out pipe, out var pipeError))
        {
            why = pipeError;
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>Another process's words, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    private static int Malformed(CliContext context, string path, string problem)
    {
        context.Stderr.WriteLine($"{_verb}: {path}: {problem}; fix it or delete it");
        return CliApp.ExitInternalError;
    }

    private static int Usage(CliContext context, string error)
    {
        context.Stderr.WriteLine($"{_verb}: {error}");
        WriteUsage(context.Stderr);
        return CliApp.ExitUsageError;
    }
}
