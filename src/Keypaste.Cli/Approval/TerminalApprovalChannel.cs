using System.Globalization;
using Keypaste.Cli.Prompting;
using Keypaste.Cli.Styling;
using Keypaste.Core.Approval;

namespace Keypaste.Cli.Approval;

/// <summary>
/// Asks the human in the terminal they started <c>keypaste agent</c> in.
/// </summary>
/// <remarks>
/// <para>
/// This is why the approver is a separate process at all. An MCP server's stdin and stdout
/// <em>are</em> the JSON-RPC stream and Claude Desktop starts it with no terminal, so a prompt
/// cannot live there — and reaching for the controlling terminal instead would mean
/// <c>/dev/tty</c>, two incompatible <c>termios</c> layouts, and a <c>stty -echo</c> that leaves
/// somebody's shell typing invisibly if the process dies mid-prompt. Putting the prompt in a
/// process whose stdin already <em>is</em> a terminal does not solve that problem; it deletes it
/// (DECISIONS.md D-0023).
/// </para>
/// <para>
/// Everything goes to stderr, like every other prompt in keypaste (D-0009), so stdout stays
/// data-only, and all of it through the agent's one <see cref="AgentConsole"/>, so no other line
/// can splice into the dialog or its choice line.
/// </para>
/// <para>
/// <b>Rendering rules, which are THREATS.md T-2's mitigation and not decoration.</b> The reason is
/// already sanitized and capped by <see cref="ApprovalPrompt"/> — no control characters, no
/// newlines, no bidirectional overrides — so it cannot draw a second dialog inside this one or move
/// the question off the screen. It is printed last, under a line saying who wrote it, and it is
/// never interpolated into anything that gets parsed. The default is no: <c>o</c> allows once,
/// <c>h</c> allows for the timed grant when one is offered, and every other answer, Enter and
/// silence included, is a denial (D-0326).
/// </para>
/// </remarks>
internal sealed class TerminalApprovalChannel : IApprovalChannel
{
    internal const string Rule = "────────────────────────────────────────────────────────────";

    private const string _choices = "oh";

    private readonly ISecretPrompt _prompt;
    private readonly AgentConsole _console;
    private readonly TimeSpan _window;
    private readonly TimeProvider _clock;

    /// <summary>Builds the channel.</summary>
    /// <param name="prompt">Where the answer is read.</param>
    /// <param name="console">Where the dialog is written.</param>
    /// <param name="window">The gate's window, which the countdown shows.</param>
    /// <param name="clock">The clock the countdown runs on.</param>
    internal TerminalApprovalChannel(ISecretPrompt prompt, AgentConsole console, TimeSpan window, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(clock);

        _prompt = prompt;
        _console = console;
        _window = window;
        _clock = clock;
    }

    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));
        return AnswerAsync(request.TtlSeconds, started, cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));
        return AnswerAsync(OfferedSeconds(request), started, cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(RunPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));
        return AnswerAsync(request.GrantSeconds, started, cancellationToken);
    }

    /// <summary>The timed grant an env prompt offers: none to a requester that is not the person's own run.</summary>
    private static int OfferedSeconds(EnvReleasePrompt request) =>
        request.Requester is null ? request.GrantSeconds : 0;

    private async ValueTask<ApprovalAnswer> AnswerAsync(int grantSeconds, long started, CancellationToken cancellationToken)
    {
        string ChoiceLine() => Choice(grantSeconds, Remaining(started));

        char? choice;

        try
        {
            // Checked here rather than left to WaitAsync, which short-circuits on an already
            // completed task and never looks at the token: a read that finished before the await got
            // here would be answered as though nobody had withdrawn the request.
            cancellationToken.ThrowIfCancellationRequested();

            _console.BeginChoice(ChoiceLine);

            // A terminal read polls the token and stops within a poll interval; a redirected read
            // blocks on its pipe, so the wait is what gets cancelled and that reader is abandoned.
            var reading = Task.Run(() => _prompt.ReadChoice(ChoiceLine, _choices, cancellationToken), CancellationToken.None);

            try
            {
                choice = await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_prompt.IsInteractive)
            {
                // Its last redraw must land before the line that says the question is gone.
                await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _console.EndChoice();
            }
        }
        catch (OperationCanceledException)
        {
            _console.WriteLine(string.Join(
                Environment.NewLine,
                string.Empty,
                _console.Paint(Tone.Danger, "keypaste: the request was withdrawn before you answered. Nothing was released."),
                RuleLine));
            return ApprovalAnswer.Denied;
        }

        var answer = choice switch
        {
            'o' => ApprovalAnswer.ApprovedOnce,
            'h' when grantSeconds > 0 => ApprovalAnswer.Approved,
            _ => ApprovalAnswer.Denied,
        };

        _console.WriteLine(string.Join(
            Environment.NewLine,
            answer switch
            {
                ApprovalAnswer.Approved => _console.Paint(Tone.Ok, $"keypaste: allowed for {ApprovalLimits.Describe(grantSeconds)}."),
                ApprovalAnswer.ApprovedOnce => _console.Paint(Tone.Ok, "keypaste: allowed once."),
                _ => _console.Paint(Tone.Danger, "keypaste: denied. Nothing was released."),
            },
            RuleLine));

        return answer;
    }

    /// <summary>The choice line: what each key does, and how long is left to press one, at one width while it counts down.</summary>
    /// <remarks>Its colours, when stderr shows any, are the same escapes on every redraw, so the width stays one.</remarks>
    private string Choice(int grantSeconds, int secondsLeft) =>
        _console.Paint(Tone.Accent, "[d] deny  [o] once  ")
        + (grantSeconds > 0 ? _console.Paint(Tone.Ok, $"[h] {ApprovalLimits.Describe(grantSeconds)}") + "  " : string.Empty)
        + _console.Paint(Tone.Accent, string.Create(CultureInfo.InvariantCulture, $"{secondsLeft,2}s {_console.Glyph(Mark.Prompt)}"))
        + " ";

    private string RuleLine => _console.Paint(Tone.Accent, Rule);

    private int Remaining(long started)
    {
        var left = _window - _clock.GetElapsedTime(started);
        return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
    }

    private static string Render(EnvReleasePrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: `keypaste run --session` is asking for a project's variables.",
            string.Empty,
            $"  project    {request.Project}",
            $"  profile    {request.Profile}",
        ];

        if (request.Requester is { } requester)
        {
            lines.Add($"  asked by   {requester}");
        }

        lines.Add($"  variables  {(request.Keys.Count == 0 ? "(none)" : string.Join(' ', request.Keys))}");
        lines.AddRange(request.FileLines.Select(line => $"  injects    {line}"));
        lines.Add($"  command    {request.Command}");
        lines.Add($"  in         {request.Directory}");

        if (request.CommandWasAltered || request.DirectoryWasAltered)
        {
            lines.Add(string.Empty);
            lines.Add("  Characters that cannot be shown were scrubbed from the command or directory above.");
        }

        lines.Add(string.Empty);
        lines.Add("  No value is shown here. `keypaste run --session` names the command and keypaste cannot check it,");
        lines.Add("  so allow only a run you started.");
        lines.Add(string.Empty);
        lines.Add("  [d] deny: the run gets nothing.");
        lines.Add("  [o] once: the run starts this command one time with every value in its environment.");

        var offered = OfferedSeconds(request);

        if (offered > 0)
        {
            var duration = ApprovalLimits.Describe(offered);
            lines.Add($"  [h] {duration}: the same, and any program of yours can run exactly this command here again for {duration} without asking.");
        }
        else if (request.Requester is null)
        {
            lines.Add("  this profile is protected: it is asked about every time.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Render(ApprovalPrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: an agent is asking for a credential.",
            string.Empty,
            $"  client   {request.Client}",
            $"  entry    {request.Entry}",
            $"  field    {request.Field}",
            string.Empty,
            "  the agent says it needs this because:",
            $"    {request.Reason}",
        ];

        if (request.ReasonWasTruncated)
        {
            lines.Add("    (cut short — the full text is hashed in the audit log)");
        }

        if (request.EntryWasAltered || request.ReasonWasAltered)
        {
            lines.Add(string.Empty);
            lines.Add(request.EntryWasAltered && request.ReasonWasAltered
                ? "  The entry and the reason above are not what the vault holds: both were scrubbed."
                : request.EntryWasAltered
                    ? "  The entry above is not what the vault holds: the stored name was scrubbed."
                    : "  The reason above is not what the agent sent: it was scrubbed.");
        }

        lines.Add(string.Empty);
        lines.Add("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");
        lines.Add(string.Empty);

        if (request.TtlSeconds == 0)
        {
            lines.Add(request.OnceOnly == OnceOnly.ClientPolicy
                ? _clientPolicyLine
                : "  this entry is in a protected profile: it is asked about every time.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private const string _clientPolicyLine = "  this client's policy is Ask every time: no timed grant is offered.";

    /// <summary>The run dialog: who, the exact program and command line, where, each variable and its source, and the agent's claim last.</summary>
    internal static string Render(RunPrompt request, string rule)
    {
        var client = request.Label is { } label ? $"{request.Client} ({label})" : request.Client;
        var nameWidth = request.Variables.Max(variable => variable.Name.Length);
        var entryWidth = request.Variables.Max(variable => Source(variable).Length);

        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: an agent wants to run a command with your secrets.",
            string.Empty,
            $"  client     {client}",
            $"  tool       {RunPrompt.ToolName}",
            $"  runs       {request.Program}",
            $"  command    {request.Command}",
            $"  in         {request.Directory}",
            $"  project    {request.Project}",
            $"  profile    {request.Profile}",
        ];

        lines.AddRange(request.Variables.Select(variable =>
            $"  injects    {variable.Name.PadRight(nameWidth)}   {Source(variable).PadRight(entryWidth)}   inject only"
            + (variable.ChangesHowProgramsStart ? "  (changes how programs start)" : string.Empty)));

        lines.Add(string.Empty);
        lines.Add("  the agent says it needs this because:");
        lines.Add($"    {request.Reason}");

        if (request.ReasonWasTruncated)
        {
            lines.Add("    (cut short — the full text is hashed in the audit log)");
        }

        if (request.ReasonWasAltered)
        {
            lines.Add(string.Empty);
            lines.Add("  The reason above is not what the agent sent: it was scrubbed.");
        }

        lines.Add(string.Empty);
        lines.Add("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");
        lines.Add("  keypaste puts these values into that command's environment and returns its output with each");
        lines.Add("  value's literal and escaped forms removed. The agent sees the names; the command itself can read");
        lines.Add("  the values and reveal them, so approve only a command you would run yourself.");
        lines.Add(string.Empty);

        lines.Add(request.OnceOnly switch
        {
            _ when request.GrantSeconds > 0 =>
                $"  [h] lets {request.Label ?? request.Client} run this command line here again for {ApprovalLimits.Describe(request.GrantSeconds)} without asking."
                + Environment.NewLine
                + "  It can change the files that command runs (scripts, package.json) in that time.",
            OnceOnly.ClientPolicy => _clientPolicyLine,
            _ => "  this profile is protected: it is asked about every time.",
        });

        return string.Join(Environment.NewLine, lines);
    }

    private static string Source(RunPromptVariable variable) =>
        string.Equals(variable.Field, "password", StringComparison.Ordinal) ? variable.Entry : $"{variable.Entry} · {variable.Field}";
}
