using System.Text;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What a human actually sees before they decide, and which answers release anything.
/// </summary>
/// <remarks>
/// This is the display half of THREATS.md T-2. The reason arrives already sanitized and capped, so
/// what is under test here is that the channel does not undo that — and that the default is no:
/// <c>o</c> allows once, <c>a</c> asks to always allow when that is offered, and everything
/// else denies.
/// </remarks>
public sealed class TerminalApprovalChannelTests
{
    private static readonly TimeSpan _window = TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AlwaysOffer Offer(string scope)
    {
        Assert.True(ProjectScope.TryParseName(scope, out var parsed, out var error), error);

        return new AlwaysOffer(AccessEdit.Automatic(parsed), parsed.Name);
    }

    private static ApprovalPrompt Prompt(string reason = "deploy billing to staging") =>
        ApprovalPrompt.For("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", reason);

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

    /// <summary>Paints by marking each tone, so a test sees what was coloured and how without reading escapes.</summary>
    private sealed class MarkingStyle(TextWriter painted) : Styling.IConsoleStyle
    {
        public void Alarm(TextWriter writer, string text) => writer.WriteLine(text);

        public string Paint(TextWriter writer, Styling.Tone tone, string text) =>
            ReferenceEquals(writer, painted) ? $"<{tone}>{text}</>" : text;
    }

    /// <summary>
    /// The credential prompt names the agent as the run prompt does: the client it says it is, then the
    /// label its bridge was started with, which its settings follow; with no label, the client alone.
    /// </summary>
    [Fact]
    public async Task TheCredentialPrompt_NamesTheClientAndItsLabel()
    {
        var rig = Build("d");
        var prompt = ApprovalPrompt.For(
            "claude-code", new EntryName("env/demo", "STRIPE_KEY"), "password", "deploy the billing service to staging", "ci-probe");

        await rig.Channel.AskAsync(prompt, Token);

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: an agent is asking for a credential.",
            string.Empty,
            "  agent    claude-code (ci-probe)",
            "  item     env/demo/STRIPE_KEY",
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
    }

    [Fact]
    public async Task AnEntryNameTheSanitizerChanged_SaysSo()
    {
        var rig = Build("d");
        var spoofed = new EntryName("env/dev", "STRIPE" + ((char)0x202E) + "KEY");

        await rig.Channel.AskAsync(ApprovalPrompt.For("claude-code", spoofed, "password", "deploy"), Token);

        Assert.Contains("not what the vault holds", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryEntryName_SaysNothingExtra()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt(), Token);

        Assert.DoesNotContain("not what the vault holds", rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("protected environment", rig.Stderr.ToString(), StringComparison.Ordinal);
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
        };

    [Fact]
    public async Task TheEnvDialog_NamesTheProfile_AndRequester()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("deploy") with { Profile = "staging", Requester = "token ci-deploy" }, Token);

        var shown = rig.Stderr.ToString();
        Assert.Contains(
            "  project      billing" + Environment.NewLine + "  environment  staging" + Environment.NewLine + "  asked by     token ci-deploy" + Environment.NewLine,
            shown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnvDialog_ShowsWhatAReferenceFileInjects()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(EnvPrompt("deploy") with { FileLines = ["DB ← DATABASE_URL", "MODE=ci"] }, Token);

        Assert.Contains(
            "  variables    DATABASE_URL STRIPE_KEY" + Environment.NewLine + "  injects      DB ← DATABASE_URL" + Environment.NewLine + "  injects      MODE=ci" + Environment.NewLine,
            rig.Stderr.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEnvRun_SaysItIsAskedEveryTime_AndHDenies()
    {
        var rig = Build("h");

        var answer = await rig.Channel.AskAsync(EnvPrompt("deploy") with { Profile = "prod" }, Token);

        Assert.Equal(ApprovalAnswer.Denied, answer);
        Assert.Contains("a run names no agent to keep an always under, so it is asked about every time.", rig.Stderr.ToString(), StringComparison.Ordinal);
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

    private static PairingPrompt PairingPromptFor(string name = "laptop") => new()
    {
        Identity = Keypaste.Core.Browser.BrowserIdentity.ChromeWebStore,
        SuggestedName = name,
        TakenNames = [],
        NameChosen = true,
    };

    /// <summary>A browser's request to pair names the build it claims and the pairing's name, and only y pairs it (D-0634).</summary>
    [Theory]
    [InlineData("y", ApprovalAnswer.ApprovedOnce)]
    [InlineData("", ApprovalAnswer.Denied)]
    [InlineData("o", ApprovalAnswer.Denied)]
    [InlineData("d", ApprovalAnswer.Denied)]
    public async Task APairing_NamesTheBuildAndTheName_AndOnlyYPairs(string answer, ApprovalAnswer expected)
    {
        var rig = Build(answer);

        var said = await rig.Channel.AskAsync(PairingPromptFor(), Token);

        Assert.Equal(expected, said.Answer);
        Assert.Equal(expected == ApprovalAnswer.ApprovedOnce ? "laptop" : null, said.Name);
        var shown = rig.Stderr.ToString();
        Assert.Contains("keypaste: a browser asks to pair with this vault.", shown, StringComparison.Ordinal);
        Assert.Contains("  browser  KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)", shown, StringComparison.Ordinal);
        Assert.Contains("  name     laptop", shown, StringComparison.Ordinal);
        Assert.Contains("any program you run can name it too", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequestToPairWhileNotArmed_SaysHowToPair()
    {
        var rig = Build();

        rig.Channel.Tell(new PairingNotice(Keypaste.Core.Browser.BrowserIdentity.FirefoxAddOns));

        var said = rig.Stderr.ToString();
        Assert.Contains("KeePassXC-Browser from Firefox Add-ons (keepassxc-browser@keepassxc.org) asked to pair, and was refused", said, StringComparison.Ordinal);
        Assert.Contains("--pair-browser <name>", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_AllowsOnceAndChoosesAlways_WhereItIsOffered_AndTheLineSaysExactlyThreeThings()
    {
        var rig = Build("a");
        var offered = Prompt() with { Label = "claude-code", Always = new AlwaysOffer(AccessEdit.AlwaysItem("0123456789abcdef0123456789abcdef"), "env/dev/STRIPE_KEY") };

        Assert.Equal(ApprovalAnswer.Always, await rig.Channel.AskAsync(offered, Token));
        Assert.Contains("[d] deny  [o] once  [a] always", Assert.Single(rig.Prompt.PromptsSeen), StringComparison.Ordinal);
        Assert.DoesNotContain("[h]", rig.Prompt.PromptsSeen[0], StringComparison.Ordinal);
        Assert.Contains("[a] allows this once, then asks you to always allow this item for claude-code", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Denies_WhereNothingOffersIt_AndH_IsNoAnswerAtAll()
    {
        var rig = Build("a");

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt() with { OnceOnly = OnceOnly.ProtectedProfile }, Token));
        Assert.DoesNotContain("[a]", Assert.Single(rig.Prompt.PromptsSeen), StringComparison.Ordinal);

        var hour = Build("h");

        Assert.Equal(ApprovalAnswer.Denied, await hour.Channel.AskAsync(Prompt(), Token));
    }

    [Fact]
    public async Task AnEnvRun_OffersOnceOnly()
    {
        var rig = Build("o");

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(EnvPrompt("deploy"), Token));
        Assert.DoesNotContain("[a]", Assert.Single(rig.Prompt.PromptsSeen), StringComparison.Ordinal);
        Assert.Contains("[d] deny  [o] once", rig.Prompt.PromptsSeen[0], StringComparison.Ordinal);
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

        Assert.Equal("[d] deny  [o] once   5s › ", Assert.Single(prompt.PromptsSeen));
    }

    [Theory]
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
        Assert.Equal("<Accent>[d] deny  [o] once  </><Accent>45s ›</> ", Assert.Single(prompt.PromptsSeen));
    }

    /// <summary>
    /// The dialog for a bridge started with no label, which names the client alone: the <c>for</c>
    /// line is gone and nothing else moved.
    /// </summary>
    [Fact]
    public async Task TheDialog_HasNoForLine_AndIsOtherwiseUnchanged()
    {
        var rig = Build("d");
        var prompt = ApprovalPrompt.For(
            "claude-code", new EntryName("env/demo", "STRIPE_KEY"), "password", "deploy the billing service to staging");

        await rig.Channel.AskAsync(prompt, Token);

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: an agent is asking for a credential.",
            string.Empty,
            "  agent    claude-code",
            "  item     env/demo/STRIPE_KEY",
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
        Assert.Equal("[d] deny  [o] once  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    [Fact]
    public async Task TheChoiceMark_FallsBackOnACodePageWithoutIt()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue("d");
        using var stderr = new StreamWriter(new MemoryStream(), Encoding.GetEncoding(437));

        await Channel(prompt, stderr).AskAsync(Prompt(), Token);

        Assert.Equal("[d] deny  [o] once  45s > ", Assert.Single(prompt.PromptsSeen));
    }

    [Fact]
    public async Task ARunsRequest_ShowsTheProjectNamesCommandAndDirectory_AndOnlyAnAllowReleases()
    {
        var once = Build("o");
        var nothing = Build(string.Empty);

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await once.Channel.AskAsync(EnvPrompt("npm", "run", "deploy"), Token));
        Assert.Equal(ApprovalAnswer.Denied, await nothing.Channel.AskAsync(EnvPrompt("npm", "run", "deploy"), Token));

        var shown = once.Stderr.ToString();
        Assert.Contains("project      billing", shown, StringComparison.Ordinal);
        Assert.Contains("environment  dev", shown, StringComparison.Ordinal);
        Assert.Contains("variables    DATABASE_URL STRIPE_KEY", shown, StringComparison.Ordinal);
        Assert.Contains("from         env/billing/DATABASE_URL, env/billing/STRIPE_KEY", shown, StringComparison.Ordinal);
        Assert.Contains("command      npm run deploy", shown, StringComparison.Ordinal);
        Assert.Contains("in           /home/me/billing", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("scrubbed", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("asked by", shown, StringComparison.Ordinal);
    }

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
            "  agent        claude-code (claude-code)",
            "  tool         keypaste.run",
            "  runs         /usr/bin/npm",
            "  command      npm run migrate",
            "  in           /home/me/acme/api",
            "  project      acme-api",
            "  environment  dev",
            "  injects      DATABASE_URL        env/acme-api/DATABASE_URL        inject only",
            "  injects      STRIPE_SECRET_KEY   env/acme-api/STRIPE_SECRET_KEY   inject only",
            string.Empty,
            "  the agent says it needs this because:",
            "    run the pending migration",
            string.Empty,
            "  That sentence was written by the agent, not by keypaste. Treat it as a claim.",
            "  keypaste puts these values into that command's environment and returns its output with each",
            "  value's literal and escaped forms removed. The agent sees the names; the command itself can read",
            "  the values and reveal them, so approve only a command you would run yourself.",
            string.Empty,
            "keypaste: denied. Nothing was released.",
            TerminalApprovalChannel.Rule,
            string.Empty,
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), rig.Stderr.ToString());
        Assert.Equal("[d] deny  [o] once  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    private static RunPrompt RunPromptFor(OnceOnly onceOnly = OnceOnly.None) =>
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
            onceOnly);

    [Fact]
    public async Task ALoaderName_IsFlaggedInTheRunDialog()
    {
        var rig = Build("d");
        var prompt = RunPromptFor() with { Variables = [new RunPromptVariable("NODE_OPTIONS", "env/acme-api/NODE_OPTIONS", "password")] };

        await rig.Channel.AskAsync(prompt, Token);

        Assert.Contains("inject only  (changes how programs start)", rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OnceOnly.ProtectedProfile, "  this item is in a protected environment: it is asked about every time.")]
    [InlineData(OnceOnly.LimitReached, "  this agent's Automatic limit was reached: once only, so that limit holds.")]
    [InlineData(OnceOnly.Unnamed, "  This agent has no name, so Always allow is not offered. Start its bridge with")]
    public async Task TheCredentialDialog_SaysWhyItIsOnceOnly(OnceOnly onceOnly, string why)
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Prompt() with { OnceOnly = onceOnly }, Token);

        Assert.Contains(why, rig.Stderr.ToString(), StringComparison.Ordinal);
    }

    private static ApprovalPrompt Offering(string scope = "billing:dev")
    {
        Assert.True(ProjectScope.TryParseName(scope, out var parsed, out var error), error);
        return Prompt() with { Label = "claude-code", Always = new AlwaysOffer(AccessEdit.Automatic(parsed), parsed.Name) };
    }

    /// <summary>
    /// When the change <c>a</c> asked for did not take effect, one line says what was not made Automatic, why
    /// and the exact command that asks again, and asks nothing.
    /// </summary>
    [Fact]
    public void AnAlwaysForThatDidNotTakeEffect_SaysSoAndWhy_AndHowToAskAgain()
    {
        var rig = Build();

        rig.Channel.Tell(new AlwaysNotice("claude-code", Offer("billing:dev"), "nobody answered inside the window"));

        Assert.Equal(
            "keypaste: allowed once. billing:dev was not made Automatic for claude-code: nobody answered inside the window. "
                + "To ask for it again: keypaste clients claude-code --project billing:dev --mode automatic" + Environment.NewLine,
            rig.Stderr.ToString());
        Assert.Empty(rig.Prompt.PromptsSeen);
    }

    /// <summary>
    /// The command names this owner's vault and pipe when <c>keypaste clients</c> would not find them
    /// unasked, and is not printed when a name in it is one a shell would act on or read as an option.
    /// </summary>
    [Theory]
    [InlineData("claude-code", "billing:dev", "/home/you/work.kdbx", "keypaste-test-pipe", " --vault /home/you/work.kdbx --approver keypaste-test-pipe")]
    [InlineData("claude code", "billing:dev", null, null, null)]
    [InlineData("--vault", "billing:dev", null, null, null)]
    [InlineData("claude-code", "billing $(x)", null, null, null)]
    [InlineData("claude-code", "billing:dev", "/home/you/my vault.kdbx", null, null)]
    [InlineData("claude-code", "billing:dev", @"C:\Users\you\vault.kdbx", null, null)]
    public void TheCommandToAskAgain_ReachesThisOwner_AndIsPrintedOnlyWhenSafeToPaste(string agent, string scope, string? vault, string? approver, string? reach)
    {
        var stderr = new StringWriter();
        var channel = new TerminalApprovalChannel(new FakeSecretPrompt(), new AgentConsole(stderr, interactive: false), _window, TimeProvider.System)
        {
            Vault = vault,
            Approver = approver,
        };

        channel.Tell(new AlwaysNotice(agent, Offer(scope), "nobody answered inside the window"));

        Assert.EndsWith(
            reach is null
                ? "`keypaste clients` can ask for it again." + Environment.NewLine
                : $"To ask for it again: keypaste clients {agent} --project {scope} --mode automatic{reach}" + Environment.NewLine,
            stderr.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_WhenNotOffered_Denies_AndIsNotOnTheChoiceLine()
    {
        var rig = Build("a");

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Prompt(), Token));
        Assert.DoesNotContain("[a]", rig.Stderr.ToString() + Assert.Single(rig.Prompt.PromptsSeen), StringComparison.Ordinal);
    }

    private static AccessChangePrompt Change(AccessOrigin origin = AccessOrigin.Terminal, string reason = "") => new()
    {
        Agent = "claude-code",
        Gains = ["by default: Automatic: released without asking, recorded in history", "may run commands, each still asked about"],
        Origin = origin,
        Reason = reason,
        ReasonWasTruncated = false,
        ReasonWasAltered = false,
    };

    /// <summary>The confirmation of a change that gives an agent more: the agent, every gain, where it says it came from as a claim, and only <c>y</c> allows.</summary>
    [Fact]
    public async Task TheChangeConfirmation_NamesTheAgentAndEveryGain_AndYAllows()
    {
        var rig = Build("y");

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(Change(), Token));

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: a change would give an agent more.",
            string.Empty,
            "  agent    claude-code",
            "  asked    asked from a terminal; any program you run could have typed it",
            "  gains    by default: Automatic: released without asking, recorded in history",
            "           may run commands, each still asked about",
            string.Empty,
            "  [d] deny: nothing changes.",
            "  [y] allow: keypaste saves it in the vault, and the agent gets it from its next request.",
            "keypaste: allowed. claude-code gets it from its next request.",
            TerminalApprovalChannel.Rule,
            string.Empty,
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), rig.Stderr.ToString());
        Assert.Equal("[d] deny  [y] allow  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    /// <summary>A change that makes an agent Automatic with a limit names the limit, its end on this computer's clock with that clock's offset.</summary>
    [Fact]
    public async Task TheChangeConfirmation_ShowsTheLimitAutomaticWouldHave_OnThisComputersClock()
    {
        var rig = Build("y", "d");
        var limited = AgentAccess.Defaults with
        {
            Mode = AgentMode.Automatic,
            Limit = new AccessLimit { PerHour = 20, Until = new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero) },
        };
        var row = AgentAccess.Defaults with
        {
            Projects = [new ProjectAccess(Scope("billing:dev"), AgentMode.Automatic, new AccessLimit { PerHour = 5 })],
        };

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(Changing(limited), Token));
        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Changing(row), Token));

        var shown = rig.Stderr.ToString();
        Assert.Contains(
            "  gains    by default: Automatic until 2026-12-31 23:59 +02:00, at most 20 an hour: released without asking, recorded in history",
            shown,
            StringComparison.Ordinal);
        Assert.Contains(
            "  gains    billing:dev: Automatic, at most 5 an hour: released without asking, recorded in history",
            shown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UTC", shown, StringComparison.Ordinal);

        static AccessChangePrompt Changing(AgentAccess proposed) =>
            AccessChangePrompt.For("claude-code", AccessChange.Classify(EffectiveAccess.Defaults, proposed), AccessOrigin.Terminal, string.Empty, TestZones.PlusTwo);

        static ProjectScope Scope(string name) =>
            ProjectScope.TryParseName(name, out var scope, out var error) ? scope : throw new InvalidOperationException(error);
    }

    /// <summary>A change to settings keypaste does not wholly understand says, before and after <c>y</c>, that the agent gets no more than the defaults until they are fixed.</summary>
    [Fact]
    public async Task TheChangeConfirmation_OfSettingsKeypasteDoesNotWhollyUnderstand_SaysItWaitsForThemToBeFixed()
    {
        var rig = Build("y");

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(Change() with { StaysBeneathDefaults = true }, Token));

        var shown = rig.Stderr.ToString();
        Assert.Contains(
            "  [y] allow: saved in the vault, but keypaste does not wholly understand this agent's saved settings, so the agent gets no more than the defaults until they are fixed.",
            shown,
            StringComparison.Ordinal);
        Assert.Contains("keypaste: allowed. claude-code gets no more than the defaults until its saved settings are fixed.", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("next request", shown, StringComparison.Ordinal);
    }

    /// <summary>Enter, a deny key, the keys the credential prompt allows with, and end of input all leave the agent as it was.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("n")]
    [InlineData("o")]
    [InlineData("h")]
    [InlineData("a")]
    [InlineData("yes")]
    public async Task TheChangeConfirmation_DeniesEverythingButY(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Change(), Token));
        Assert.Contains("keypaste: denied. Nothing was changed.", rig.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(ApprovalAnswer.Denied, await Build().Channel.AskAsync(Change(), Token));
    }

    /// <summary>An agent's own reason is shown under a line saying who wrote it, and its origin is the agent's claim, never the person's.</summary>
    [Fact]
    public async Task AnAgentsChangeRequest_ShowsItsReasonAsAClaim()
    {
        var rig = Build("d");

        await rig.Channel.AskAsync(Change(AccessOrigin.Agent, "so deploys stop asking"), Token);

        var shown = rig.Stderr.ToString();
        Assert.Contains("  asked    asked by claude-code through its keypaste tool", shown, StringComparison.Ordinal);
        Assert.Contains("    so deploys stop asking", shown, StringComparison.Ordinal);
        Assert.Contains("  That sentence was not written by keypaste. Treat it as a claim.", shown, StringComparison.Ordinal);
    }

    private static TerminalEditPrompt Edit() => new()
    {
        Command = "keypaste set",
        Item = "work/github",
        Changes = ["Replaces the password of work/github with the password the terminal sent; the old one stays in its history."],
    };

    /// <summary>A terminal's edit: the command, the item, where it says it came from as a claim and what changes, never a value; only <c>y</c> allows.</summary>
    [Fact]
    public async Task ATerminalsEdit_NamesTheCommandItemAndChange_AndYAllows()
    {
        var rig = Build("y");

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await rig.Channel.AskAsync(Edit(), Token));

        string[] expected =
        [
            string.Empty,
            TerminalApprovalChannel.Rule,
            "keypaste: a terminal asks to change this vault.",
            string.Empty,
            "  command  keypaste set",
            "  item     work/github",
            "  asked    asked from a terminal; any program you run could have typed it",
            "  changes  Replaces the password of work/github with the password the terminal sent; the old one stays in its history.",
            string.Empty,
            "  [d] deny: nothing changes.",
            "  [y] allow: keypaste saves this in the vault.",
            "keypaste: allowed. Saving it in the vault.",
            TerminalApprovalChannel.Rule,
            string.Empty,
        ];

        Assert.Equal(string.Join(Environment.NewLine, expected), rig.Stderr.ToString());
        Assert.Equal("[d] deny  [y] allow  45s › ", Assert.Single(rig.Prompt.PromptsSeen));
    }

    /// <summary>A placeholder a new value plants is named on its own line under the change it follows, by keyword only.</summary>
    [Fact]
    public async Task ATerminalsEdit_NamesThePlaceholderItsValuePlants()
    {
        var rig = Build("d");
        var planting = Edit() with
        {
            Changes =
            [
                Edit().Changes[0],
                "Its new password holds {REF:…}, which keypaste does not fill in, so it is refused wherever it would leave.",
            ],
        };

        await rig.Channel.AskAsync(planting, Token);

        Assert.Contains(
            string.Join(
                Environment.NewLine,
                "  changes  Replaces the password of work/github with the password the terminal sent; the old one stays in its history.",
                "           Its new password holds {REF:…}, which keypaste does not fill in, so it is refused wherever it would leave.",
                string.Empty),
            rig.Stderr.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Enter, a deny key, the credential prompt's allow keys and end of input all write nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("o")]
    [InlineData("a")]
    [InlineData("yes")]
    public async Task ATerminalsEdit_DeniesEverythingButY(string answer)
    {
        var rig = Build(answer);

        Assert.Equal(ApprovalAnswer.Denied, await rig.Channel.AskAsync(Edit(), Token));
        Assert.Contains("keypaste: denied. Nothing was written.", rig.Stderr.ToString(), StringComparison.Ordinal);
    }
}
