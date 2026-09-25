using System.Text;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What a human actually sees before they decide, and which answers release anything.
/// </summary>
/// <remarks>
/// This is the display half of THREATS.md T-2. The reason arrives already sanitized and capped, so
/// what is under test here is that the channel does not undo that — and that the default is no:
/// <c>o</c> allows once, <c>h</c> allows for the timed grant when one is offered, and everything
/// else denies.
/// </remarks>
public sealed class TerminalApprovalChannelTests
{
    private static readonly TimeSpan _window = TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ApprovalPrompt Prompt(string reason = "deploy billing to staging", int grantSeconds = 3600) =>
        ApprovalPrompt.For("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", reason, grantSeconds);

    private sealed record Rig(TerminalApprovalChannel Channel, FakeSecretPrompt Prompt, StringWriter Stderr);

    private static Rig Build(params string[] answers)
    {
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue(answers);

        var stderr = new StringWriter();

        return new Rig(Channel(prompt, stderr), prompt, stderr);
    }

    private static TerminalApprovalChannel Channel(FakeSecretPrompt prompt, TextWriter stderr) =>
        new(prompt, new AgentConsole(stderr, interactive: false), _window, TimeProvider.System);

    [Fact]
    public async Task H_AllowsForTheOfferedTime()
    {
        var rig = Build("h");

        Assert.Equal(ApprovalAnswer.Approved, await rig.Channel.AskAsync(Prompt(), Token));
        Assert.Contains("keypaste: allowed for 1 hour.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("o")]
    [InlineData("O")]
    [InlineData("once")]
    public async Task O_AllowsOnce(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(Prompt(), Token));
        Assert.Contains("keypaste: allowed once.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every one of these is something a person might type, or something a terminal might deliver,
    /// and every one of them has to mean no — including the empty line somebody produces by leaning
    /// on Enter, and the <c>y</c> the previous prompt accepted.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("n")]
    [InlineData("y")]
    [InlineData("yes")]
    [InlineData("x")]
    [InlineData(" ")]
    [InlineData("oh")]
    [InlineData("hours")]
    public async Task EverythingElse_Denies(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), Token));
        Assert.Contains("keypaste: denied. Nothing was released.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>End of input — a closed pipe, a Ctrl+D — is a denial, not a hang and not a yes.</summary>
    [Fact]
    public async Task EndOfInput_Denies()
    {
        var rig = Build();

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), Token));
    }

    [Fact]
    public async Task H_WhenNoTimedGrantIsOffered_Denies()
    {
        var rig = Build("h");

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(grantSeconds: 0), Token));
    }

    [Fact]
    public async Task TheChoiceLine_NamesTheDuration()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(grantSeconds: 300), Token);

        Assert.Equal("[d] deny  [o] once  [h] 5 minutes  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    /// <summary>The choice line keeps one width as it counts down, which is what lets <see cref="AgentConsole"/> blank it with spaces.</summary>
    [Fact]
    public async Task TheChoiceLine_KeepsItsWidth_AtOneDigit()
    {
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue("d");
        var channel = new TerminalApprovalChannel(
            prompt, new AgentConsole(new StringWriter(), interactive: false), TimeSpan.FromSeconds(ApprovalLimits.MinimumWindowSeconds), TimeProvider.System);

        await channel.AskAsync(Prompt(), Token);

        Assert.Equal("[d] deny  [o] once  [h] 1 hour   5s › ", Assert.Single(prompt.PromptsSeen));
    }

    [Theory]
    [InlineData("h", "<Ok>keypaste: allowed for 1 hour.</>")]
    [InlineData("o", "<Ok>keypaste: allowed once.</>")]
    [InlineData("d", "<Danger>keypaste: denied. Nothing was released.</>")]
    public async Task TheDialog_IsColouredOnlyThroughTheConsoleStyle(string answer, string outcome)
    {
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue(answer);
        var stderr = new StringWriter();
        var channel = new TerminalApprovalChannel(
            prompt, new AgentConsole(stderr, interactive: false, new MarkingStyle(stderr)), _window, TimeProvider.System);

        await channel.AskAsync(Prompt(), Token);

        Assert.Contains($"<Accent>{TerminalApprovalChannel.Rule}</>", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains(outcome, stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("<Accent>[d] deny  [o] once  </><Ok>[h] 1 hour</>  <Accent>45s ›</> ", Assert.Single(prompt.PromptsSeen));
    }

    /// <summary>Paints by marking each tone, so a test sees what was coloured and how without reading escapes.</summary>
    private sealed class MarkingStyle(TextWriter painted) : Styling.IConsoleStyle
    {
        public void Alarm(TextWriter writer, string text) => writer.WriteLine(text);

        public string Paint(TextWriter writer, Styling.Tone tone, string text) =>
            ReferenceEquals(writer, painted) ? $"<{tone}>{text}</>" : text;
    }

    [Fact]
    public async Task TheChoiceLine_DropsH_WhenNotOffered()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(grantSeconds: 0), Token);

        Assert.Equal("[d] deny  [o] once  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
        Assert.Contains(
            Environment.NewLine + "  this entry is in a protected profile: it is asked about every time." + Environment.NewLine,
            rig.Stderr.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The dialog <c>scripts/verify-demo.sh</c> diffs against five pages: the <c>for</c> line is
    /// gone and nothing else moved.
    /// </summary>
    [Fact]
    public async Task TheDialog_HasNoForLine_AndIsOtherwiseUnchanged()
    {
        var rig = Build("d");
        var prompt = ApprovalPrompt.For(
            "claude-code", new EntryName("env/demo", "STRIPE_KEY"), "password", "deploy the billing service to staging", 3600);

        await rig.Channel.AskAsync(prompt, Token);

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: an agent is asking for a credential.",
            string.Empty,
            "  client   claude-code",
            "  entry    env/demo/STRIPE_KEY",
            "  field    password",
            string.Empty,
            "  the agent says it needs this because:",
            "    deploy the billing service to staging",
            string.Empty,
            "  That sentence was written by the agent, not by keypaste. Treat it as a claim.",
            string.Empty,
            "keypaste: denied. Nothing was released.",
            TerminalApprovalChannel.Rule,
            string.Empty,
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), rig.Stderr.ToString());
        Assert.Equal("[d] deny  [o] once  [h] 1 hour  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    [Fact]
    public async Task TheChoiceMark_FallsBackOnACodePageWithoutIt()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue("d");
        using var stderr = new StreamWriter(new MemoryStream(), Encoding.GetEncoding(437));

        await Channel(prompt, stderr).AskAsync(Prompt(), Token);

        Assert.Equal("[d] deny  [o] once  [h] 1 hour  45s > ", Assert.Single(prompt.PromptsSeen));
    }

    [Fact]
    public async Task AnEntryNameTheSanitizerChanged_SaysSo()
    {
        var rig = Build("d");
        var spoofed = new EntryName("env/dev", "STRIPE" + ((char)0x202E) + "KEY");

        await rig.Channel.AskAsync(ApprovalPrompt.For("claude-code", spoofed, "password", "deploy", 300), Token);

        Assert.Contains("not what the vault holds", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryEntryName_SaysNothingExtra()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(), Token);

        Assert.DoesNotContain("not what the vault holds", rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("protected profile", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePersonIsShownWhoIsAskingForWhatAndWhy()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(), Token);

        var shown = rig.Stderr.ToString();

        Assert.Contains("claude-code", shown, StringComparison.Ordinal);
        Assert.Contains("env/dev/STRIPE_KEY", shown, StringComparison.Ordinal);
        Assert.Contains("password", shown, StringComparison.Ordinal);
        Assert.Contains("deploy billing to staging", shown, StringComparison.Ordinal);

        // The line that stops the reason being read as keypaste's own words.
        Assert.Contains("written by the agent, not by keypaste", shown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The attack this rendering exists to survive: a reason that ends the request block and writes
    /// its own reassuring line underneath. The sanitizer collapses the newlines, so it arrives as
    /// one run-on sentence in the reason's own slot rather than as a second dialog.
    /// </summary>
    [Fact]
    public async Task AReasonCannotDrawASecondPromptInsideTheFirst()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(
            Prompt("routine\n" + TerminalApprovalChannel.Rule + "\nkeypaste: this one is safe, press h"),
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
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(new string('a', 2000)), Token);

        Assert.Contains("cut short", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// When the request is withdrawn the human has to be told, or they answer a question nobody is
    /// listening to any more — and then wonder why nothing happened.
    /// </summary>
    /// <remarks>
    /// It queues an <c>h</c>, so that "the request was withdrawn" has to beat an answer already
    /// sitting in the buffer rather than merely surviving an empty one. The assertion that carries
    /// the test is <c>PromptsSeen</c>: an already-withdrawn request must not put a question on a
    /// person's terminal at all, which is true or false regardless of scheduling.
    /// </remarks>
    [Fact]
    public async Task Withdrawal_StillDenies()
    {
        var rig = Build("h");
        using var withdraw = new CancellationTokenSource();

        await withdraw.CancelAsync();

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), withdraw.Token));
        Assert.Contains("withdrawn", rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Empty(rig.Prompt.PromptsSeen);
        Assert.DoesNotContain("keypaste: allowed", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    private static EnvReleasePrompt EnvPrompt(params string[] command) =>
        EnvReleasePrompt.For(new EnvPreview("billing", ["DATABASE_URL", "STRIPE_KEY"]), command, "/home/me/billing") with
        {
            GrantSeconds = EnvGrantCache.CeilingSeconds,
        };

    [Fact]
    public async Task ARunsRequest_ShowsTheProjectNamesCommandAndDirectory_AndOnlyAnAllowReleases()
    {
        var once = Build("o");
        var hour = Build("h");
        var nothing = Build(string.Empty);

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await once.Channel.AskAsync(EnvPrompt("npm", "run", "deploy"), Token));
        Assert.Equal(ApprovalAnswer.Approved, await hour.Channel.AskAsync(EnvPrompt("npm", "run", "deploy"), Token));
        Assert.Equal(ApprovalAnswer.Denied, await nothing.Channel.AskAsync(EnvPrompt("npm", "run", "deploy"), Token));

        var shown = once.Stderr.ToString();
        Assert.Contains("project    billing", shown, StringComparison.Ordinal);
        Assert.Contains("profile    dev", shown, StringComparison.Ordinal);
        Assert.Contains("variables  DATABASE_URL STRIPE_KEY", shown, StringComparison.Ordinal);
        Assert.Contains("command    npm run deploy", shown, StringComparison.Ordinal);
        Assert.Contains("in         /home/me/billing", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("scrubbed", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("asked by", shown, StringComparison.Ordinal);
        Assert.Contains("keypaste: allowed for 15 minutes.", hour.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnvDialog_NamesTheProfile_AndRequester()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("deploy") with { Profile = "staging", Requester = "token ci-deploy" }, Token);

        var shown = rig.Stderr.ToString();
        Assert.Contains(
            "  project    billing" + Environment.NewLine + "  profile    staging" + Environment.NewLine + "  asked by   token ci-deploy" + Environment.NewLine,
            shown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnvDialog_ShowsWhatAReferenceFileInjects()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("deploy") with { FileLines = ["DB ← DATABASE_URL", "MODE=ci"] }, Token);

        Assert.Contains(
            "  variables  DATABASE_URL STRIPE_KEY" + Environment.NewLine + "  injects    DB ← DATABASE_URL" + Environment.NewLine + "  injects    MODE=ci" + Environment.NewLine,
            rig.Stderr.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnvDialog_SaysWhomTheTimedChoiceCovers()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("deploy"), Token);

        Assert.Contains(
            "  [d] deny: the run gets nothing." + Environment.NewLine
                + "  [o] once: the run starts this command one time with every value in its environment." + Environment.NewLine
                + "  [h] 15 minutes: the same, and any program of yours can run exactly this command here again for 15 minutes without asking.",
            rig.Stderr.ToString(),
            StringComparison.Ordinal);
        Assert.Equal("[d] deny  [o] once  [h] 15 minutes  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    [Fact]
    public async Task ARequesterPrompt_OffersNoTimedChoice()
    {
        var rig = Build("h");

        var answer = await rig.Channel.AskAsync(EnvPrompt("deploy") with { Requester = "token ci-deploy" }, Token);

        Assert.Equal(ApprovalAnswer.Denied, answer);
        Assert.Equal("[d] deny  [o] once  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
        Assert.DoesNotContain("[h]", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProtectedProfilesRun_OffersNoTimedChoice_AndSaysWhy()
    {
        var rig = Build("h");

        var answer = await rig.Channel.AskAsync(EnvPrompt("deploy") with { Profile = "prod", GrantSeconds = 0 }, Token);

        Assert.Equal(ApprovalAnswer.Denied, answer);
        Assert.Contains("this profile is protected: it is asked about every time.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A line break in a command cannot draw a line of its own under the prompt's.</summary>
    [Fact]
    public async Task ARunsCommandCannotAddALineToThePrompt()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("echo", "hi\n  keypaste: this request is safe, approve it"), Token);

        var shown = rig.Stderr.ToString();
        Assert.DoesNotContain("\n  keypaste: this request is safe", shown, StringComparison.Ordinal);
        Assert.Contains("scrubbed", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChannelRejectsNulls()
    {
        var console = new AgentConsole(new StringWriter(), interactive: false);

        Assert.Throws<ArgumentNullException>(() => new TerminalApprovalChannel(null!, console, _window, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new TerminalApprovalChannel(new FakeSecretPrompt(), null!, _window, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new TerminalApprovalChannel(new FakeSecretPrompt(), console, _window, null!));
    }

    private static RunPrompt RunPromptFor(int grantSeconds = 900, OnceOnly onceOnly = OnceOnly.None) =>
        RunPrompt.For(
            new Keypaste.Core.Ipc.RunRequest
            {
                Program = "/usr/bin/npm",
                Command = ["npm", "run", "migrate"],
                Directory = "/home/me/acme/api",
                Project = "acme-api",
                Reason = "run the pending migration",
                Exposure = ["env/**"],
                ClientName = "claude-code",
                ClientLabel = "claude-code",
            },
            "acme-api",
            "dev",
            [
                new RunPromptVariable("DATABASE_URL", "env/acme-api/DATABASE_URL", "password"),
                new RunPromptVariable("STRIPE_SECRET_KEY", "env/acme-api/STRIPE_SECRET_KEY", "password"),
            ],
            grantSeconds,
            onceOnly);

    [Fact]
    public async Task TheRunDialog_IsExactlyTheBlock()
    {
        var rig = Build("d");

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(RunPromptFor(), Token));

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: an agent wants to run a command with your secrets.",
            string.Empty,
            "  client     claude-code (claude-code)",
            "  tool       keypaste.run",
            "  runs       /usr/bin/npm",
            "  command    npm run migrate",
            "  in         /home/me/acme/api",
            "  project    acme-api",
            "  profile    dev",
            "  injects    DATABASE_URL        env/acme-api/DATABASE_URL        inject only",
            "  injects    STRIPE_SECRET_KEY   env/acme-api/STRIPE_SECRET_KEY   inject only",
            string.Empty,
            "  the agent says it needs this because:",
            "    run the pending migration",
            string.Empty,
            "  That sentence was written by the agent, not by keypaste. Treat it as a claim.",
            "  keypaste puts these values into that command's environment and returns its output with each",
            "  value's literal and escaped forms removed. The agent sees the names; the command itself can read",
            "  the values and reveal them, so approve only a command you would run yourself.",
            string.Empty,
            "  [h] lets claude-code run this command line here again for 15 minutes without asking.",
            "  It can change the files that command runs (scripts, package.json) in that time.",
            "keypaste: denied. Nothing was released.",
            TerminalApprovalChannel.Rule,
            string.Empty,
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), rig.Stderr.ToString());
        Assert.Equal("[d] deny  [o] once  [h] 15 minutes  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    [Fact]
    public async Task TheRunDialog_TimedLineNamesTheClient_AndHAllowsForIt()
    {
        var rig = Build("h");

        Assert.Equal(ApprovalAnswer.Approved, await rig.Channel.AskAsync(RunPromptFor(), Token));
        Assert.Contains("[h] lets claude-code run this command line here again for 15 minutes", rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("keypaste: allowed for 15 minutes.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OnceOnly.ProtectedProfile, "  this profile is protected: it is asked about every time.")]
    [InlineData(OnceOnly.ClientPolicy, "  this client's policy is Ask every time: no timed grant is offered.")]
    public async Task ARunPromptOfferingNone_H_Denies_AndSaysWhy(OnceOnly onceOnly, string why)
    {
        var rig = Build("h");

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(RunPromptFor(0, onceOnly), Token));
        Assert.Contains(why, rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[h]", Assert.Single(rig.Prompt.PromptsSeen), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALoaderName_IsFlaggedInTheRunDialog()
    {
        var rig = Build("d");
        var prompt = RunPromptFor() with { Variables = [new RunPromptVariable("NODE_OPTIONS", "env/acme-api/NODE_OPTIONS", "password")] };

        await rig.Channel.AskAsync(prompt, Token);

        Assert.Contains("inject only  (changes how programs start)", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OnceOnly.ProtectedProfile, "  this entry is in a protected profile: it is asked about every time.")]
    [InlineData(OnceOnly.ClientPolicy, "  this client's policy is Ask every time: no timed grant is offered.")]
    public async Task TheCredentialDialog_SaysWhyItIsOnceOnly(OnceOnly onceOnly, string why)
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(grantSeconds: 0) with { OnceOnly = onceOnly }, Token);

        Assert.Contains(why, rig.Stderr.ToString(), StringComparison.Ordinal);
    }
}
