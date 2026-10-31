using System.CommandLine;
using System.Text;
using System.Text.Json;
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
    private const string _noAnswer =
        "the keypaste process holding the vault did not answer; it may be older than this keypaste";

    private static readonly TimeSpan _listBound = TimeSpan.FromSeconds(10);

    // Long enough for the person to answer the longest window the owner can have, and a little more.
    private static readonly TimeSpan _changeBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    private static readonly Argument<string?> _agent = Syntax.Optional("agent", "the agent whose settings to change; \"*\" is every other agent");
    private static readonly Option<string> _project = Syntax.Value("--project", "project[:environment]", "the project row to change");
    private static readonly Option<string> _mode = Syntax.Value("--mode", "ask|allow-for-a-while|automatic", "how it is asked");
    private static readonly Option<string[]> _see = Syntax.Values("--see", "selector", "what it can see, as often as you like");
    private static readonly Option<string> _values = Syntax.Value("--values", "yes|no", "whether it may read values");
    private static readonly Option<string> _run = Syntax.Value("--run", "on|off", "whether it may run commands");
    private static readonly Option<string> _perHour = Syntax.Value("--per-hour", "n|none", "how many releases an hour");
    private static readonly Option<string> _until = Syntax.Value(
        "--until", "date|time|none", "when Automatic ends: a date at its close, a time on this computer's clock unless it ends in Z or an offset");
    private static readonly Option<bool> _reset = Syntax.Flag("--reset", "go back to the defaults");

    internal static CliVerb Command { get; } = new(
        "clients",
        "show or change what each agent can see and do\n\n"
            + "shows each agent's settings, from the vault through the app or `keypaste approve` holding it, and this "
            + "computer's ceilings in clients.toml; with an agent's name, changes them. settings follow the name an "
            + "agent's bridge was given with --client-label. a change that gives the agent nothing more applies at once; "
            + "one that gives more waits for you to confirm it where the vault is unlocked, and with nothing holding it, "
            + "only a change that lowers this computer's ceilings can be made.",
        Execute)
    {
        _agent, _project, _mode, _see, _values, _run, _perHour, _until, _reset, CliOptions.Json, CliOptions.Vault, CliOptions.Approver,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var clients = KeypasteHome.ClientsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (line.GetValue(_agent) is not { } agent)
        {
            return HasChange(line) ? Usage(context, "name the agent to change") : List(line, clients, context);
        }

        if (line.GetValue(CliOptions.Json))
        {
            return Usage(context, "give one agent's name and what to change, or nothing to list them");
        }

        if (!AgentNames.IsValid(agent, out var nameError))
        {
            return Usage(context, nameError);
        }

        var see = line.GetValue(_see) ?? [];

        if (!UntilTime.TryRecord(line.GetValue(_until), context.Clock, out var until, out var untilError))
        {
            return Usage(context, untilError);
        }

        if (!AccessEdit.TryCreate(
                line.GetValue(_project),
                line.GetValue(_mode),
                see.Length == 0 ? null : see,
                line.GetValue(_values),
                line.GetValue(_run),
                line.GetValue(_perHour),
                until,
                line.GetValue(_reset),
                out var edit,
                out var editError))
        {
            return Usage(context, editError);
        }

        var request = new AccessChangeRequest(agent, string.Empty)
        {
            Origin = AccessOrigin.Terminal,
            Project = line.GetValue(_project),
            Mode = line.GetValue(_mode),
            See = see.Length == 0 ? null : see,
            Values = line.GetValue(_values),
            Run = line.GetValue(_run),
            PerHour = line.GetValue(_perHour),
            Until = until,
            Reset = line.GetValue(_reset),
        };

        return ChangeAsync(line, clients, agent, edit, request, context).GetAwaiter().GetResult();
    }

    private static bool HasChange(ParseResult line) =>
        new[] { _project, _mode, _values, _run, _perHour, _until }.Any(option => line.GetValue(option) is not null)
        || line.GetValue(_see) is { Length: > 0 }
        || line.GetValue(_reset);

    private static int List(ParseResult line, string clients, CliContext context)
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

        if (line.GetValue(CliOptions.Json))
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
    private static async Task<(IReadOnlyList<AccessRow> Agents, string? Note, int Exit)> ListAsync(ParseResult line, CliContext context)
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
        ParseResult line,
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

    private static bool TryPipe(ParseResult line, CliContext context, out string vault, out string pipe, out string why)
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
        context.Stderr.WriteLine($"see {_verb} --help");
        return CliApp.ExitUsageError;
    }
}
