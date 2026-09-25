using Keypaste.Cli.Styling;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Replaces a secret with a new generated one: <c>keypaste rotate &lt;entry&gt;</c>.</summary>
/// <remarks>
/// <para>
/// The old value stays in the entry's history (D-0014) and the new one is never printed. It writes
/// the vault only: a key a provider issued still has to be rotated there and stored with
/// <c>keypaste set</c>.
/// </para>
/// <para>
/// It holds the vault's claim while it runs, so it is refused while the app or an agent holds the
/// vault instead of saving under it (D-0317).
/// </para>
/// </remarks>
internal static class RotateCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        .. GenerateOption.Specs.Where(spec => spec.Name != "generate"),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste rotate: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (!GenerateOption.TryRead(line, implied: true, out var recipe, out var recipeError))
        {
            context.Stderr.WriteLine($"keypaste rotate: {recipeError}");
            return CliApp.ExitUsageError;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste rotate: expected exactly one entry name");
            return CliApp.ExitUsageError;
        }

        var target = line.Operands[0];
        var slash = target.LastIndexOf('/');
        var name = new EntryName(WrittenGroup.Normalize(slash < 0 ? string.Empty : target[..slash]), target[(slash + 1)..]);
        var entryPath = name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            context.Stderr.WriteLine($"keypaste rotate: {Shown(entryPath)} is keypaste's own group; it cannot be written here");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste rotate: {locateError}");
            return CliApp.ExitUsageError;
        }

        var wanted = recipe ?? SecretRecipe.Default;

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            switch (EntryRotation.Rotate(vault, name, wanted))
            {
                case RotateOutcome.NotFound:
                    context.Stderr.WriteLine($"keypaste rotate: no entry named {Shown(entryPath)}");
                    return CliApp.ExitNotFound;

                case RotateOutcome.Reserved:
                    context.Stderr.WriteLine($"keypaste rotate: {Shown(entryPath)} is keypaste's own group; it cannot be written here");
                    return CliApp.ExitUsageError;
            }

            vault.Save();

            var style = context.ConsoleStyle;
            var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
            var dot = style.Glyph(context.Stderr, Mark.Dot);
            var size = wanted.Kind == SecretRecipeKind.Words
                ? wanted.Describe("password").Split('-')[0] + " words"
                : wanted.Describe("password").Split('-')[0] + " characters";
            context.Stderr.WriteLine($"  {done} rotated {Shown(entryPath)} {dot} {size} {dot} the old value stays in history");
            return CliApp.ExitSuccess;
        });
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste rotate <entry> [--length N | --words N] [--separator C] [--no-symbols]");
        writer.WriteLine("                       [--no-lookalikes] [--vault <path>] [--keyfile <path>]");
        writer.WriteLine();
        writer.WriteLine("replaces the entry's password with a new generated one, 20 characters unless");
        writer.WriteLine("told otherwise; the old value stays in its history and the new one is never");
        writer.WriteLine("printed. A key a provider issued still has to be rotated at the provider and");
        writer.WriteLine("stored with keypaste set.");
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;
}
