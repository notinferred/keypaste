using System.Globalization;
using Keypaste.Cli.Prompting;
using Keypaste.Cli.Styling;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;

namespace Keypaste.Cli.Approval;

/// <summary>
/// Asks the human in the terminal they started <c>keypaste approve</c> in.
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
/// <c>h</c> allows for the timed allowance when one is offered, <c>a</c> allows once and then asks to make
/// the entry's project environment Automatic when that is offered, and every other answer, Enter and
/// silence included, is a denial (D-0326).
/// </para>
/// <para>
/// A change that gives an agent more is confirmed the same way, with <c>y</c> the only key that
/// allows it, armed as the others are; the request's origin is shown as a claim and its reason under
/// a line saying who wrote it. When the change <c>a</c> asked for does not take effect, a line says
/// so, and why, once the release it allowed has left.
/// </para>
/// </remarks>
internal sealed class TerminalApprovalChannel : IApprovalChannel
{
    internal const string Rule = "────────────────────────────────────────────────────────────";

    private const string _choices = "oh";
    private const string _alwaysChoices = "oha";
    private const string _confirmChoices = "y";

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
        return AnswerAsync(request.TtlSeconds, started, cancellationToken, request.AlwaysFor);
    }

    public async ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));

        char? choice;

        try
        {
            choice = await ChooseAsync(_confirmChoices, () => Confirmation(Remaining(started)), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Withdrawn("Nothing was changed.");
        }

        var answer = choice == 'y' ? ApprovalAnswer.ApprovedOnce : ApprovalAnswer.Denied;

        _console.WriteLine(string.Join(
            Environment.NewLine,
            answer == ApprovalAnswer.ApprovedOnce
                ? _console.Paint(Tone.Ok, request.StaysBeneathDefaults
                    ? $"keypaste: allowed. {request.Agent} gets no more than the defaults until its saved settings are fixed."
                    : $"keypaste: allowed. {request.Agent} gets it from its next request.")
                : _console.Paint(Tone.Danger, "keypaste: denied. Nothing was changed."),
            RuleLine));

        return answer;
    }

    public async ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));

        char? choice;

        try
        {
            choice = await ChooseAsync(_confirmChoices, () => Confirmation(Remaining(started)), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Withdrawn("Nothing was written.");
        }

        var answer = choice == 'y' ? ApprovalAnswer.ApprovedOnce : ApprovalAnswer.Denied;

        _console.WriteLine(string.Join(
            Environment.NewLine,
            answer == ApprovalAnswer.ApprovedOnce
                ? _console.Paint(Tone.Ok, "keypaste: allowed. Saving it in the vault.")
                : _console.Paint(Tone.Danger, "keypaste: denied. Nothing was written."),
            RuleLine));

        return answer;
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

    /// <summary>The vault <c>keypaste clients</c> must be pointed at to reach this owner, or null when it finds this one unasked.</summary>
    internal string? Vault { get; init; }

    /// <summary>The pipe <c>keypaste clients</c> must be pointed at to reach this owner, or null when it finds this one unasked.</summary>
    internal string? Approver { get; init; }

    public void Tell(AlwaysForNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var again = RetryCommand(notice) is { } command
            ? $"To ask for it again: {command}"
            : "`keypaste clients` can ask for it again.";

        _console.WriteLine(_console.Paint(Tone.Danger, $"keypaste: allowed once. {notice.Unmade} {again}"));
    }

    /// <summary>
    /// The command that asks again for what Always for asked, reaching this owner, when each name in it is
    /// one a shell passes on as written and no command line reads as an option; otherwise null.
    /// </summary>
    private string? RetryCommand(AlwaysForNotice notice)
    {
        string?[] named = [notice.Agent, notice.Scope, Vault, Approver];

        if (!named.All(name => name is null || IsPlain(name)))
        {
            return null;
        }

        return $"keypaste clients {notice.Agent} --project {notice.Scope} --mode automatic"
            + (Vault is { } vault ? $" --vault {vault}" : string.Empty)
            + (Approver is { } approver ? $" --approver {approver}" : string.Empty);
    }

    private static bool IsPlain(string name) =>
        name.Length > 0
        && name[0] != '-'
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/');

    /// <summary>The timed allowance an env prompt offers: none to a requester that is not the person's own run.</summary>
    private static int OfferedSeconds(EnvReleasePrompt request) =>
        request.Requester is null ? request.GrantSeconds : 0;

    private async ValueTask<ApprovalAnswer> AnswerAsync(int grantSeconds, long started, CancellationToken cancellationToken, ProjectScope? alwaysFor = null)
    {
        char? choice;

        try
        {
            choice = await ChooseAsync(
                alwaysFor is null ? _choices : _alwaysChoices,
                () => Choice(grantSeconds, alwaysFor, Remaining(started)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Withdrawn("Nothing was released.");
        }

        var answer = choice switch
        {
            'o' => ApprovalAnswer.ApprovedOnce,
            'h' when grantSeconds > 0 => ApprovalAnswer.Approved,
            'a' when alwaysFor is not null => ApprovalAnswer.AlwaysForProject,
            _ => ApprovalAnswer.Denied,
        };

        _console.WriteLine(string.Join(
            Environment.NewLine,
            answer switch
            {
                ApprovalAnswer.Approved => _console.Paint(Tone.Ok, $"keypaste: allowed for {ApprovalLimits.Describe(grantSeconds)}."),
                ApprovalAnswer.ApprovedOnce => _console.Paint(Tone.Ok, "keypaste: allowed once."),
                ApprovalAnswer.AlwaysForProject => _console.Paint(Tone.Ok, $"keypaste: allowed once. Confirm below to make {alwaysFor!.Name} Automatic."),
                _ => _console.Paint(Tone.Danger, "keypaste: denied. Nothing was released."),
            },
            RuleLine));

        return answer;
    }

    /// <summary>Reads one choice under a countdown, the choice line drawn and cleared around it.</summary>
    /// <exception cref="OperationCanceledException">The question was withdrawn.</exception>
    private async ValueTask<char?> ChooseAsync(string choices, Func<string> choiceLine, CancellationToken cancellationToken)
    {
        // Checked here rather than left to WaitAsync, which short-circuits on an already
        // completed task and never looks at the token: a read that finished before the await got
        // here would be answered as though nobody had withdrawn the request.
        cancellationToken.ThrowIfCancellationRequested();

        _console.BeginChoice(choiceLine);

        // A terminal read polls the token and stops within a poll interval; a redirected read
        // blocks on its pipe, so the wait is what gets cancelled and that reader is abandoned.
        var reading = Task.Run(() => _prompt.ReadChoice(choiceLine, choices, cancellationToken), CancellationToken.None);

        try
        {
            return await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private ApprovalAnswer Withdrawn(string nothing)
    {
        _console.WriteLine(string.Join(
            Environment.NewLine,
            string.Empty,
            _console.Paint(Tone.Danger, $"keypaste: the request was withdrawn before you answered. {nothing}"),
            RuleLine));
        return ApprovalAnswer.Denied;
    }

    /// <summary>The choice line: what each key does, and how long is left to press one, at one width while it counts down.</summary>
    /// <remarks>Its colours, when stderr shows any, are the same escapes on every redraw, so the width stays one.</remarks>
    private string Choice(int grantSeconds, ProjectScope? alwaysFor, int secondsLeft) =>
        _console.Paint(Tone.Accent, "[d] deny  [o] once  ")
        + (grantSeconds > 0 ? _console.Paint(Tone.Ok, $"[h] {ApprovalLimits.Describe(grantSeconds)}") + "  " : string.Empty)
        + (alwaysFor is not null ? _console.Paint(Tone.Ok, $"[a] always for {alwaysFor.Name}") + "  " : string.Empty)
        + _console.Paint(Tone.Accent, string.Create(CultureInfo.InvariantCulture, $"{secondsLeft,2}s {_console.Glyph(Mark.Prompt)}"))
        + " ";

    /// <summary>The confirmation's choice line: deny first, and the one key that allows.</summary>
    private string Confirmation(int secondsLeft) =>
        _console.Paint(Tone.Accent, "[d] deny  ")
        + _console.Paint(Tone.Ok, "[y] allow")
        + "  "
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
            $"  project      {request.Project}",
            $"  environment  {request.Profile}",
        ];

        if (request.Requester is { } requester)
        {
            lines.Add($"  asked by     {requester}");
        }

        lines.Add($"  variables    {(request.Keys.Count == 0 ? "(none)" : string.Join(' ', request.Keys))}");
        lines.AddRange(request.FileLines.Select(line => $"  injects      {line}"));

        if (request.Entries.Count > 0)
        {
            lines.Add($"  from         {string.Join(", ", request.Entries.Distinct(StringComparer.Ordinal))}");
        }

        lines.Add($"  command      {request.Command}");
        lines.Add($"  in           {request.Directory}");

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
            lines.Add("  this environment is protected: it is asked about every time.");
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
            $"  agent    {request.Client}",
            $"  item     {request.Entry}",
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
                ? "  The item and the reason above are not what the vault holds: both were scrubbed."
                : request.EntryWasAltered
                    ? "  The item above is not what the vault holds: the stored name was scrubbed."
                    : "  The reason above is not what the agent sent: it was scrubbed.");
        }

        lines.Add(string.Empty);
        lines.Add("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");
        lines.Add(string.Empty);

        if (request.TtlSeconds == 0)
        {
            lines.Add(request.OnceOnly switch
            {
                OnceOnly.AskEveryTime => _askEveryTimeLine,
                OnceOnly.TotpCode => "  a TOTP code changes every period: no timed allowance is offered.",
                _ => "  this item is in a protected environment: it is asked about every time.",
            });
        }

        if (request.AlwaysFor is { } scope)
        {
            lines.Add($"  [a] allows this once, then asks you to make {scope.Name} Automatic for {request.Label ?? request.Client}:");
            lines.Add("      released without asking you from then on, and recorded in history.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private const string _askEveryTimeLine = "  this agent is set to Ask every time: no timed allowance is offered.";

    /// <summary>The confirmation of a change that gives an agent more: who, what it gains, where the request says it came from, and its reason last.</summary>
    internal static string Render(AccessChangePrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: a change would give an agent more.",
            string.Empty,
            $"  agent    {request.Agent}",
            $"  asked    {request.OriginText}",
        ];

        lines.AddRange(request.Gains.Select((gain, index) => (index == 0 ? "  gains    " : "           ") + gain));

        if (request.Reason.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  the request says it is needed because:");
            lines.Add($"    {request.Reason}");

            if (request.ReasonWasTruncated)
            {
                lines.Add("    (cut short — the full text is hashed in the audit log)");
            }

            if (request.ReasonWasAltered)
            {
                lines.Add("  The reason above is not what was sent: it was scrubbed.");
            }

            lines.Add("  That sentence was not written by keypaste. Treat it as a claim.");
        }

        lines.Add(string.Empty);
        lines.Add("  [d] deny: nothing changes.");
        lines.Add(request switch
        {
            { ResetsCeilings: true } => $"  [y] allow: {AccessChangePrompt.ResetText}.",
            { StaysBeneathDefaults: true } => $"  [y] allow: saved in the vault, but {AccessChangePrompt.BeneathDefaultsText}.",
            _ => "  [y] allow: keypaste saves it in the vault, and the agent gets it from its next request.",
        });

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A terminal's edit: the command, the item, where it says it came from and what changes, never a value.</summary>
    internal static string Render(TerminalEditPrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: a terminal asks to change this vault.",
            string.Empty,
            $"  command  {request.Command}",
            $"  item     {request.Item}",
            $"  asked    {request.OriginText}",
        ];

        lines.AddRange(request.Changes.Select((change, index) => (index == 0 ? "  changes  " : "           ") + change));
        lines.Add(string.Empty);
        lines.Add("  [d] deny: nothing changes.");
        lines.Add("  [y] allow: keypaste saves this in the vault.");

        return string.Join(Environment.NewLine, lines);
    }

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
            $"  agent        {client}",
            $"  tool         {RunPrompt.ToolName}",
            $"  runs         {request.Program}",
            $"  command      {request.Command}",
            $"  in           {request.Directory}",
            $"  project      {request.Project}",
            $"  environment  {request.Profile}",
        ];

        lines.AddRange(request.Variables.Select(variable =>
            $"  injects      {variable.Name.PadRight(nameWidth)}   {Source(variable).PadRight(entryWidth)}   inject only"
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
            OnceOnly.AskEveryTime => _askEveryTimeLine,
            _ => "  this environment is protected: it is asked about every time.",
        });

        return string.Join(Environment.NewLine, lines);
    }

    private static string Source(RunPromptVariable variable) =>
        string.Equals(variable.Field, "password", StringComparison.Ordinal) ? variable.Entry : $"{variable.Entry} · {variable.Field}";
}
