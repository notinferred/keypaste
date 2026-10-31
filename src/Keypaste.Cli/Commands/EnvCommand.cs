using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Projects;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>The environment-variable verbs: <c>keypaste env &lt;command&gt;</c>.</summary>
internal static class EnvCommand
{
    internal static Verb Command { get; } = new(
        "env",
        "import, export and diff .env environments\n\n"
            + "a variable is a field of an item tagged env:<project> (the dev environment) or env:<project>:<environment>, "
            + "editable in KeePassXC. set and pull write a new one on --entry, or on the environment's home item "
            + "env/<project>/.env, created tagged. to read a value: `keypaste get <item> --field <KEY> --reveal`")
    {
        EnvListCommand.Command, EnvSetCommand.Command, EnvRemoveCommand.Command, EnvPullCommand.Command,
        EnvExportCommand.Command, EnvDiffCommand.Command, EnvTagCommand.Tag, EnvTagCommand.Untag,
    };

    /// <summary>The profile a command line names, <c>dev</c> when it names none.</summary>
    /// <returns>False with the reason when the name is not one keypaste resolves.</returns>
    internal static bool TryProfile(ParseResult line, out string profile, out string error)
    {
        profile = line.GetValue(CliOptions.Profile) ?? EnvProfileNames.Default;
        return EnvProfileNames.IsValid(profile, out error);
    }

    /// <summary>The project <c>projects.json</c> maps the working directory to, for one vault.</summary>
    /// <param name="context">Where keypaste's home and the working directory come from.</param>
    /// <param name="vaultPath">The vault in use.</param>
    /// <param name="project">The project, when this returns true.</param>
    /// <param name="error">Why none, in words the verb prefixes.</param>
    /// <param name="notMapped">What to add when no mapping covers the directory.</param>
    /// <returns>False when there is not exactly one.</returns>
    internal static bool TryInferProject(
        CliContext context,
        string vaultPath,
        [NotNullWhen(true)] out string? project,
        out string error,
        string notMapped = NameAProject)
    {
        project = null;
        var path = KeypasteHome.ProjectsPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!ProjectMappings.TryLoad(path, out var mappings))
        {
            error = $"{path} could not be read, so no project could be found for this directory; name one";
            return false;
        }

        if (ProjectInference.TryInfer(mappings, vaultPath, context.WorkingDirectory, out project, out error))
        {
            return true;
        }

        if (string.Equals(error, ProjectInference.NotMapped, StringComparison.Ordinal))
        {
            error = $"{error}; {notMapped}";
        }

        return false;
    }

    /// <summary>What a verb that could not infer a project asks for.</summary>
    internal const string NameAProject = "name a project, as in: keypaste run -p dev acme-api -- npm start";
}
