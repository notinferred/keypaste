using System.Globalization;
using Keypaste.Cli.Prompting;
using Keypaste.Cli.Styling;
using Keypaste.Core;
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
/// never interpolated into anything that gets parsed. The default is no: <c>o</c> allows once, and
/// every other answer, Enter and silence included, is a denial (D-0326). Where the prompt offers them,
/// <c>a</c> and <c>b</c> ask to confirm an Always allow or an Always block with <c>y</c>, and any other
/// answer to that is a denial too.
/// </para>
/// </remarks>
internal sealed class TerminalApprovalChannel : IApprovalChannel
{
    internal const string Rule = "────────────────────────────────────────────────────────────";

    private const string _choices = "o";
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
        return AnswerAsync(started, Always.Of(request, request.Field, blocks: true), cancellationToken);
    }

    /// <summary>Lists the items a request names by number, reads the number of the one the person picks, then asks about that one as any request is asked.</summary>
    public async ValueTask<ItemPick> AskToPickAsync(ApprovalPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        var numbers = string.Concat(Enumerable.Range(1, request.Candidates.Count).Select(number => number.ToString(CultureInfo.InvariantCulture)));
        _console.WriteLine(RenderPicker(request, RuleLine));

        char? choice;

        try
        {
            choice = await ChooseAsync(numbers, () => Picking(request.Candidates.Count, Remaining(started)), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ItemPick(Withdrawn("Nothing was released."), null);
        }

        if (choice is not { } digit || numbers.IndexOf(digit, StringComparison.Ordinal) is var picked && picked < 0)
        {
            _console.WriteLine(string.Join(Environment.NewLine, _console.Paint(Tone.Danger, "keypaste: denied. Nothing was released."), RuleLine));
            return new ItemPick(ApprovalAnswer.Denied, null);
        }

        _console.WriteLine(string.Join(Environment.NewLine, string.Empty, $"  picked   [{picked + 1}] {Described(request.Candidates[picked])}"));

        var always = Always.Of(request, $"the {request.Field} of {Described(request.Candidates[picked])} when it asks for {request.Entry},", blocks: false);

        return new ItemPick(await AnswerAsync(started, always, cancellationToken).ConfigureAwait(false), picked);
    }

    /// <summary>
    /// Shows an agent's request to save a value, reads the value masked or generates one, then asks for <c>y</c> to save it.
    /// The value is never printed.
    /// </summary>
    public async ValueTask<SaveAnswer> AskToSaveAsync(SavePrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(RenderSave(request, RuleLine));

        try
        {
            var how = await ChooseAsync("tg", () => SaveChoice(Remaining(started)), cancellationToken).ConfigureAwait(false);
            string? value = null;

            if (how == 't')
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var typed = await Task.Run(() => _prompt.ReadSecret("  value: "), CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
                value = typed is { Length: > 0 } ? new string(typed.Value) : null;
            }
            else if (how == 'g')
            {
                using var generated = new SecretBuffer();
                PasswordGenerator.Append(SecretRecipe.Default, generated);
                value = new string(generated.Value);
                _console.WriteLine($"  generated a {SecretRecipe.Default.Describe("password")}; it is not shown");
            }

            if (value is null)
            {
                return new SaveAnswer(SaidSave(saved: false), null);
            }

            if (request.Replaces)
            {
                _console.WriteLine($"  This replaces {request.Field} at {request.Where}. The old value stays in the item's history.");
            }

            var confirmed = await ChooseAsync(_confirmChoices, () => Confirmation(Remaining(started), "save"), cancellationToken).ConfigureAwait(false) == 'y';

            return confirmed ? new SaveAnswer(SaidSave(saved: true), value) : new SaveAnswer(SaidSave(saved: false), null);
        }
        catch (OperationCanceledException)
        {
            return new SaveAnswer(Withdrawn("Nothing was saved."), null);
        }
    }

    private ApprovalAnswer SaidSave(bool saved)
    {
        _console.WriteLine(string.Join(
            Environment.NewLine,
            saved ? _console.Paint(Tone.Ok, "keypaste: saving it. The agent is not given it.") : _console.Paint(Tone.Danger, "keypaste: not saved."),
            RuleLine));

        return saved ? ApprovalAnswer.ApprovedOnce : ApprovalAnswer.Denied;
    }

    /// <summary>The save prompt's choice line: cancel first, then typing or generating the value.</summary>
    private string SaveChoice(int secondsLeft) =>
        _console.Paint(Tone.Accent, "[d] cancel  [t] type it  [g] generate  ")
        + _console.Paint(Tone.Accent, string.Create(CultureInfo.InvariantCulture, $"{secondsLeft,2}s {_console.Glyph(Mark.Prompt)}"))
        + " ";

    private static string RenderSave(SavePrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            "keypaste: an agent asks you to save a value. You enter it; the agent is not given it.",
            string.Empty,
            $"  agent    {Named(request.Client, request.Label)}",
            $"  {(request.ForProject ? "project" : "item   ")}  {request.Where}",
            $"  {(request.ForProject ? "key    " : "field  ")}  {request.Field}",
            $"  saving   {(request.Replaces ? "replaces the value it holds" : request.Creates ? "creates it" : "adds it")}",
            string.Empty,
            "  the agent says it needs this because:",
            $"    {request.Reason}",
        ];

        if (request.ReasonWasTruncated)
        {
            lines.Add("    (cut short — the full text is hashed in the audit log)");
        }

        if (request.WasAltered)
        {
            lines.Add(string.Empty);
            lines.Add("  The place or the reason above is not what the agent sent: it was scrubbed.");
        }

        lines.Add(string.Empty);
        lines.Add("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");
        lines.Add(string.Empty);

        return string.Join(Environment.NewLine, lines);
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

    public async ValueTask<PairingAnswer> AskAsync(PairingPrompt request, CancellationToken cancellationToken)
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
            _ = Withdrawn("Nothing was paired.");
            return new PairingAnswer(ApprovalAnswer.Denied, null);
        }

        var allowed = choice == 'y';

        _console.WriteLine(string.Join(
            Environment.NewLine,
            allowed
                ? _console.Paint(Tone.Ok, $"keypaste: allowed. Saving the pairing {request.SuggestedName} in the vault.")
                : _console.Paint(Tone.Danger, "keypaste: denied. Nothing was paired."),
            RuleLine));

        return allowed ? new PairingAnswer(ApprovalAnswer.ApprovedOnce, request.SuggestedName) : new PairingAnswer(ApprovalAnswer.Denied, null);
    }

    public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));
        return AnswerAsync(started, always: null, cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(RunPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = _clock.GetTimestamp();
        _console.WriteLine(Render(request, RuleLine));
        return AnswerAsync(started, always: null, cancellationToken);
    }

    public void Tell(PairingNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        _console.WriteLine(_console.Paint(
            Tone.Danger,
            $"keypaste: {notice.Identity.Described} asked to pair, and was refused: pairing is not on. "
            + "To pair it, start keypaste approve with --pair-browser <name>, then connect again."));
    }

    private async ValueTask<ApprovalAnswer> AnswerAsync(long started, Always? always, CancellationToken cancellationToken)
    {
        var choices = _choices + (always is null ? string.Empty : always.Blocks ? "ab" : "a");
        char? choice;

        try
        {
            choice = await ChooseAsync(choices, () => Choice(always, Remaining(started)), cancellationToken).ConfigureAwait(false);

            if (always is not null && choice is 'a' or 'b')
            {
                var allows = choice == 'a';
                _console.WriteLine(string.Join(
                    Environment.NewLine,
                    string.Empty,
                    $"  {(allows ? "Always give" : "Always refuse")} {always.Agent} {always.What} without asking you?",
                    "  Remove it on the agent's page in the keypaste app to be asked again."));
                var confirmed = await ChooseAsync(_confirmChoices, () => Confirmation(Remaining(started), allows ? "always allow" : "always block"), cancellationToken).ConfigureAwait(false) == 'y';

                return Said(
                    confirmed ? allows ? ApprovalAnswer.AlwaysAllowed : ApprovalAnswer.AlwaysBlocked : ApprovalAnswer.Denied);
            }
        }
        catch (OperationCanceledException)
        {
            return Withdrawn("Nothing was released.");
        }

        return Said(choice == 'o' ? ApprovalAnswer.ApprovedOnce : ApprovalAnswer.Denied);
    }

    private ApprovalAnswer Said(ApprovalAnswer answer)
    {
        _console.WriteLine(string.Join(
            Environment.NewLine,
            answer switch
            {
                ApprovalAnswer.ApprovedOnce => _console.Paint(Tone.Ok, "keypaste: allowed once."),
                ApprovalAnswer.AlwaysAllowed => _console.Paint(Tone.Ok, "keypaste: allowed, and always from now on."),
                ApprovalAnswer.AlwaysBlocked => _console.Paint(Tone.Danger, "keypaste: denied, and always from now on. Nothing was released."),
                _ => _console.Paint(Tone.Danger, "keypaste: denied. Nothing was released."),
            },
            RuleLine));

        return answer;
    }

    /// <summary>What a prompt's always answers keep, for the agent its bridge names, or null when it offers none.</summary>
    /// <param name="Agent">The agent's name.</param>
    /// <param name="What">What the agent would always get, as the confirmation names it.</param>
    /// <param name="Blocks">Whether Always block is offered too.</param>
    private sealed record Always(string Agent, string What, bool Blocks)
    {
        internal static Always? Of(ApprovalPrompt prompt, string what, bool blocks) =>
            prompt.OffersAlways && prompt.Label is { } agent ? new Always(agent, blocks ? $"this item's {what}" : what, blocks) : null;
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
    private string Choice(Always? always, int secondsLeft) =>
        _console.Paint(Tone.Accent, always switch
        {
            null => "[d] deny  [o] once  ",
            { Blocks: true } => "[d] deny  [o] once  [a] always  [b] block  ",
            _ => "[d] deny  [o] once  [a] always  ",
        })
        + _console.Paint(Tone.Accent, string.Create(CultureInfo.InvariantCulture, $"{secondsLeft,2}s {_console.Glyph(Mark.Prompt)}"))
        + " ";

    /// <summary>The picker's choice line: deny first, then the numbers that pick an item.</summary>
    private string Picking(int count, int secondsLeft) =>
        _console.Paint(Tone.Accent, "[d] deny  ")
        + _console.Paint(Tone.Accent, count == 1 ? "[1] pick  " : string.Create(CultureInfo.InvariantCulture, $"[1-{count}] pick  "))
        + _console.Paint(Tone.Accent, string.Create(CultureInfo.InvariantCulture, $"{secondsLeft,2}s {_console.Glyph(Mark.Prompt)}"))
        + " ";

    /// <summary>The confirmation's choice line: deny first, and the one key that confirms.</summary>
    private string Confirmation(int secondsLeft, string confirms = "allow") =>
        _console.Paint(Tone.Accent, "[d] deny  ")
        + _console.Paint(Tone.Ok, $"[y] {confirms}")
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

        if (request.Requester is null)
        {
            lines.Add("  a run names no agent to keep an always under, so it is asked about every time.");
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
            $"  agent    {Named(request.Client, request.Label)}",
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

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The picker: the agent, the name it asked for and the field, each item answering to it by number, and the agent's claim last.</summary>
    private static string RenderPicker(ApprovalPrompt request, string rule)
    {
        List<string> lines =
        [
            string.Empty,
            rule,
            string.Create(CultureInfo.InvariantCulture, $"keypaste: an agent is asking for a credential, and {request.Candidates.Count} items answer to it."),
            string.Empty,
            $"  agent    {Named(request.Client, request.Label)}",
            $"  asked    {request.Entry}",
            $"  field    {request.Field}, its value not shown here",
            string.Empty,
            "  Pick the one item to send, by its number:",
        ];

        lines.AddRange(request.Candidates.Select((candidate, index) => string.Create(CultureInfo.InvariantCulture, $"  [{index + 1}] {Described(candidate)}")));
        lines.Add(string.Empty);
        lines.Add("  the agent says it needs this because:");
        lines.Add($"    {request.Reason}");

        if (request.ReasonWasTruncated)
        {
            lines.Add("    (cut short — the full text is hashed in the audit log)");
        }

        if (request.EntryWasAltered || request.ReasonWasAltered)
        {
            lines.Add(string.Empty);
            lines.Add("  Characters that cannot be shown were scrubbed from the names or the reason above.");
        }

        lines.Add(string.Empty);
        lines.Add("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>One candidate on a line: its title, then its group, username and URL where it has them.</summary>
    private static string Described(ItemCandidate candidate) =>
        string.Join(
            "  ",
            new[] { candidate.Title, candidate.Group.Length > 0 ? candidate.Group : "top level", candidate.Username, candidate.Url }.Where(part => part.Length > 0));

    /// <summary>The agent as both prompts name it: the client it says it is, then the label its bridge was started with.</summary>
    private static string Named(string client, string? label) => label is { } shown ? $"{client} ({shown})" : client;

    /// <summary>A browser's request to pair: the build it named and the pairing's name, never a key.</summary>
    internal static string Render(PairingPrompt request, string rule) =>
        string.Join(
            Environment.NewLine,
            string.Empty,
            rule,
            "keypaste: a browser asks to pair with this vault.",
            string.Empty,
            $"  browser  {request.IdentityText}",
            $"  name     {request.SuggestedName}",
            string.Empty,
            "  The browser names this build when it starts keypaste, and any program you run can name it too,",
            "  so allow only a pairing you started in your browser just now.",
            string.Empty,
            "  [d] deny: nothing is paired.",
            "  [y] allow: keypaste saves the pairing in the vault, and that browser can ask you for logins.");

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
        var client = Named(request.Client, request.Label);
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

        return string.Join(Environment.NewLine, lines);
    }

    private static string Source(RunPromptVariable variable) =>
        string.Equals(variable.Field, "password", StringComparison.Ordinal) ? variable.Entry : $"{variable.Entry} · {variable.Field}";
}
