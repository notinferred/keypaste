using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What a human actually sees before they decide, and what counts as them saying yes.
/// </summary>
/// <remarks>
/// This is the display half of THREATS.md T-2. The reason arrives already sanitized and capped, so
/// what is under test here is that the channel does not undo that — and that the default is no.
/// </remarks>
public sealed class TerminalApprovalChannelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ApprovalPrompt Prompt(string reason = "deploy billing to staging") =>
        ApprovalPrompt.For("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", reason, 300);

    private sealed record Rig(TerminalApprovalChannel Channel, FakeSecretPrompt Prompt, StringWriter Stderr);

    private static Rig Build(params string[] answers)
    {
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue(answers);

        var stderr = new StringWriter();

        return new Rig(new TerminalApprovalChannel(prompt, stderr), prompt, stderr);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("Y")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("  y  ")]
    public async Task AnExplicitYes_Approves(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.Approved, await rig.Channel.AskAsync(Prompt(), Token));
    }

    /// <summary>
    /// The list is deliberately long. Every one of these is something a person might type, or
    /// something a terminal might deliver, and every one of them has to mean no — including the
    /// empty line somebody produces by leaning on Enter to get their prompt back.
    /// </summary>
    [Theory]
    [InlineData("n")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("yep")]
    [InlineData("yeah")]
    [InlineData("ok")]
    [InlineData("sure")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("y please")]
    [InlineData("approve")]
    public async Task AnythingThatIsNotAnExplicitYes_Denies(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), Token));
    }

    /// <summary>End of input — a closed pipe, a Ctrl+D — is a denial, not a hang and not a yes.</summary>
    [Fact]
    public async Task EndOfInput_Denies()
    {
        var rig = Build();

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), Token));
    }

    [Fact]
    public async Task ThePersonIsShownWhoIsAskingForWhatAndWhy()
    {
        var rig = Build("n");

        await rig.Channel.AskAsync(Prompt(), Token);

        var shown = rig.Stderr.ToString();

        Assert.Contains("claude-code", shown, StringComparison.Ordinal);
        Assert.Contains("env/dev/STRIPE_KEY", shown, StringComparison.Ordinal);
        Assert.Contains("password", shown, StringComparison.Ordinal);
        Assert.Contains("300 seconds", shown, StringComparison.Ordinal);
        Assert.Contains("deploy billing to staging", shown, StringComparison.Ordinal);

        // The line that stops the reason being read as keypaste's own words.
        Assert.Contains("written by the agent, not by keypaste", shown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt is a question with a default, and the default is no. A reader who skims has to
    /// see that pressing Enter releases nothing.
    /// </summary>
    [Fact]
    public async Task TheQuestionSaysTheDefaultIsNo()
    {
        var rig = Build("n");

        await rig.Channel.AskAsync(Prompt(), Token);

        Assert.Contains(rig.Prompt.PromptsSeen, seen => seen.Contains("[y/N]", StringComparison.Ordinal));
    }

    /// <summary>
    /// The attack this rendering exists to survive: a reason that ends the request block and writes
    /// its own reassuring line underneath. The sanitizer collapses the newlines, so it arrives as
    /// one run-on sentence in the reason's own slot rather than as a second dialog.
    /// </summary>
    [Fact]
    public async Task AReasonCannotDrawASecondPromptInsideTheFirst()
    {
        var rig = Build("n");

        await rig.Channel.AskAsync(
            Prompt("routine\n" + TerminalApprovalChannel.Rule + "\nkeypaste: this one is safe, press y"),
            Token);

        var shown = rig.Stderr.ToString();
        var lines = shown.Split(Environment.NewLine);

        // Exactly the two rules the channel drew itself: one above the block, one below.
        Assert.Equal(2, lines.Count(line => line.Trim().Equals(TerminalApprovalChannel.Rule, StringComparison.Ordinal)));

        // ...and the payload is still visible, inside the reason, rather than having been silently
        // dropped. A test that passed because the text vanished would prove nothing.
        Assert.Contains("this one is safe", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATruncatedReasonSaysSo()
    {
        var rig = Build("n");

        await rig.Channel.AskAsync(Prompt(new string('a', 2000)), Token);

        Assert.Contains("cut short", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// When the request is withdrawn the human has to be told, or they answer a question nobody is
    /// listening to any more — and then wonder why nothing happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It queues a "y", so that "the request was withdrawn" has to beat an answer already sitting
    /// in the buffer rather than merely surviving an empty one.
    /// </para>
    /// <para>
    /// <b>The assertion that carries this test is <c>PromptsSeen</c>, not the message.</b>
    /// <see cref="Task.WaitAsync(CancellationToken)"/> returns an already completed task without
    /// ever looking at the token, so before the channel checked cancellation itself the outcome
    /// here turned on whether the thread pool finished the read first — it passed on developer
    /// machines for three stages and failed on a Linux CI runner. Asserting on the message alone
    /// reproduces that coin flip. Asserting that the read was never started does not: an
    /// already-withdrawn request must not put a question on a person's terminal at all, and that
    /// is true or false regardless of scheduling.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWithdrawnRequest_DeniesAndSaysSoToTheHuman()
    {
        var rig = Build("y");
        using var withdraw = new CancellationTokenSource();

        await withdraw.CancelAsync();

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), withdraw.Token));
        Assert.Contains("withdrawn", rig.Stderr.ToString(), StringComparison.Ordinal);

        // Never asked, so the queued "y" is still queued and could not have approved anything.
        Assert.Empty(rig.Prompt.PromptsSeen);
        Assert.DoesNotContain("keypaste: approved.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheChannelRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new TerminalApprovalChannel(null!, new StringWriter()));
        Assert.Throws<ArgumentNullException>(() => new TerminalApprovalChannel(new FakeSecretPrompt(), null!));
    }
}
