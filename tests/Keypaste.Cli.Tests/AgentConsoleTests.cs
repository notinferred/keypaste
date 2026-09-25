using Keypaste.Cli.Approval;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste agent</c>'s one stderr writer: a line written while a person is choosing never
/// splices into the choice line of the dialog they are reading (THREATS.md T-2).
/// </summary>
public sealed class AgentConsoleTests
{
    private const string _clear = "\r\u001b[K";

    private static readonly string _nl = Environment.NewLine;

    [Fact]
    public void ALineDuringAChoice_ClearsPrintsAndRedraws()
    {
        using var stderr = new StringWriter();
        var console = new AgentConsole(stderr, interactive: true);

        console.BeginChoice(() => "[d] deny  [o] once  45s › ");
        console.WriteLine("keypaste: released env/dev/A to claude-code once");
        console.EndChoice();
        console.WriteLine("after");

        Assert.Equal(
            _clear + "keypaste: released env/dev/A to claude-code once" + _nl
                + AgentConsole.Drawn("[d] deny  [o] once  45s › ") + _nl
                + "after" + _nl,
            stderr.ToString());
    }

    /// <summary>
    /// Narration from many connections while the prompt redraws its countdown, as the real prompt
    /// does: one write of the drawn line to the same writer. Every piece of output between carriage
    /// returns and newlines is a whole narration line or a whole choice line.
    /// </summary>
    [Fact]
    public async Task ConcurrentNarration_NeverSplicesIntoTheChoiceLine()
    {
        const string choice = "[d] deny  [o] once  [h] 1 hour  45s › ";
        const int writers = 8;
        const int linesEach = 200;

        using var inner = new StringWriter();
        var stderr = TextWriter.Synchronized(inner);
        var console = new AgentConsole(stderr, interactive: true);
        using var stop = new CancellationTokenSource();

        console.BeginChoice(() => choice);

        var ticking = Task.Run(() =>
        {
            for (var ticks = 0; ticks < 50_000 && !stop.IsCancellationRequested; ticks++)
            {
                stderr.Write(AgentConsole.Drawn(choice));
                Thread.Yield();
            }
        }, TestContext.Current.CancellationToken);

        var narrating = Enumerable.Range(0, writers)
            .Select(writer => Task.Run(() =>
            {
                for (var i = 0; i < linesEach; i++)
                {
                    console.WriteLine($"keypaste: narration {writer}-{i}");
                }
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(narrating);
        await stop.CancelAsync();
        await ticking;
        console.EndChoice();

        var pieces = inner.ToString()
            .Split(_nl)
            .SelectMany(line => line.Split('\r'))
            .Select(piece => piece.Replace("\u001b[K", string.Empty, StringComparison.Ordinal))
            .Where(piece => piece.Length > 0)
            .ToList();

        Assert.All(pieces, piece => Assert.True(
            piece == choice || piece.StartsWith("keypaste: narration ", StringComparison.Ordinal) && !piece.Contains('[', StringComparison.Ordinal),
            $"a spliced line: |{piece}|"));

        var narrated = pieces.Where(piece => piece != choice).ToList();
        Assert.Equal(writers * linesEach, narrated.Count);
        Assert.Equal(writers * linesEach, narrated.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Redirected_WritesLinesInOrder_WithNoEscapes()
    {
        using var stderr = new StringWriter();
        var console = new AgentConsole(stderr, interactive: false);

        console.WriteLine("before");
        console.BeginChoice(() => "[d] deny  [o] once  45s › ");
        console.WriteLine("during");
        console.EndChoice();
        console.WriteLine("after");

        Assert.Equal("before" + _nl + "during" + _nl + "after" + _nl, stderr.ToString());
        Assert.DoesNotContain('\u001b', stderr.ToString());
        Assert.DoesNotContain('\r', stderr.ToString().Replace(_nl, "\n", StringComparison.Ordinal));
    }
}
