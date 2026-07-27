using Keypaste.Cli.Prompting;
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
/// data-only.
/// </para>
/// <para>
/// <b>Rendering rules, which are THREATS.md T-2's mitigation and not decoration.</b> The reason is
/// already sanitized and capped by <see cref="ApprovalPrompt"/> — no control characters, no
/// newlines, no bidirectional overrides — so it cannot draw a second dialog inside this one or move
/// the question off the screen. It is printed last, under a line saying who wrote it, and it is
/// never interpolated into anything that gets parsed. The default is no, and the only thing that is
/// not a no is an explicit yes.
/// </para>
/// </remarks>
internal sealed class TerminalApprovalChannel(ISecretPrompt prompt, TextWriter stderr) : IApprovalChannel
{
    internal const string Rule = "────────────────────────────────────────────────────────────";

    private readonly ISecretPrompt _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    private readonly TextWriter _stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));

    public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Render(request);

        string? answer;

        try
        {
            // Checked here rather than left to WaitAsync, which short-circuits on an already
            // completed task and never looks at the token. A read that finished before the await
            // got here would therefore be answered as though nobody had withdrawn the request -
            // an outcome that depended on how the thread pool happened to schedule the read rather
            // than on anything the caller did.
            cancellationToken.ThrowIfCancellationRequested();

            // The read blocks and cannot be interrupted, so it runs somewhere else and the wait is
            // what gets cancelled. The abandoned reader is the known cost: it stays parked on the
            // terminal until the next keystroke, which is why the withdrawal notice below tells the
            // human the question is gone.
            answer = await Task
                .Run(() => _prompt.ReadLine("Approve? [y/N] "), CancellationToken.None)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _stderr.WriteLine();
            _stderr.WriteLine("keypaste: the request was withdrawn before you answered. Nothing was released.");
            _stderr.WriteLine(Rule);
            return ApprovalAnswer.Denied;
        }

        var approved = answer is not null && Yes(answer);

        _stderr.WriteLine(approved
            ? "keypaste: approved."
            : "keypaste: denied. Nothing was released.");
        _stderr.WriteLine(Rule);

        return approved ? ApprovalAnswer.Approved : ApprovalAnswer.Denied;
    }

    /// <summary>Whether an answer is an explicit yes. Everything else, including nothing, is no.</summary>
    private static bool Yes(string answer)
    {
        var trimmed = answer.Trim();

        return string.Equals(trimmed, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private void Render(ApprovalPrompt request)
    {
        _stderr.WriteLine();
        _stderr.WriteLine(Rule);
        _stderr.WriteLine("keypaste: an agent is asking for a credential.");
        _stderr.WriteLine();
        _stderr.WriteLine($"  client   {request.Client}");
        _stderr.WriteLine($"  entry    {request.Entry}");
        _stderr.WriteLine($"  field    {request.Field}");
        _stderr.WriteLine($"  for      {request.TtlSeconds} seconds");
        _stderr.WriteLine();
        _stderr.WriteLine("  the agent says it needs this because:");
        _stderr.WriteLine($"    {request.Reason}");

        if (request.ReasonWasTruncated)
        {
            _stderr.WriteLine("    (cut short — the full text is hashed in the audit log)");
        }

        _stderr.WriteLine();
        _stderr.WriteLine("  That sentence was written by the agent, not by keypaste. Treat it as a claim.");
        _stderr.WriteLine();
    }
}
