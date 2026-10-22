using System.Text.Json;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Projects;

/// <summary>Where a project's command runs on this machine, and which env set it runs with.</summary>
/// <param name="Vault">The vault file holding the set, as a full path.</param>
/// <param name="Project">The project, as its entries' tags name it.</param>
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

    private static readonly string[] _fields = ["vault", "project", "directory", "command"];

    /// <summary>Reads the mappings.</summary>
    /// <param name="path">The file, from <see cref="Audit.KeypasteHome.ProjectsPath"/>.</param>
    /// <param name="mappings">Every well-formed mapping, or none when this returns false.</param>
    /// <returns>False when the file exists and could not be read, which <see cref="Save"/> must then not replace.</returns>
    public static bool TryLoad(string path, out IReadOnlyList<ProjectMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(path);

        mappings = [];

        using var document = Parse(path, out var absent);

        if (document is null)
        {
            return absent;
        }

        mappings = [.. Projects(document).Select(Read).OfType<ProjectMapping>()];
        return true;
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

    /// <summary>Writes the mappings, keeping what the file holds that <see cref="TryLoad"/> does not read.</summary>
    /// <param name="path">The file.</param>
    /// <param name="mappings">What to write.</param>
    /// <returns>Whether the file was written.</returns>
    /// <remarks>
    /// <para>
    /// A mapping keeps the other properties the file gave the mapping for its vault and project, a mapping
    /// <see cref="TryLoad"/> skipped stays as it was unless one written names its vault and project, and the
    /// file's other top-level properties stay, so a version older than the one that wrote them drops none
    /// (D-NEW-X51). A mapping <see cref="TryLoad"/> read and the caller left out is gone.
    /// </para>
    /// <para>Owner-only on Unix; on Windows the file inherits the profile's ACL, as <c>app.toml</c> does.</para>
    /// </remarks>
    public static bool Save(string path, IReadOnlyList<ProjectMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mappings);

        using var existing = Parse(path, out _);
        JsonElement[] items = existing is null ? [] : [.. Projects(existing)];

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

                if (Array.Find(items, item => Names(item, mapping)) is { ValueKind: JsonValueKind.Object } before)
                {
                    foreach (var property in before.EnumerateObject().Where(member => !_fields.Contains(member.Name, StringComparer.Ordinal)))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            foreach (var item in items.Where(element => Read(element) is null && !mappings.Any(mapping => Names(element, mapping))))
            {
                item.WriteTo(writer);
            }

            writer.WriteEndArray();

            if (existing is not null)
            {
                foreach (var property in existing.RootElement.EnumerateObject().Where(member => !member.NameEquals("projects")))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            AtomicFile.Write(path, buffer.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads the file as a document whose root holds a <c>projects</c> array.</summary>
    /// <param name="path">The file.</param>
    /// <param name="absent">Whether there is no file at all, rather than one that cannot be read.</param>
    /// <returns>The document, or null.</returns>
    private static JsonDocument? Parse(string path, out bool absent)
    {
        absent = !File.Exists(path);

        if (absent)
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);

            if (bytes.Length > MaximumBytes)
            {
                return null;
            }

            JsonDocument? document = JsonDocument.Parse(bytes);

            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("projects", out var projects)
                    || projects.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var read = document;
                document = null;
                return read;
            }
            finally
            {
                document?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static JsonElement.ArrayEnumerator Projects(JsonDocument document) =>
        document.RootElement.GetProperty("projects").EnumerateArray();

    /// <summary>The mapping an element of the file describes, or null when a field is missing or unusable.</summary>
    private static ProjectMapping? Read(JsonElement item) =>
        Text(item, "vault") is { } vault
        && Text(item, "project") is { } project
        && Text(item, "directory") is { } directory
        && Text(item, "command") is { } command
        && Path.IsPathFullyQualified(vault)
        && IsUsable(directory, command, out _)
            ? new ProjectMapping(vault, project, directory, command)
            : null;

    /// <summary>Whether an element of the file names the same vault and project as <paramref name="mapping"/>.</summary>
    private static bool Names(JsonElement item, ProjectMapping mapping) =>
        Text(item, "vault") is { } vault
        && Text(item, "project") is { } project
        && string.Equals(vault, mapping.Vault, PathIdentity.Comparison)
        && string.Equals(project, mapping.Project, StringComparison.Ordinal);

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
