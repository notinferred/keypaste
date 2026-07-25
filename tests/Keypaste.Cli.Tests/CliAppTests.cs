using System.Globalization;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

public sealed class CliAppTests
{
    /// <summary>
    /// The wiring proof: the CLI's greeting must be produced by keypaste-core, not by
    /// the CLI itself. Fails to compile if the reference is dropped and fails at runtime
    /// if the CLI ever grows its own copy — CORE.md law 4.3 as an executable assertion.
    /// </summary>
    [Fact]
    public void Hello_WritesTheCoreGreetingToStdout_AndExitsZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["hello"], stdout, stderr);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Equal(CoreInfo.Hello(), stdout.ToString().TrimEnd());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void NoArguments_DefaultsToHello()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run([], stdout, stderr);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Equal(CoreInfo.Hello(), stdout.ToString().TrimEnd());
    }

    [Fact]
    public void UnknownCommand_WritesToStderr_AndExitsNonZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["nope"], stdout, stderr);

        Assert.NotEqual(CliApp.ExitSuccess, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains("nope", stderr.ToString(), StringComparison.Ordinal);
    }
}
