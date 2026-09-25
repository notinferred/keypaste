using Keypaste.Cli.Approval;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste agent</c>'s one stderr writer: a line written while a person is choosing never
/// splices into the choice line of the dialog they are reading (THREATS.md T-2).
/// </summary>
public sealed class AgentConsoleTests
{
    private const string _choice = "[d] deny  [o] once  [h] 1 hour  45s › ";

    private static readonly string _nl = Environment.NewLine;

    [Fact]
    public void ALineDuringAChoice_ClearsPrintsAndRedraws()
    {
        using var stderr = new StringWriter();
        var console = new AgentConsole(stderr, interactive: true);

        console.BeginChoice(() => _choice);
        console.WriteLine("keypaste: released env/dev/A to claude-code once");
        console.EndChoice();
        console.WriteLine("after");

        Assert.Equal(
            AgentConsole.Cleared(_choice) + "keypaste: released env/dev/A to claude-code once" + _nl
                + AgentConsole.Drawn(_choice) + _nl
                + "after" + _nl,
            stderr.ToString());
    }

    /// <summary>
    /// What a Windows console without virtual terminal processing shows: an escape sequence would be
    /// printed rather than obeyed, so a line shorter than the choice line has to leave none of it
    /// behind by the characters alone.
    /// </summary>
    [Fact]
    public void AShortLineDuringAChoice_LeavesNothingOfItBehind_WithoutEscapes()
    {
        using var stderr = new StringWriter();
        var console = new AgentConsole(stderr, interactive: true);

        console.BeginChoice(() => _choice);
        stderr.Write(AgentConsole.Drawn(_choice));
        console.WriteLine("keypaste: short");
        console.EndChoice();

        Assert.DoesNotContain('\u001b', stderr.ToString());
        Assert.Equal(["keypaste: short", _choice.TrimEnd(), string.Empty], Screen(stderr.ToString()));
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
            .Where(piece => !string.IsNullOrWhiteSpace(piece))
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
        console.BeginChoice(() => _choice);
        console.WriteLine("during");
        console.EndChoice();
        console.WriteLine("after");

        Assert.Equal("before" + _nl + "during" + _nl + "after" + _nl, stderr.ToString());
        Assert.DoesNotContain('\u001b', stderr.ToString());
        Assert.DoesNotContain('\r', stderr.ToString().Replace(_nl, "\n", StringComparison.Ordinal));
    }

    /// <summary>The rows a console shows for what was written, each without trailing blanks: a carriage return goes back to column 0 and nothing else moves the cursor.</summary>
    private static List<string> Screen(string written)
    {
        List<List<char>> rows = [[]];
        var column = 0;

        foreach (var c in written.Replace(_nl, "\n", StringComparison.Ordinal))
        {
            switch (c)
            {
                case '\r':
                    column = 0;
                    break;
                case '\n':
                    rows.Add([]);
                    column = 0;
                    break;
                default:
                    var row = rows[^1];
                    if (column < row.Count)
                    {
                        row[column] = c;
                    }
                    else
                    {
                        row.Add(c);
                    }

                    column++;
                    break;
            }
        }

        return rows.Select(row => new string([.. row]).TrimEnd()).ToList();
    }

    [Fact]
    public void Clearing_APaintedLine_BlanksOnlyTheColumnsItShows()
    {
        Assert.Equal("\r   \r", AgentConsole.Cleared("\u001b[38;5;214mabc\u001b[0m"));
    }
}
