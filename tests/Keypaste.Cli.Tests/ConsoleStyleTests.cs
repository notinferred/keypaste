using Keypaste.Cli.Styling;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The one place keypaste emits colour, and the several places it must not.
/// </summary>
/// <remarks>
/// Asserted against the real <see cref="SystemConsoleStyle"/> rather than the harness fake, which
/// writes plain text so that every other test in this suite can keep matching substrings.
/// </remarks>
public sealed class ConsoleStyleTests
{
    internal const string Escape = "\u001b";

    private static string Alarm(bool errorRedirected, params (string Name, string Value)[] environment)
    {
        var values = environment.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var style = new SystemConsoleStyle(new FakeEnvironment(values), errorRedirected);

        using var writer = new StringWriter();
        style.Alarm(writer, "danger");
        return writer.ToString();
    }

    /// <summary>
    /// Redirected stderr is a file, a pipe, or a CI log. Escape sequences there are noise at best
    /// and a corrupted grep at worst.
    /// </summary>
    [Fact]
    public void RedirectedStderr_GetsNoEscapes()
    {
        var written = Alarm(errorRedirected: true);

        Assert.Contains("danger", written, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, written, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NO_COLOR", "1")]
    [InlineData("NO_COLOR", "anything")]
    [InlineData("TERM", "dumb")]
    public void TheUsualOptOuts_AreHonoured(string name, string value)
    {
        var written = Alarm(errorRedirected: false, (name, value));

        Assert.Contains("danger", written, StringComparison.Ordinal);
        Assert.DoesNotContain(Escape, written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>NO_COLOR</c> convention is that the variable must be <em>set and non-empty</em>; an
    /// empty value is not an opt-out.
    /// </summary>
    [Fact]
    public void AnEmptyNoColor_IsNotAnOptOut() =>
        AssertColoured(Alarm(errorRedirected: false, ("NO_COLOR", string.Empty)));

    [Fact]
    public void ATerminalGetsTheColour() =>
        AssertColoured(Alarm(errorRedirected: false, ("TERM", "xterm-256color")));

    /// <summary>The text always survives, whichever route it took.</summary>
    private static void AssertColoured(string written)
    {
        Assert.Contains("danger", written, StringComparison.Ordinal);

        // Windows colours through the console attribute API rather than escape sequences, because
        // conhost does not enable virtual terminal processing for us. There is nothing in the
        // stream to assert there — only that the message still came through.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains(SystemConsoleStyle.Red, written, StringComparison.Ordinal);
            Assert.EndsWith(SystemConsoleStyle.Reset + System.Environment.NewLine, written, StringComparison.Ordinal);
        }
    }
}
