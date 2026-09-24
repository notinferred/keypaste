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
}
