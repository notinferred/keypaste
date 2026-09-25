using Keypaste.Cli.Styling;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Compares profiles by name: <c>keypaste env diff [project] [&lt;profileA&gt; &lt;profileB&gt;]</c>.</summary>
/// <remarks>
/// Values are compared inside <see cref="EnvMatrix"/> and never printed: a key is missing from one
/// profile, unusable in one, or holds the value another profile holds. Two profiles holding
/// different values is what profiles are for, so it is not a difference.
/// </remarks>
internal static class EnvDiffCommand
{
    private const int _keyColumn = 18;

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            return Fail(context, error);
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env diff [project] [<profileA> <profileB>]");
            context.Stdout.WriteLine();
            context.Stdout.WriteLine($"with no profiles, compares {EnvProfileNames.Default} with every other profile. names only, never values.");
            return CliApp.ExitSuccess;
        }

        var operands = line.Operands;

        if (operands.Count > 3)
        {
            return Fail(context, "expected a project and two profiles at most");
        }

        var named = operands.Count is 1 or 3 ? operands[0] : null;
        var pair = operands.Count >= 2 ? (A: operands[^2], B: operands[^1]) : ((string A, string B)?)null;

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
                    context.Stderr.WriteLine($"keypaste env diff: '{project}' has no '{absent}' profile");
                    return CliApp.ExitNotFound;
                }

                Write(context, matrix, asked.A, asked.B);
                return CliApp.ExitSuccess;
            }

            if (names.Count == 1)
            {
                context.Stdout.WriteLine($"  {Done(context)} '{Shown(project)}' has only the {EnvProfileNames.Default} profile");
                return CliApp.ExitSuccess;
            }

            foreach (var other in names.Skip(1))
            {
                context.Stdout.WriteLine(context.ConsoleStyle.Paint(
                    context.Stdout, Tone.Muted, $"  {EnvProfileNames.Default} {context.ConsoleStyle.Glyph(context.Stdout, Mark.Dot)} {other}"));
                Write(context, matrix, EnvProfileNames.Default, other);
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
