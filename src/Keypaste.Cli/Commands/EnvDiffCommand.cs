using System.CommandLine;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Compares environments by name: <c>keypaste env diff [project] [&lt;environmentA&gt; &lt;environmentB&gt;]</c>.</summary>
/// <remarks>
/// Values are compared inside <see cref="EnvMatrix"/> and never printed: a key is missing from one
/// environment, unusable in one, or holds the value another environment holds. Two environments
/// holding different values is what environments are for, so it is not a difference.
/// </remarks>
internal static class EnvDiffCommand
{
    private const int _keyColumn = 18;

    private static readonly Argument<string?> _first = Syntax.Optional("project", "the project, or the one projects.json maps this directory to");
    private static readonly Argument<string?> _second = Syntax.Optional("environment", "an environment to compare");
    private static readonly Argument<string?> _third = Syntax.Optional("other", "the environment to compare it with");

    internal static CliVerb Command { get; } = new(
        "diff",
        "compare environments by name, never by value\n\n"
            + $"with no environments, compares {EnvProfileNames.Default} with every other environment.",
        Execute)
    {
        _first, _second, _third, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        string[] operands = [.. new[] { line.GetValue(_first), line.GetValue(_second), line.GetValue(_third) }.OfType<string>()];

        var named = operands.Length is 1 or 3 ? operands[0] : null;
        var pair = operands.Length >= 2 ? (A: operands[^2], B: operands[^1]) : ((string A, string B)?)null;

        if (pair is { } profiles
            && (!EnvProfileNames.IsValid(profiles.A, out var invalid) || !EnvProfileNames.IsValid(profiles.B, out invalid)))
        {
            return Fail(context, invalid);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, locateError);
        }

        string project;

        if (named is not null)
        {
            project = named;
        }
        else if (EnvCommand.TryInferProject(context, path, out var inferred, out var inferError))
        {
            project = inferred;
        }
        else
        {
            return Fail(context, inferError);
        }

        return VaultSession.Open(path, line, context, vault =>
        {
            var matrix = EnvMatrix.Build(vault, project, context.Clock);

            if (matrix.Profiles.Count == 0)
            {
                context.Stderr.WriteLine($"keypaste env diff: no env set for '{project}'");
                return CliApp.ExitNotFound;
            }

            var names = matrix.Profiles.Select(profile => profile.Name).ToList();

            if (pair is { } asked)
            {
                if (new[] { asked.A, asked.B }.FirstOrDefault(profile => !names.Contains(profile, StringComparer.Ordinal)) is { } absent)
                {
                    context.Stderr.WriteLine($"keypaste env diff: '{project}' has no '{absent}' environment");
                    return CliApp.ExitNotFound;
                }

                Write(context, matrix, asked.A, asked.B);
                return CliApp.ExitSuccess;
            }

            // dev comes first when the project has it, which it need not.
            var first = names[0];

            if (names.Count == 1)
            {
                context.Stdout.WriteLine($"  {Done(context)} '{Shown(project)}' has only the {first} environment");
                return CliApp.ExitSuccess;
            }

            foreach (var other in names.Skip(1))
            {
                context.Stdout.WriteLine(context.ConsoleStyle.Paint(
                    context.Stdout, Tone.Muted, $"  {first} {context.ConsoleStyle.Glyph(context.Stdout, Mark.Dot)} {other}"));
                Write(context, matrix, first, other);
            }

            return CliApp.ExitSuccess;
        });
    }

    private static void Write(CliContext context, EnvMatrix matrix, string a, string b)
    {
        var lines = EnvDiff.Compare(matrix, a, b);

        if (lines.Count == 0)
        {
            context.Stdout.WriteLine($"  {Done(context)} {a} and {b} have the same keys");
            return;
        }

        var style = context.ConsoleStyle;

        foreach (var difference in lines)
        {
            var (mark, tone, text) = difference.Kind switch
            {
                EnvDiffKind.Missing => ("-", Tone.Danger, $"missing in {difference.Profile}"),
                EnvDiffKind.Unusable => ("~", Tone.Accent, $"{Shown(difference.Detail ?? string.Empty)} in {difference.Profile}"),
                _ => ("=", Tone.Accent, $"same value in {difference.Profile} and {difference.Detail}"),
            };

            var key = EntryNameSanitizer.Sanitize(difference.Key, 512).Text;
            context.Stdout.WriteLine($"  {style.Paint(context.Stdout, tone, mark)} {key.PadRight(_keyColumn - 1)} {text}");
        }
    }

    private static string Done(CliContext context) =>
        context.ConsoleStyle.Paint(context.Stdout, Tone.Ok, context.ConsoleStyle.Glyph(context.Stdout, Mark.Done));

    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste env diff: {message}");
        return CliApp.ExitUsageError;
    }
}
