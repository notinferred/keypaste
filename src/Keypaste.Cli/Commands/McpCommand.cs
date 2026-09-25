using System.Text.Json;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste mcp serve</c>, <c>setup</c> and <c>policy</c>: the agent verbs under the name the MCP world looks for.</summary>
/// <remarks>
/// <c>serve</c> and <c>setup</c> are aliases: the MCP server itself is <c>keypaste-mcp</c>, which the
/// client starts, and <c>serve</c> is the terminal approver it talks to. <c>policy</c> reads and writes
/// <c>clients.toml</c>, which the owner reads at each request, so it needs no vault and no owner (D-0360).
/// </remarks>
internal static class McpCommand
{
    private const string _policyVerb = "keypaste mcp policy";

    private static readonly OptionSpec[] _policyOptions = [new(CliJson.Option, TakesValue: false)];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        switch (args.Length >= 2 ? args[1] : null)
        {
            case "serve":
                return AgentCommand.Execute(["agent", .. args[2..]], context);

            case "setup":
                return SetupCommand.Execute(["setup", .. args[2..]], context);

            case "policy":
                return Policy(args, context);

            case "-h" or "--help" or "help":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste mcp <serve|setup|policy> [options]");
        writer.WriteLine("  serve   approve agents' requests in this terminal (same as keypaste agent)");
        writer.WriteLine("  setup   point the AI clients on this machine at your vault (same as keypaste setup)");
        writer.WriteLine("  policy  how each MCP client is asked: keypaste mcp policy [<label> <session|ask|inject-only>]");
        writer.WriteLine("the MCP server itself is keypaste-mcp; your MCP client starts it.");
    }

    private static int Policy(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _policyOptions, out var line, out var error))
        {
            return Usage(context, error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        var path = KeypasteHome.ClientsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (line.Operands.Count == 0)
        {
            if (!ClientPolicies.TryLoad(path, out var policies, out var problem))
            {
                return Malformed(context, path, problem);
            }

            if (line.HasFlag(CliJson.Option))
            {
                CliJson.WriteArray(context.Stdout, policies.Rows, WriteJson);
            }
            else
            {
                WriteTable(policies, context);
            }

            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 2 || line.HasFlag(CliJson.Option))
        {
            return Usage(context, "give a label and a policy, or nothing to list them");
        }

        var label = line.Operands[0];

        if (!ClientPolicies.IsValidLabel(label, out var labelError))
        {
            return Usage(context, labelError);
        }

        if (!ClientPolicies.TryParseWire(line.Operands[1], out var policy))
        {
            return Usage(context, "a policy is session, ask or inject-only");
        }

        if (!ClientPolicies.TryLoad(path, out var current, out var loadProblem))
        {
            return Malformed(context, path, loadProblem);
        }

        if (!ClientPolicies.TrySave(path, current.With(label, policy), out var saveError))
        {
            context.Stderr.WriteLine($"{_policyVerb}: {path} could not be written: {saveError}");
            return CliApp.ExitInternalError;
        }

        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        var dot = style.Glyph(context.Stderr, Mark.Dot);
        context.Stderr.WriteLine($"  {done} {label} {dot} {ClientPolicies.Describe(policy)} {dot} applies to its next request");
        return CliApp.ExitSuccess;
    }

    private static void WriteTable(ClientPolicies policies, CliContext context)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;
        List<(string Label, string Policy)> rows = [.. policies.Rows.Select(row => (row.Label, ClientPolicies.Describe(row.Policy)))];

        if (!policies.Rows.Any(row => string.Equals(row.Label, ClientPolicies.AnyClient, StringComparison.Ordinal)))
        {
            rows.Add((ClientPolicies.AnyClient, ClientPolicies.Describe(ClientPolicy.SessionGrants) + "   (default)"));
        }

        var width = Math.Max("CLIENT".Length, rows.Max(row => row.Label.Length)) + 2;

        writer.WriteLine(style.Paint(writer, Tone.Muted, "  " + "CLIENT".PadRight(width) + "POLICY"));

        foreach (var (label, policy) in rows)
        {
            writer.WriteLine("  " + label.PadRight(width) + policy);
        }
    }

    private static void WriteJson(Utf8JsonWriter json, ClientPolicyRow row)
    {
        json.WriteString("label", row.Label);
        json.WriteString("policy", ClientPolicies.Wire(row.Policy));
    }

    private static int Malformed(CliContext context, string path, string problem)
    {
        context.Stderr.WriteLine($"{_policyVerb}: {path}: {problem}; fix it or delete it");
        return CliApp.ExitInternalError;
    }

    private static int Usage(CliContext context, string error)
    {
        context.Stderr.WriteLine($"{_policyVerb}: {error}");
        WriteUsage(context.Stderr);
        return CliApp.ExitUsageError;
    }
}
