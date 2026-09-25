namespace Keypaste.Core.Tests;

/// <summary>Where the built <c>Keypaste.EnvReporter</c> is: the child the capture and run tests start.</summary>
internal static class Reporter
{
    /// <summary>The reporter's executable, built beside these tests by the project reference.</summary>
    internal static string Path { get; } = Locate();

    private static string Locate()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(System.IO.Path.Combine(directory, "keypaste.slnx")))
        {
            directory = System.IO.Path.GetDirectoryName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Could not locate keypaste.slnx above " + AppContext.BaseDirectory);
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase) ? "debug" : "release";
        var name = OperatingSystem.IsWindows() ? "Keypaste.EnvReporter.exe" : "Keypaste.EnvReporter";
        var path = System.IO.Path.Combine(directory, "artifacts", "bin", "Keypaste.EnvReporter", configuration, name);

        return File.Exists(path) ? path : throw new InvalidOperationException($"build Keypaste.EnvReporter first: {path}");
    }
}
