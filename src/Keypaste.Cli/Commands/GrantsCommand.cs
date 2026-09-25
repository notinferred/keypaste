using System.Globalization;
using System.Text.Json;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste grants</c> and <c>keypaste grants revoke</c>: the time-boxed access the process holding
/// the vault has given, listed and ended over its endpoint (D-0351).
/// </summary>
/// <remarks>
/// Names only, never a value: the owner's reply has no member for one. Everything it sends is another
/// process's text, so the table draws it sanitized and <c>--json</c> leaves escaping to the encoder.
/// </remarks>
internal static class GrantsCommand
{
    private const string _allOption = "all";

    private const string _noAnswer =
        "the keypaste process holding the vault did not answer; it may be older than this keypaste";

    /// <summary>The widest entry drawn before it is cut.</summary>
    private const int _entryWidth = 40;

    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(10);

    private static readonly OptionSpec[] _listOptions =
    [
        new("vault", TakesValue: true),
        new(SessionPipe.ApproverOption, TakesValue: true),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _revokeOptions =
    [
        new("vault", TakesValue: true),
        new(SessionPipe.ApproverOption, TakesValue: true),
        new(_allOption, TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        return args.Length >= 2 && string.Equals(args[1], "revoke", StringComparison.Ordinal)
            ? Revoke(args, context)
            : List(args, context);
    }

    private static int List(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _listOptions, out var line, out var error))
        {
            return Usage(context, "keypaste grants", error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            return Usage(context, "keypaste grants", $"unexpected argument '{line.Operands[0]}'");
        }

        if (!TryPipe(line, context, "keypaste grants", out var vault, out var pipe))
        {
            return CliApp.ExitUsageError;
        }

        return ListAsync(vault, pipe, line.HasFlag(CliJson.Option), context).GetAwaiter().GetResult();
    }

    private static async Task<int> ListAsync(string vault, string pipe, bool json, CliContext context)
    {
        using var bound = new CancellationTokenSource(_answerBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            if (attachment.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"keypaste grants: {Shown(refusal)}");
                return CliApp.ExitInternalError;
            }

            if (json)
            {
                context.Stdout.WriteLine("[]");
            }

            context.Stderr.WriteLine($"keypaste grants: nothing holds {vault} unlocked, so nothing is granted");
            return CliApp.ExitSuccess;
        }

        var reply = await client.GrantsAsync(new GrantsRequest { Vault = vault, Session = session }, bound.Token);

        if (reply is null)
        {
            context.Stderr.WriteLine($"keypaste grants: {_noAnswer}");
            return CliApp.ExitInternalError;
        }

        if (!reply.Answered)
        {
            context.Stderr.WriteLine($"keypaste grants: {Shown(reply.Reason)}");
            return CliApp.ExitInternalError;
        }

        if (json)
        {
            CliJson.WriteArray(context.Stdout, reply.Grants, WriteJson);
        }
        else
        {
            WriteTable(reply.Grants, context);
        }

        if (!reply.Complete)
        {
            context.Stderr.WriteLine(
                $"keypaste grants: more grants are in force than one reply holds; these are the first {reply.Grants.Count}");
        }

        return CliApp.ExitSuccess;
    }

    private static void WriteJson(Utf8JsonWriter json, GrantSummary grant)
    {
        json.WriteString("id", grant.Id);
        json.WriteString("kind", grant.Kind);
        json.WriteString("agent", grant.Client);
        json.WriteString("scope", grant.Scope);
        json.WriteString("field", grant.Field);
        json.WriteNumber("seconds_left", grant.SecondsLeft);
    }

    private static void WriteTable(IReadOnlyList<GrantSummary> grants, CliContext context)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;

        if (grants.Count == 0)
        {
            writer.WriteLine(style.Paint(writer, Tone.Muted, "  no active grants"));
            return;
        }

        string[] headings = ["ID", "AGENT", "ENTRY", "FIELD", "LEFT"];
        var rows = grants.Select(grant => new[]
        {
            EntryNameSanitizer.Sanitize(grant.Id, GrantId.Length).Text,
            EntryNameSanitizer.Sanitize(grant.Client, ApprovalPrompt.MaximumClientLength).Text,
            Cut(EntryNameSanitizer.SanitizeProse(grant.Scope, 512).Text),
            EntryNameSanitizer.Sanitize(grant.Field, 32).Text,
            Left(grant.SecondsLeft),
        }).ToList();

        var widths = headings[..^1]
            .Select((heading, column) => Math.Max(heading.Length, rows.Max(row => row[column].Length)))
            .ToArray();

        string Line(string[] cells) =>
            "  " + string.Concat(widths.Select((width, column) => cells[column].PadRight(width) + "  "));

        writer.WriteLine(style.Paint(writer, Tone.Muted, Line(headings) + headings[^1]));

        foreach (var row in rows)
        {
            writer.WriteLine(Line(row) + style.Paint(writer, Tone.Accent, row[^1]));
        }
    }

    private static int Revoke(string[] args, CliContext context)
    {
        const string verb = "keypaste grants revoke";

        if (!CommandLine.TryParse(args, 2, _revokeOptions, out var line, out var error))
        {
            return Usage(context, verb, error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        var all = line.HasFlag(_allOption);

        if (all ? line.Operands.Count != 0 : line.Operands.Count != 1)
        {
            return Usage(context, verb, all
                ? "give --all or one id or agent, not both"
                : "expected one grant id or agent name, or --all");
        }

        if (!TryPipe(line, context, verb, out var vault, out var pipe))
        {
            return CliApp.ExitUsageError;
        }

        var target = all ? null : line.Operands[0];
        var request = target is null
            ? new RevokeGrantsRequest([], null, All: true)
            : GrantId.IsId(target)
                ? new RevokeGrantsRequest([target.ToLowerInvariant()], null, All: false)
                : new RevokeGrantsRequest([], target, All: false);

        return RevokeAsync(vault, pipe, request, target, context).GetAwaiter().GetResult();
    }

    private static async Task<int> RevokeAsync(
        string vault,
        string pipe,
        RevokeGrantsRequest request,
        string? target,
        CliContext context)
    {
        using var bound = new CancellationTokenSource(_answerBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            context.Stderr.WriteLine(attachment.Refusal is { } refusal
                ? $"keypaste grants revoke: {Shown(refusal)}"
                : $"keypaste grants revoke: nothing holds {vault} unlocked, so nothing is granted");
            return attachment.Refusal is null ? CliApp.ExitSuccess : CliApp.ExitInternalError;
        }

        var reply = await client.RevokeGrantsAsync(request with { Vault = vault, Session = session }, bound.Token);

        if (reply is null)
        {
            context.Stderr.WriteLine($"keypaste grants revoke: {_noAnswer}");
            return CliApp.ExitInternalError;
        }

        if (reply.Reason.Length > 0)
        {
            context.Stderr.WriteLine($"keypaste grants revoke: {Shown(reply.Reason)}");
            return CliApp.ExitInternalError;
        }

        if (reply.Revoked == 0)
        {
            if (target is null)
            {
                context.Stderr.WriteLine(context.ConsoleStyle.Paint(context.Stderr, Tone.Muted, "  no active grants"));
                return CliApp.ExitSuccess;
            }

            context.Stderr.WriteLine(
                $"keypaste grants revoke: nothing matched '{EntryNameSanitizer.Sanitize(target, 64).Text}'; nothing was revoked");
            return CliApp.ExitNotFound;
        }

        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"  {done} revoked {reply.Revoked} grant{(reply.Revoked == 1 ? string.Empty : "s")}"));

        return CliApp.ExitSuccess;
    }

    /// <summary>How long a grant has left, in the largest unit that says it: <c>45s</c>, <c>42m</c>, <c>1h 5m</c>.</summary>
    internal static string Left(int seconds) => seconds switch
    {
        < 60 => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, seconds)}s"),
        < 3600 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}m"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}h {seconds % 3600 / 60}m"),
    };

    private static string Cut(string text) =>
        text.Length <= _entryWidth ? text : text[..(_entryWidth - 1)] + "…";

    private static bool TryPipe(CommandLine line, CliContext context, string verb, out string vault, out string pipe)
    {
        pipe = string.Empty;

        if (!VaultLocator.TryResolve(line, context.Environment, out vault, out var locateError))
        {
            context.Stderr.WriteLine($"{verb}: {locateError}");
            return false;
        }

        if (!SessionPipe.TryResolve(line, vault, context, out pipe, out var pipeError))
        {
            context.Stderr.WriteLine($"{verb}: {pipeError}");
            return false;
        }

        return true;
    }

    /// <summary>Another process's words, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    private static int Usage(CliContext context, string verb, string error)
    {
        context.Stderr.WriteLine($"{verb}: {error}");
        WriteUsage(context.Stderr);
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste grants [--json] [--vault <path>] [--approver <name>]");
        writer.WriteLine("       keypaste grants revoke (<id> | <agent> | --all) [--vault <path>] [--approver <name>]");
        writer.WriteLine();
        writer.WriteLine("lists the time-boxed access the keypaste app or `keypaste agent` holding the vault");
        writer.WriteLine("has given, names only, and ends it: one grant by its id, every grant an agent");
        writer.WriteLine("holds by its name, or all of them.");
    }
}
