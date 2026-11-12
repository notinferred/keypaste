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

    /// <summary>The repository the tests were built from.</summary>
    internal static string Repository => _root.Value;

    internal static string Keypaste => Environment.GetEnvironmentVariable("KEYPASTE_BIN") is { Length: > 0 } configured
        ? configured
        : Program("Keypaste.Cli", "keypaste");

    internal static string AppDriver => Program("Keypaste.AppDriver", "Keypaste.AppDriver");

    internal static string FakeBrowser => Program("Keypaste.FakeBrowser", "Keypaste.FakeBrowser");

    /// <summary>tests/Keypaste.FakeMcpClient, built under the name <c>claude</c> so a tool's lookup on PATH finds it.</summary>
    internal static string FakeClaude => Program("Keypaste.FakeMcpClient", "claude");

    /// <summary>keypaste-app itself, its Windows target on Windows, as the package carries it there.</summary>
    internal static string App => Program("Keypaste.App", "keypaste-app", OperatingSystem.IsWindows() ? "release_net10.0-windows10.0.19041.0" : "release_net10.0");

    private static string Program(string project, string name, string configuration = "release")
    {
        var path = Path.Combine(_root.Value, "artifacts", "bin", project, configuration, OperatingSystem.IsWindows() ? $"{name}.exe" : name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"build {project} first", path);
    }
}
