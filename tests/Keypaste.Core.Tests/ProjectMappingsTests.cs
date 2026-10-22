using System.Text.Json;
using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A project's directory and command are kept on this machine, one per vault and project, with no
/// field a value could be written to, and a file keypaste cannot read is never replaced (D-0339).
/// </summary>
public sealed class ProjectMappingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-projects-").FullName;

    private string File_ => Path.Combine(_directory, "home", "projects.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_mapping_round_trips_with_quotes_and_backslashes_and_replaces_the_one_before_it()
    {
        var vault = Path.Combine(_directory, "a.kdbx");
        var first = new ProjectMapping(vault, "dev", _directory, "make");
        var second = first with { Command = "\"C:\\Program Files\\nodejs\\npm.cmd\" run dev" };
        var other = first with { Project = "ops" };

        Assert.True(ProjectMappings.Save(File_, ProjectMappings.Put(ProjectMappings.Put([first, other], second), second)));

        Assert.True(ProjectMappings.TryLoad(File_, out var loaded));
        Assert.Equal(2, loaded.Count);
        Assert.Equal(second, ProjectMappings.Find(loaded, vault, "dev"));
        Assert.Equal(other, ProjectMappings.Find(loaded, vault, "ops"));
        Assert.Null(ProjectMappings.Find(loaded, Path.Combine(_directory, "b.kdbx"), "dev"));
    }

    [Fact]
    public void The_file_holds_only_the_vault_the_project_the_directory_and_the_command()
    {
        Assert.True(ProjectMappings.Save(File_, [new ProjectMapping(Path.Combine(_directory, "a.kdbx"), "dev", _directory, "make")]));

        using var document = JsonDocument.Parse(File.ReadAllBytes(File_));
        var item = Assert.Single(document.RootElement.GetProperty("projects").EnumerateArray());

        Assert.Equal(["vault", "project", "directory", "command"], item.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Saving_replaces_the_file_owner_only_and_leaves_nothing_beside_it()
    {
        var mapping = new ProjectMapping(Path.Combine(_directory, "a.kdbx"), "dev", _directory, "make");

        Assert.True(ProjectMappings.Save(File_, [mapping]));
        Assert.True(ProjectMappings.Save(File_, [mapping with { Command = "make test" }]));

        Assert.Equal([File_], Directory.GetFiles(Path.GetDirectoryName(File_)!));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(File_));
        }
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_and_left_as_it_is()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "{ half an edit");

        Assert.False(ProjectMappings.TryLoad(File_, out var loaded));
        Assert.Empty(loaded);
        Assert.Equal("{ half an edit", File.ReadAllText(File_));

        Assert.True(ProjectMappings.TryLoad(Path.Combine(_directory, "absent.json"), out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void A_malformed_mapping_is_skipped_and_the_rest_are_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        var vault = JsonEncodedText.Encode(Path.Combine(_directory, "a.kdbx"));
        var directory = JsonEncodedText.Encode(_directory);
        File.WriteAllText(File_, $$"""
            { "projects": [
              { "vault": "{{vault}}", "project": "dev", "directory": "relative", "command": "make" },
              { "vault": "{{vault}}", "project": "ops", "directory": "{{directory}}", "command": "make" }
            ] }
            """);

        Assert.True(ProjectMappings.TryLoad(File_, out var loaded));
        Assert.Equal("ops", Assert.Single(loaded).Project);
    }

    /// <summary>What a newer version wrote and this one does not read survives a save of another mapping (D-0520).</summary>
    [Fact]
    public void A_mapping_it_cannot_read_and_properties_it_does_not_know_survive_a_save()
    {
        WriteFile();
        Assert.True(ProjectMappings.TryLoad(File_, out var loaded));

        var added = new ProjectMapping(Path.Combine(_directory, "a.kdbx"), "web", _directory, "npm start");
        Assert.True(ProjectMappings.Save(File_, ProjectMappings.Put(loaded, added)));

        using var document = JsonDocument.Parse(File.ReadAllBytes(File_));
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        var items = document.RootElement.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("relative", Item(items, "dev").GetProperty("directory").GetString());
        Assert.Equal("dev-note", Item(items, "dev").GetProperty("note").GetString());
        Assert.Equal("pwsh", Item(items, "ops").GetProperty("shell").GetString());

        Assert.True(ProjectMappings.TryLoad(File_, out var reloaded));
        Assert.Equal(["ops", "web"], reloaded.Select(mapping => mapping.Project));
    }

    [Fact]
    public void Replacing_a_mapping_keeps_the_properties_it_does_not_know_and_takes_the_place_of_one_it_could_not_read()
    {
        WriteFile();
        Assert.True(ProjectMappings.TryLoad(File_, out var loaded));
        var ops = Assert.Single(loaded);

        Assert.True(ProjectMappings.Save(File_, ProjectMappings.Put(loaded, ops with { Command = "make deploy" })));
        Assert.True(ProjectMappings.TryLoad(File_, out loaded));
        Assert.True(ProjectMappings.Save(File_, ProjectMappings.Put(loaded, ops with { Project = "dev" })));

        using var document = JsonDocument.Parse(File.ReadAllBytes(File_));
        var items = document.RootElement.GetProperty("projects").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("make deploy", Item(items, "ops").GetProperty("command").GetString());
        Assert.Equal("pwsh", Item(items, "ops").GetProperty("shell").GetString());
        Assert.Equal(_directory, Item(items, "dev").GetProperty("directory").GetString());
        Assert.Equal("dev-note", Item(items, "dev").GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("relative/dir", "make", false)]
    [InlineData(null, "", false)]
    [InlineData(null, "make\nrm -rf /", false)]
    [InlineData(null, "npm run dev", true)]
    public void A_mapping_needs_a_full_directory_and_one_line_of_command(string? directory, string command, bool usable)
    {
        Assert.Equal(usable, ProjectMappings.IsUsable(directory ?? _directory, command, out var error));
        Assert.Equal(usable, error.Length == 0);
    }

    /// <summary>A file a newer version might write: a top-level property, a mapping with a relative directory and a note, and one with a shell.</summary>
    private void WriteFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        var vault = JsonEncodedText.Encode(Path.Combine(_directory, "a.kdbx"));
        var directory = JsonEncodedText.Encode(_directory);
        File.WriteAllText(File_, $$"""
            { "version": 2, "projects": [
              { "vault": "{{vault}}", "project": "dev", "directory": "relative", "command": "make", "note": "dev-note" },
              { "vault": "{{vault}}", "project": "ops", "directory": "{{directory}}", "command": "make", "shell": "pwsh" }
            ] }
            """);
    }

    private static JsonElement Item(List<JsonElement> items, string project) =>
        Assert.Single(items, item => string.Equals(item.GetProperty("project").GetString(), project, StringComparison.Ordinal));
}
