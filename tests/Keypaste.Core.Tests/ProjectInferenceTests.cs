using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A directory's project comes from the mapping a person saved for this vault, the nearest one whole (T-31).</summary>
public sealed class ProjectInferenceTests
{
    private static readonly string _root = Path.Combine(Path.GetTempPath(), "keypaste-inference");
    private static readonly string _vault = Path.Combine(_root, "vault.kdbx");

    [Fact]
    public void The_mapped_directory_itself_names_its_project()
    {
        Assert.True(Infer([Map("acme-api", "a", "b")], Dir("a", "b"), out var project, out _));
        Assert.Equal("acme-api", project);
    }

    [Fact]
    public void The_nearest_mapped_ancestor_wins()
    {
        var mappings = new[] { Map("monorepo", "a"), Map("acme-api", "a", "b") };

        Assert.True(Infer(mappings, Dir("a", "b", "src", "deep"), out var project, out _));
        Assert.Equal("acme-api", project);

        Assert.True(Infer(mappings, Dir("a", "other"), out var outer, out _));
        Assert.Equal("monorepo", outer);
    }

    [Fact]
    public void A_sibling_sharing_a_prefix_is_not_under_it()
    {
        Assert.False(Infer([Map("acme-api", "a", "b")], Dir("a", "bc"), out var project, out var error));
        Assert.Null(project);
        Assert.Equal(ProjectInference.NotMapped, error);
    }

    [Fact]
    public void A_mapping_of_another_vault_is_ignored()
    {
        var other = new ProjectMapping(Path.Combine(_root, "other.kdbx"), "acme-api", Dir("a"), "npm start");

        Assert.False(Infer([other], Dir("a"), out _, out var error));
        Assert.Equal(ProjectInference.NotMapped, error);
    }

    [Fact]
    public void Two_projects_on_one_directory_are_named_and_refused()
    {
        Assert.False(Infer([Map("web", "a"), Map("api", "a")], Dir("a"), out var project, out var error));
        Assert.Null(project);
        Assert.Contains("(api, web)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_matches_a_directory_in_another_case()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths are case-insensitive; Linux paths are not.");

        Assert.True(Infer([Map("acme-api", "Work", "Acme")], Dir("work", "acme", "src"), out var project, out _));
        Assert.Equal("acme-api", project);
    }

    private static bool Infer(IReadOnlyList<ProjectMapping> mappings, string directory, out string? project, out string error) =>
        ProjectInference.TryInfer(mappings, _vault, directory, out project, out error);

    private static ProjectMapping Map(string project, params string[] directory) =>
        new(_vault, project, Dir(directory), "npm start");

    private static string Dir(params string[] parts) => Path.Combine([_root, .. parts]);
}
