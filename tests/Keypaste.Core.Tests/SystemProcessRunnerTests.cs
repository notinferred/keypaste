using System.Text;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A tool given an environment runs with exactly that environment and nothing this process holds,
/// which is how the app gives <c>open</c> its own less every <c>KEYPASTE_</c> variable (T-42);
/// a tool given none inherits this process's, as every other caller relies on.
/// </summary>
public sealed class SystemProcessRunnerTests : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private static readonly Encoding _noPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _inherited = "RUNNER_TEST_" + Guid.NewGuid().ToString("N");

    public SystemProcessRunnerTests() => Environment.SetEnvironmentVariable(_inherited, "inherited");

    public void Dispose() => Environment.SetEnvironmentVariable(_inherited, null);

    [Fact]
    public void A_tool_given_an_environment_sees_exactly_it()
    {
        var given = new Dictionary<string, string>
        {
            ["GIVEN_MARKER"] = "a value with spaces = and an equals sign",
            ["ANOTHER"] = "second",
        };

        if (OperatingSystem.IsWindows())
        {
            given["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot")!;
        }

        var printed = Printed(new SystemProcessRunner(given));

        Assert.All(given, pair => Assert.Contains($"{pair.Key}={pair.Value}", printed));
        Assert.DoesNotContain(printed, line => line.StartsWith(_inherited + "=", StringComparison.OrdinalIgnoreCase));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(given.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal), printed.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_tool_given_no_environment_inherits_this_processes()
    {
        var printed = Printed(new SystemProcessRunner());

        Assert.Contains($"{_inherited}=inherited", printed);
    }

    /// <summary>Every <c>NAME=value</c> line the tool printed of its environment.</summary>
    private static List<string> Printed(SystemProcessRunner runner)
    {
        var result = OperatingSystem.IsWindows()
            ? runner.Run(Environment.GetEnvironmentVariable("ComSpec")!, ["/d", "/c", "set"], stdin: null, _noPreamble, _timeout)
            : runner.Run("/usr/bin/env", [], stdin: null, _noPreamble, _timeout);

        Assert.True(result.Succeeded, result.StandardError);
        return [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))];
    }
}
