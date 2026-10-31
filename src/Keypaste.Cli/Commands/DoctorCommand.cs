using System.CommandLine;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Clients;
using Keypaste.Core.Health;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste doctor</c>: says whether this keypaste and its Argon2, the vault, the process holding it, each AI tool's
/// registration, keypaste's settings files and PATH work together, and what to fix (D-0467, D-0525).
/// </summary>
/// <remarks>
/// It only reads, through <see cref="HealthCheck"/>. A finding can quote another program or a path from an AI tool's
/// file, so each line is made safe for the terminal before it is printed; <c>--json</c> carries the text as it was read,
/// with every control character escaped by the encoder.
/// </remarks>
internal static class DoctorCommand
{
    private const int _subjectWidth = 16;

    internal static CliVerb Command { get; } = new(
        "doctor",
        "check the vault, the AI tools and the app, and say what to fix\n\n"
            + "checks, reading only, that this keypaste and its Argon2, your vault, the keypaste app or `keypaste approve` "
            + "holding it, your AI tools' connections, keypaste's settings files and PATH work together. it opens no "
            + "vault, asks for no password and changes nothing.\n\n"
            + $"exits {CliApp.ExitSuccess} when nothing needs fixing and {CliApp.ExitNeedsFixing} when something does.",
        Execute)
    {
        CliOptions.Json, CliOptions.Vault,
    };

    /// <summary>Where a finding's path and fix start: under its text, past the margin, the mark and the subject.</summary>
    private static readonly string _underText = new(' ', 4 + _subjectWidth + 1);

    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var scope = new HealthScope(
            McpClientMachine.From(context.Environment.Get, McpClientMachine.CurrentPlatform),
            context.Environment.Get,
            line.GetValue(CliOptions.Vault),
            context.ProcessPath,
            context.AppImage());
        var findings = HealthCheck.RunAsync(scope, context.ProcessRunner, CancellationToken.None).GetAwaiter().GetResult();

        if (line.GetValue(CliOptions.Json))
        {
            WriteJson(context.Stdout, findings);
        }
        else
        {
            WriteReport(context, findings);
        }

        return findings.Any(finding => finding.Status == HealthStatus.Fix) ? CliApp.ExitNeedsFixing : CliApp.ExitSuccess;
    }

    private static void WriteReport(CliContext context, IReadOnlyList<HealthFinding> findings)
    {
        var writer = context.Stdout;
        var style = context.ConsoleStyle;
        var prompt = style.Paint(writer, Tone.Accent, style.Glyph(writer, Mark.Prompt));

        foreach (var finding in findings)
        {
            writer.WriteLine($"  {Marked(writer, style, finding.Status)} {finding.Subject.PadRight(_subjectWidth)} {Shown(finding.Says)}");

            if (finding.Where is { } where)
            {
                writer.WriteLine(_underText + style.Paint(writer, Tone.Muted, Shown(where)));
            }

            if (finding.Fix is { } fix)
            {
                writer.WriteLine($"{_underText}{prompt} {Shown(fix)}");
            }
        }

        var fixes = findings.Count(finding => finding.Status == HealthStatus.Fix);
        writer.WriteLine();
        writer.WriteLine(fixes == 0 ? "  nothing to fix" : $"  {fixes} to fix");
    }

    private static void WriteJson(TextWriter writer, IReadOnlyList<HealthFinding> findings) =>
        CliJson.WriteArray(writer, findings, (json, finding) =>
        {
            json.WriteString("area", Named(finding.Area));
            json.WriteString("subject", finding.Subject);
            json.WriteString("status", Named(finding.Status));
            json.WriteString("says", finding.Says);
            json.WriteString("where", finding.Where);
            json.WriteString("fix", finding.Fix);
        });

    private static string Marked(TextWriter writer, IConsoleStyle style, HealthStatus status) => status switch
    {
        HealthStatus.Ok => style.Paint(writer, Tone.Ok, style.Glyph(writer, Mark.Done)),
        HealthStatus.Fix => style.Paint(writer, Tone.Danger, ConsoleMarks.Carried(writer, "✗", "x")),
        _ => style.Paint(writer, Tone.Muted, style.Glyph(writer, Mark.Dot)),
    };

    private static string Named(HealthArea area) => area switch
    {
        HealthArea.Versions => "versions",
        HealthArea.Argon2 => "argon2",
        HealthArea.Vault => "vault",
        HealthArea.Tool => "tool",
        HealthArea.Owner => "owner",
        HealthArea.Settings => "settings",
        HealthArea.OnPath => "path",
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    private static string Named(HealthStatus status) => status switch
    {
        HealthStatus.Ok => "ok",
        HealthStatus.Fix => "fix",
        HealthStatus.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // Not EntryNameSanitizer, which would blank a fix's backticks and a Windows path's backslashes: only what repaints or breaks the line goes.
    internal static string Shown(string text, int maximumLength = DisplayTextSanitizer.MaximumLength) =>
        DisplayTextSanitizer.Sanitize(text, maximumLength).Text.Replace('\n', ' ').Replace('\t', ' ');
}
