using System.CommandLine;
using System.Text;
using System.Text.Json;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste clients</c>: lists this computer's agent rules, each known agent's own rules and the answers kept for it; turns
/// an agent's run on or off, sets its own rule for a kind of value, forgets a kept Always allow or pick, and removes an agent.
/// </summary>
/// <remarks>
/// The rules live in <c>agents.toml</c> under the keypaste home, sealed with a key the vault holds, so a change goes to the
/// process holding the vault, which makes and seals it; turning run on releases nothing unasked, since every run is asked,
/// so it is made without a confirmation. A terminal proves no person agreed, so a set or a forget releases nothing more
/// unasked (<see cref="AgentRules.Narrowing"/>, <see cref="AgentRules.ForgettingKey"/>). With nothing holding the vault only
/// a change that gives less can be made, and it is written unsealed, which leaves every agent at the defaults until the
/// owner sets the rules again.
/// </remarks>
internal static class ClientsCommand
{
    private const string _verb = "keypaste clients";
    private const string _noAnswer = "the keypaste process holding the vault did not answer; it may be older than this keypaste";

    private static readonly TimeSpan _changeBound = TimeSpan.FromSeconds(10);

    private static readonly Argument<string?> _agent = Syntax.Optional("agent", "the agent to change, by its bridge's --client-label");
    private static readonly Option<string> _run = Syntax.Value("--run", "on|off", "whether it may ask to run commands, each run asked");
    private static readonly Option<bool> _remove = Syntax.Flag("--remove", "forget the agent, which turns its run off");
    private static readonly Option<string> _set = Syntax.Value("--set", $"<value>={AgentRuleTarget.Rules}", "set its own rule for one kind of value: password, custom, variable, totp, notes or attachment");
    private static readonly Option<string> _forget = Syntax.Value("--forget", "<key>", "forget one Always allow or pick kept for it, by the key the list shows");

    internal static CliVerb Command { get; } = new(
        "clients",
        "list agents, their rules and kept answers, and change them for less\n\n"
            + "lists the rules every agent's request is decided by on this computer, kept in agents.toml, each agent it "
            + "knows by the name its bridge was given with --client-label, its own rules and the answers kept for it. with an "
            + "agent's name, turns its run on or off, sets its rule for a kind of value to ask, block or what Settings sets, "
            + "forgets an Always allow or pick, or removes it. each run is still asked; an Allow, and removing an Always block, "
            + "are made in the app.",
        Execute)
    {
        _agent, _run, _remove, _set, _forget, CliOptions.Json, CliOptions.Vault, CliOptions.Approver,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var file = new AgentRulesFile(KeypasteHome.AgentsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)));
        var run = line.GetValue(_run);
        var remove = line.GetValue(_remove);
        var set = line.GetValue(_set);
        var forget = line.GetValue(_forget);
        var changes = (run is null ? 0 : 1) + (remove ? 1 : 0) + (set is null ? 0 : 1) + (forget is null ? 0 : 1);

        if (line.GetValue(_agent) is not { } agent)
        {
            return changes > 0 ? Usage(context, "name the agent to change") : List(line, file, context);
        }

        if (line.GetValue(CliOptions.Json))
        {
            return Usage(context, "give one agent's name and what to change, or nothing to list them");
        }

        if (!AgentNames.IsNamed(agent))
        {
            return Usage(context, $"an agent's name is its bridge's --client-label: 1 to {AgentNames.MaximumLength} characters with no quote, backslash, slash or control character");
        }

        if (changes != 1)
        {
            return Usage(context, "give one of --run on, --run off, --set, --forget or --remove");
        }

        if (set is not null && !AgentRuleTarget.TryParse(set, out _, out _))
        {
            return Usage(context, $"--set is a kind of value and {AgentRuleTarget.Rules}, such as password=block");
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

        var (kind, target, what) = (remove, set, forget) switch
        {
            (true, _, _) => (RulesChangeKind.Remove, string.Empty, "removed; its run is off"),
            (_, { } rule, _) => (RulesChangeKind.Set, rule, $"{rule} set"),
            (_, _, { } key) => (RulesChangeKind.Forget, key, $"{key} forgotten"),
            _ when run == "on" => (RulesChangeKind.RunOn, string.Empty, "run on; each run is still asked"),
            _ => (RulesChangeKind.RunOff, string.Empty, "run off"),
        };

        if (OwnerAsync(line, agent, kind, target, what, context).GetAwaiter().GetResult() is { } answered)
        {
            return answered;
        }

        // Applied here only when nothing holds the vault: the owner's sealed rules, not this unverified copy, decide otherwise.
        AgentRules changed;

        try
        {
            changed = remove ? rules.Without(agent)
                : set is not null ? AgentRuleTarget.Apply(rules, agent, set)
                : forget is not null ? rules.ForgettingKey(agent, forget)
                : rules.WithRun(agent, run == "on");
        }
        catch (ArgumentException refused)
        {
            context.Stderr.WriteLine($"{_verb}: {refused.Message}");
            return CliApp.ExitUsageError;
        }

        if (kind == RulesChangeKind.RunOn)
        {
            context.Stderr.WriteLine(
                $"{_verb}: nothing holds the vault unlocked, and only the process holding it can seal a change that lets an agent run commands. "
                + "Unlock the vault in the keypaste app or run `keypaste approve`, then run this again.");
            return CliApp.ExitInternalError;
        }

        if (!file.TryWrite(changed, seal: null, out problem))
        {
            context.Stderr.WriteLine($"{_verb}: nothing was changed: {problem}");
            return CliApp.ExitInternalError;
        }

        const string unsealed = "written unsealed, since nothing holds the vault, so every agent gets the defaults, every value asked and no runs, until the rules are set again where the vault is unlocked";
        Record(context, agent, $"{what}; {unsealed}");
        Done(context, agent, $"{what}; {unsealed}");
        return CliApp.ExitSuccess;
    }

    /// <summary>Asks the process holding the vault to make and seal the change; null when nothing holds it.</summary>
    private static async Task<int?> OwnerAsync(ParseResult line, string agent, RulesChangeKind change, string target, string what, CliContext context)
    {
        if (!VaultLocator.TryResolve(line, context.Environment, out var vault, out _) || !SessionPipe.TryResolve(line, vault, context, out var pipe, out _))
        {
            return null;
        }

        using var bound = new CancellationTokenSource(_changeBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            if (attachment.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"{_verb}: {EntryNameSanitizer.SanitizeProse(refusal, 512).Text}");
                return CliApp.ExitInternalError;
            }

            return null;
        }

        var reply = await client.ChangeRulesAsync(new RulesChangeRequest(agent, change) { Vault = vault, Session = session, Target = target }, bound.Token);

        if (reply is null)
        {
            context.Stderr.WriteLine($"{_verb}: {_noAnswer}");
            return CliApp.ExitInternalError;
        }

        if (!reply.Changed)
        {
            context.Stderr.WriteLine($"{_verb}: {EntryNameSanitizer.SanitizeProse(reply.Reason, 512).Text}");
            return CliApp.ExitInternalError;
        }

        Done(context, agent, $"{what}; sealed by the process holding the vault");
        return CliApp.ExitSuccess;
    }

    private static int List(ParseResult line, AgentRulesFile file, CliContext context)
    {
        if (!file.TryReadFile(out var read, out var problem))
        {
            return Malformed(context, problem);
        }

        var rules = read.Rules;

        if (read is { Present: true, Seal: null })
        {
            context.Stderr.WriteLine($"{_verb}: {file.Path} is not sealed, so keypaste ignores it and every agent gets the defaults until the rules are set again where the vault is unlocked");
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
                [FieldCategories.Shown(FieldCategory.Url), "shown"],
                [FieldCategories.Shown(FieldCategory.Username), rules.ShowsUsername ? "shown" : "hidden"],
                .. FieldCategories.Ruled.Select(category => new[] { FieldCategories.Shown(category), Word(rules.RuleFor(category)) }),
                [FieldCategories.Shown(FieldCategory.Passkey), Word(CategoryRule.Block)],
            ]);

        writer.WriteLine();
        Table(writer, style, ["AGENT", "RUN"], [.. rules.Agents.Select(agent => new[] { Shown(agent.Name), agent.Run ? "on" : "off" })]);

        if (rules.AgentCategories.Count > 0)
        {
            writer.WriteLine();
            Table(
                writer,
                style,
                ["AGENT", "VALUE", "ITS OWN RULE"],
                [.. rules.AgentCategories.Select(own => new[] { Shown(own.Agent), FieldCategories.Key(own.Category), Word(own.Rule) })]);
        }

        if (rules.Answers.Count + rules.Picks.Count > 0)
        {
            writer.WriteLine();
            Table(
                writer,
                style,
                ["AGENT", "KEY", "KEPT ANSWER"],
                [
                    .. rules.Answers.Select(answer => new[]
                    {
                        Shown(answer.Agent), AgentRules.KeyOf(answer),
                        $"{(answer.Rule == CategoryRule.Allow ? "always allows" : "always blocks")} {Field(answer.Field)} of item {answer.Item}",
                    }),
                    .. rules.Picks.Select(pick => new[]
                    {
                        Shown(pick.Agent), AgentRules.KeyOf(pick),
                        $"always picks item {pick.Item} for {Field(pick.Field)} of {DisplayTextSanitizer.Sanitize(pick.Name).Text}",
                    }),
                ]);
        }
    }

    /// <summary>A kept answer's field, scrubbed, since an agent's request named it.</summary>
    private static string Field(string field) => EntryNameSanitizer.Sanitize(field).Text;

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
            json.WriteString("kind", "username");
            json.WriteString("rule", rules.ShowsUsername ? "shown" : "hidden");
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

            foreach (var own in rules.AgentCategories)
            {
                json.WriteStartObject();
                json.WriteString("kind", "agent-rule");
                json.WriteString("agent", own.Agent);
                json.WriteString("value", FieldCategories.Key(own.Category));
                json.WriteString("rule", Word(own.Rule));
                json.WriteEndObject();
            }

            foreach (var answer in rules.Answers)
            {
                json.WriteStartObject();
                json.WriteString("kind", "answer");
                json.WriteString("agent", answer.Agent);
                json.WriteString("key", AgentRules.KeyOf(answer));
                json.WriteString("item", answer.Item);
                json.WriteString("field", answer.Field);
                json.WriteString("rule", Word(answer.Rule));
                json.WriteEndObject();
            }

            foreach (var pick in rules.Picks)
            {
                json.WriteStartObject();
                json.WriteString("kind", "pick");
                json.WriteString("agent", pick.Agent);
                json.WriteString("key", AgentRules.KeyOf(pick));
                json.WriteString("name", pick.Name);
                json.WriteString("field", pick.Field);
                json.WriteString("item", pick.Item);
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
