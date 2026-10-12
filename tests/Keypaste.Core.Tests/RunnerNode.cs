using System.Text.Json.Nodes;
using Keypaste.Core.Launch;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The runner's real <c>node</c>, which the launch tests start a tool's script with.</summary>
internal static class RunnerNode
{
    /// <summary>Arguments a shell would reinterpret, which must reach the script as they are.</summary>
    public static readonly string[] Awkward = ["&", "\"", "%PATH%", "^", "!x!", "two words", @"trailing\", string.Empty, "$HOME", "'"];

    /// <summary>A script that prints the arguments after its own path as a JSON array.</summary>
    public const string Printer = "#!/usr/bin/env node\nprocess.stdout.write(JSON.stringify(process.argv.slice(2)));\n";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The <c>node</c> on this process's <c>PATH</c>; a skip without one, except on CI, where it fails.</summary>
    public static string Find()
    {
        if (RunProgram.TryResolve(
                "node",
                Path.GetTempPath(),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATHEXT"),
                out var node,
                out _)
            && (!OperatingSystem.IsWindows() || node.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            return node;
        }

        Assert.False(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true", "the runner has no node.exe on PATH, so this launch is not shown");
        Assert.Skip("no node on PATH");
        return string.Empty;
    }

    /// <summary>The arguments a printer received.</summary>
    public static string[] Printed(ProcessResult result)
    {
        Assert.True(result.Succeeded, $"exit {result.ExitCode}: {result.StandardError}");

        return [.. JsonNode.Parse(result.StandardOutput)!.AsArray().Select(item => item!.GetValue<string>())];
    }
}
