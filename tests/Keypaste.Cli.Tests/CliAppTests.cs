using System.Globalization;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

public sealed class CliAppTests
{
    /// <summary>
    /// The wiring proof: the CLI's greeting must be produced by keypaste-core, not by
    /// the CLI itself. Fails to compile if the reference is dropped and fails at runtime
    /// if the CLI ever grows its own copy — docs/PRODUCT.md law 4.3 as an executable assertion.
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

    /// <summary>
    /// Deliberate change from Stage 0.1, where no arguments greeted you. That was scaffolding;
    /// a tool with verbs should say what they are. The <c>hello</c> verb itself survives above,
    /// because it is the docs/PRODUCT.md law 4.3 wiring proof.
    /// </summary>
    [Fact]
    public void NoArguments_PrintsUsageToStderr_AndExitsNonZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run([], stdout, stderr);

        Assert.Equal(CliApp.ExitUsageError, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains("usage: keypaste", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_GoesToStdout_AndExitsZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["--help"], stdout, stderr);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("usage: keypaste", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
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
