using Keypaste.Core.Audit;
using Keypaste.Core.Editing;

namespace Keypaste.Cli;

/// <summary>How a verb that edits items reports a refused edit, and what it checks an edit against.</summary>
internal static class EditLines
{
    /// <summary>Prints the refusal after the verb and returns its exit code.</summary>
    internal static int Refuse(CliContext context, string verb, EditRefusal refusal)
    {
        context.Stderr.WriteLine($"keypaste {verb}: {refusal.Reason}");

        foreach (var line in refusal.Then)
        {
            context.Stderr.WriteLine(line);
        }

        return ExitOf(refusal.Kind);
    }

    internal static int ExitOf(EditRefusalKind kind) => kind switch
    {
        EditRefusalKind.NotFound => CliApp.ExitNotFound,
        EditRefusalKind.Conflict => CliApp.ExitInternalError,
        _ => CliApp.ExitUsageError,
    };

    /// <summary>This computer's settings and the time, which a tag change is judged by.</summary>
    internal static EditScene Scene(CliContext context) =>
        new(KeypasteHome.ClientsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)), context.Clock.GetUtcNow());

    /// <summary>Whether a person answered yes to a question asked on the terminal.</summary>
    internal static bool Confirmed(string? answer) =>
        answer is not null && (answer.Trim().StartsWith('y') || answer.Trim().StartsWith('Y'));
}
