namespace Keypaste.Processes.Tests;

/// <summary>The built programs these tests start: <c>KEYPASTE_BIN</c> or the Release build of keypaste, and tests/Keypaste.AppDriver.</summary>
internal static class Built
{
    private static readonly Lazy<string> _root = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "keypaste.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("no keypaste.slnx above the test's directory");
    });

    internal static string Keypaste => Environment.GetEnvironmentVariable("KEYPASTE_BIN") is { Length: > 0 } configured
        ? configured
        : Program("Keypaste.Cli", "keypaste");

    internal static string AppDriver => Program("Keypaste.AppDriver", "Keypaste.AppDriver");

    private static string Program(string project, string name)
    {
        var path = Path.Combine(_root.Value, "artifacts", "bin", project, "release", OperatingSystem.IsWindows() ? $"{name}.exe" : name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"build {project} first", path);
    }
}
