using System.Text.Json;

namespace Keypaste.Core.Projects;

/// <summary>Where a project's command runs on this machine, and which env set it runs with.</summary>
/// <param name="Vault">The vault file holding the set, as a full path.</param>
/// <param name="Project">The project, whose set is <c>env/&lt;project&gt;</c>.</param>
/// <param name="Directory">The working directory, as a full path.</param>
/// <param name="Command">The line a person would type to run it.</param>
public sealed record ProjectMapping(string Vault, string Project, string Directory, string Command);

/// <summary>
/// The desktop app's project mappings, in <c>projects.json</c> under keypaste's home (D-0339).
/// </summary>
/// <remarks>
/// <para>
/// A mapping names a directory and a command, both belonging to this machine, so it stays here
/// rather than travelling in the vault. It has no field a value could be written to.
/// </para>
/// <para>
/// JSON rather than TOML because keypaste's TOML reader carries no escapes, and a Windows command
/// needs quotes around any path with a space in it.
/// </para>
/// </remarks>
public static class ProjectMappings
{
    /// <summary>The largest file read, in bytes.</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>The longest command accepted, in characters.</summary>
    public const int MaximumCommandLength = 4096;

    /// <summary>Reads the mappings.</summary>
    /// <param name="path">The file, from <see cref="Audit.KeypasteHome.ProjectsPath"/>.</param>
    /// <param name="mappings">Every well-formed mapping, or none when this returns false.</param>
    /// <returns>False when the file exists and could not be read, which <see cref="Save"/> must then not replace.</returns>
    public static bool TryLoad(string path, out IReadOnlyList<ProjectMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(path);

        mappings = [];

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length > MaximumBytes)
            {
                return false;
            }

            using var document = JsonDocument.Parse(bytes);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("projects", out var projects)
                || projects.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            List<ProjectMapping> read = [];

            foreach (var item in projects.EnumerateArray())
            {
                if (Text(item, "vault") is { } vault
                    && Text(item, "project") is { } project
                    && Text(item, "directory") is { } directory
                    && Text(item, "command") is { } command
                    && Path.IsPathFullyQualified(vault)
                    && IsUsable(directory, command, out _))
                {
                    read.Add(new ProjectMapping(vault, project, directory, command));
                }
            }

            mappings = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>The mapping for one project of one vault, or null.</summary>
    /// <param name="mappings">The mappings.</param>
    /// <param name="vault">The vault file.</param>
    /// <param name="project">The project.</param>
    /// <returns>The mapping, or null.</returns>
    public static ProjectMapping? Find(IReadOnlyList<ProjectMapping> mappings, string vault, string project)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);

        var full = Path.GetFullPath(vault);
        return mappings.FirstOrDefault(mapping => Same(mapping, full, project));
    }

    /// <summary>The mappings with <paramref name="mapping"/> in place of any for the same vault and project.</summary>
    /// <param name="mappings">The mappings.</param>
    /// <param name="mapping">The new mapping.</param>
    /// <returns>The new list.</returns>
    public static IReadOnlyList<ProjectMapping> Put(IReadOnlyList<ProjectMapping> mappings, ProjectMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(mapping);

        var placed = mapping with { Vault = Path.GetFullPath(mapping.Vault) };
        return [.. mappings.Where(other => !Same(other, placed.Vault, placed.Project)), placed];
    }

    /// <summary>Whether a directory and command can be saved and run.</summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="command">The command.</param>
    /// <param name="error">Why not, when this returns false.</param>
    /// <returns>Whether they are usable.</returns>
    public static bool IsUsable(string directory, string command, out string error)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(command);

        if (!Path.IsPathFullyQualified(directory))
        {
            error = "Choose the project's directory as a full path.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Enter the command to run.";
            return false;
        }

        if (command.Length > MaximumCommandLength || command.Any(char.IsControl))
        {
            error = $"The command must be one line of at most {MaximumCommandLength} characters.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Writes the mappings, replacing the file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="mappings">What to write.</param>
    /// <returns>Whether the file was written.</returns>
    /// <remarks>Owner-only on Unix; on Windows the file inherits the profile's ACL, as <c>app.toml</c> does.</remarks>
    public static bool Save(string path, IReadOnlyList<ProjectMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mappings);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("projects");

            foreach (var mapping in mappings)
            {
                writer.WriteStartObject();
                writer.WriteString("vault", mapping.Vault);
                writer.WriteString("project", mapping.Project);
                writer.WriteString("directory", mapping.Directory);
                writer.WriteString("command", mapping.Command);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, buffer.ToArray());

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static bool Same(ProjectMapping mapping, string fullVault, string project) =>
        string.Equals(mapping.Vault, fullVault, PathIdentity.Comparison)
        && string.Equals(mapping.Project, project, StringComparison.Ordinal);
}
