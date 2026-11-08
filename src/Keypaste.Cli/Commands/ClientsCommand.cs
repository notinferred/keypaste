using System.CommandLine;
using System.Text;
using System.Text.Json;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste clients</c>: lists this computer's agent rules and known agents, turns an agent's run on or off and removes one.</summary>
/// <remarks>
/// The rules live in <c>agents.toml</c> under the keypaste home, never in a vault, so this verb reads and writes that file
/// itself and needs nothing holding the vault. Turning run on releases nothing unasked, since every run is asked, so it is
/// made at once and recorded.
/// </remarks>
internal static class ClientsCommand
{
    private const string _verb = "keypaste clients";

    private static readonly Argument<string?> _agent = Syntax.Optional("agent", "the agent to change, by its bridge's --client-label");
    private static readonly Option<string> _run = Syntax.Value("--run", "on|off", "whether it may ask to run commands, each run asked");
    private static readonly Option<bool> _remove = Syntax.Flag("--remove", "forget the agent, which turns its run off");

    internal static CliVerb Command { get; } = new(
        "clients",
        "list agents and turn their run on or off\n\n"
            + "lists the rules every agent's request is decided by on this computer, kept in agents.toml, and each agent it "
            + "knows by the name its bridge was given with --client-label. with an agent's name, turns its run on or off or "
            + "removes it. each run is still asked; the app's Settings sets the rules.",
        Execute)
    {
        _agent, _run, _remove, CliOptions.Json,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var file = new AgentRulesFile(KeypasteHome.AgentsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)));
        var run = line.GetValue(_run);
        var remove = line.GetValue(_remove);

        if (line.GetValue(_agent) is not { } agent)
        {
            return run is not null || remove ? Usage(context, "name the agent to change") : List(line, file, context);
        }

        if (line.GetValue(CliOptions.Json))
        {
            return Usage(context, "give one agent's name and what to change, or nothing to list them");
        }

        if (!AgentNames.IsNamed(agent))
        {
            return Usage(context, $"an agent's name is its bridge's --client-label: 1 to {AgentNames.MaximumLength} characters with no quote, backslash, slash or control character");
        }

        if ((run is null) == !remove)
        {
            return Usage(context, "give --run on, --run off or --remove");
        }

        if (run is not (null or "on" or "off"))
        {
            return Usage(context, "--run is on or off");
        }

        if (!file.TryRead(out var rules, out var problem))
        {
            return Malformed(context, problem);
        }

        if (remove && !rules.Agents.Any(known => string.Equals(known.Name, agent, StringComparison.Ordinal)))
        {
            context.Stderr.WriteLine($"{_verb}: this computer knows no agent named {Shown(agent)}, so nothing was removed");
            return CliApp.ExitUsageError;
        }

        AgentRules changed;

        try
        {
            changed = remove ? rules.Without(agent) : rules.WithRun(agent, run == "on");
        }
        catch (ArgumentException tooMany)
        {
            context.Stderr.WriteLine($"{_verb}: {tooMany.Message}");
            return CliApp.ExitUsageError;
        }

        var what = remove ? "removed; its run is off" : run == "on" ? "run on; each run is still asked" : "run off";

        if (changed.Equals(rules))
        {
            Done(context, agent, $"already {what}");
            return CliApp.ExitSuccess;
        }

        if (!file.TryWrite(changed, out problem))
        {
            context.Stderr.WriteLine($"{_verb}: nothing was changed: {problem}");
            return CliApp.ExitInternalError;
        }

        Record(context, agent, what);
        Done(context, agent, what);
        return CliApp.ExitSuccess;
    }

    private static int List(ParseResult line, AgentRulesFile file, CliContext context)
    {
        if (!file.TryRead(out var rules, out var problem))
        {
            return Malformed(context, problem);
        }

        if (line.GetValue(CliOptions.Json))
        {
            WriteJson(context.Stdout, rules);
        }
        else
        {
            WriteTable(rules, context);
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>Records a change to the rules, as the owner records each request it answers.</summary>
    private static void Record(CliContext context, string agent, string what)
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
                AuditMethod.RulesChanged,
                $"changed on this computer from a terminal: {what}",
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
        context.Stderr.WriteLine($"  {done} {Shown(agent)} {dot} {what}");
    }

    private static void WriteTable(AgentRules rules, CliContext context)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;

        Table(
            writer,
            style,
            ["VALUE", "EVERY AGENT"],
            [
                [FieldCategories.Shown(FieldCategory.Detail), rules.ShowsDetails ? "shown" : "hidden"],
                .. FieldCategories.Ruled.Select(category => new[] { FieldCategories.Shown(category), Word(rules.RuleFor(category)) }),
                [FieldCategories.Shown(FieldCategory.Passkey), Word(CategoryRule.Block)],
            ]);

        writer.WriteLine();
        Table(writer, style, ["AGENT", "RUN"], [.. rules.Agents.Select(agent => new[] { Shown(agent.Name), agent.Run ? "on" : "off" })]);
    }

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

    /// <summary>One array, one object per row, as every <c>--json</c> is: each value's rule, then each agent.</summary>
    private static void WriteJson(TextWriter writer, AgentRules rules)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            json.WriteStartObject();
            json.WriteString("kind", "details");
            json.WriteString("rule", rules.ShowsDetails ? "shown" : "hidden");
            json.WriteEndObject();

            foreach (var category in FieldCategories.Ruled.Append(FieldCategory.Passkey))
            {
                json.WriteStartObject();
                json.WriteString("kind", "category");
                json.WriteString("category", FieldCategories.Shown(category));
                json.WriteString("rule", Word(rules.RuleFor(category)));
                json.WriteEndObject();
            }

            foreach (var agent in rules.Agents)
            {
                json.WriteStartObject();
                json.WriteString("kind", "agent");
                json.WriteString("name", agent.Name);
                json.WriteString("run", agent.Run ? "on" : "off");
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static string Word(CategoryRule rule) => rule switch
    {
        CategoryRule.Allow => "allow",
        CategoryRule.Block => "block",
        _ => "ask",
    };

    /// <summary>A name from a file any program can write, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.Sanitize(text, AgentNames.MaximumLength).Text;

    private static int Malformed(CliContext context, string problem)
    {
        context.Stderr.WriteLine($"{_verb}: {problem}; fix it or delete it, which asks about every value again");
        return CliApp.ExitInternalError;
    }

    private static int Usage(CliContext context, string error)
    {
        context.Stderr.WriteLine($"{_verb}: {error}");
        context.Stderr.WriteLine($"see {_verb} --help");
        return CliApp.ExitUsageError;
    }
}
