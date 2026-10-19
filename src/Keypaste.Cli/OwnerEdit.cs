using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Cli;

/// <summary>What a verb that edits items gives the vault's holder: how it reads its values, and how it says what was done.</summary>
/// <param name="Verb">The verb, as a person types it after <c>keypaste</c>.</param>
/// <param name="Read">Reads the values the edit writes, as the verb asks for them, and returns the edit; null when it said why it could not.</param>
/// <param name="Done">Prints what the edit did, as the verb does when it saves the vault itself.</param>
internal sealed record OwnerRoute(string Verb, Func<CliContext, TerminalEdit?> Read, Func<EditDone, CliContext, int> Done);

/// <summary>
/// Sends a verb's edit to the app or <c>keypaste agent</c> holding the vault, which asks the person and
/// saves it under its own claim, instead of refusing it (D-0464).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here opens the vault or reads a master password: the edit carries only the values it writes,
/// and a recipe in place of a value it generates. The holder checks it with the same code the verb runs
/// on a vault it opened itself, so a refusal reads as the verb's own.
/// </para>
/// <para>
/// With nothing answering on the pipe, the verb is refused as a held vault always was (D-0382).
/// </para>
/// </remarks>
internal static class OwnerEdit
{
    /// <summary>How long a verb waits for the person: the longest window a prompt is up, and a little more.</summary>
    internal static readonly TimeSpan AnswerBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    /// <summary>How long reaching the holder may take before the verb says it did not answer.</summary>
    private static readonly TimeSpan _attachBound = TimeSpan.FromSeconds(10);

    /// <summary>How long piped stdin may stay open and silent before it counts as holding nothing more.</summary>
    private static readonly TimeSpan _moreInputWait = TimeSpan.FromSeconds(2);

    /// <summary>Sends the edit to <paramref name="holder"/> and waits for the person's answer.</summary>
    /// <param name="vaultPath">The vault.</param>
    /// <param name="line">The command line, for <c>--approver</c>.</param>
    /// <param name="context">Where prompts and output go.</param>
    /// <param name="holder">The app or <c>keypaste agent</c> holding the vault.</param>
    /// <param name="route">How the verb reads its values and says what was done.</param>
    /// <param name="refusal">What the verb says when nothing answers on the pipe.</param>
    /// <returns>The verb's exit code.</returns>
    internal static int Ask(string vaultPath, CommandLine line, CliContext context, VaultOwner holder, OwnerRoute route, string refusal)
    {
        if (!SessionPipe.TryResolve(line, vaultPath, context, out var pipe, out var pipeError))
        {
            context.Stderr.WriteLine($"keypaste {route.Verb}: {pipeError}");
            return CliApp.ExitUsageError;
        }

        return AskAsync(vaultPath, pipe, context, holder, route, refusal).GetAwaiter().GetResult();
    }

    private static async Task<int> AskAsync(string vaultPath, string pipe, CliContext context, VaultOwner holder, OwnerRoute route, string refusal)
    {
        var verb = route.Verb;
        var who = holder.Describe();

        using var attaching = new CancellationTokenSource(_attachBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vaultPath, attaching.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            context.Stderr.WriteLine($"keypaste: {attachment.Refusal ?? refusal}");
            return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"keypaste {verb}: {who} holds this vault; it will ask you to allow this change, and no master password is needed.");

        if (route.Read(context) is not { } edit)
        {
            return CliApp.ExitUsageError;
        }

        if (await CarriesMoreInputAsync(context))
        {
            context.Stderr.WriteLine(
                $"keypaste {verb}: stdin held more lines than this command reads, such as a master password first; nothing was sent. Pipe only the value.");
            return CliApp.ExitUsageError;
        }

        var request = new EditRequest(edit) { Vault = vaultPath, Session = session };

        if (!ApproverProtocol.Fits(request))
        {
            context.Stderr.WriteLine(
                $"keypaste {verb}: the change is too large to hand to {who} in one request (64 KiB); nothing was sent. Make it there, or run `keypaste lock` and try again.");
            return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"asking {who} now; answer in its {(holder.Kind == OwnerKind.DesktopApp ? "prompt window" : "terminal")}");

        using var answering = new CancellationTokenSource(AnswerBound);
        var reply = await client.EditAsync(request, answering.Token);

        if (reply is null)
        {
            context.Stderr.WriteLine($"keypaste {verb}: {who} did not answer, or no answer came in time; it may be older than this keypaste");
            return CliApp.ExitInternalError;
        }

        if (reply.Done is { } done && (reply.Decision == AuditDecision.Granted || reply.Method == AuditMethod.Unchanged))
        {
            var exit = route.Done(done, context);

            if (!reply.Recorded)
            {
                context.Stderr.WriteLine($"note: {who} could not record this in its audit log.");
            }

            return exit;
        }

        var said = EntryNameSanitizer.SanitizeProse(reply.Reason, 1024).Text;

        if (reply.Refusal is { } kind)
        {
            context.Stderr.WriteLine($"keypaste {verb}: {said}");
            return EditLines.ExitOf(kind);
        }

        context.Stderr.WriteLine($"keypaste {verb}: not saved: {said}");
        return reply.Method == AuditMethod.Prompt ? CliApp.ExitUsageError : CliApp.ExitInternalError;
    }

    /// <summary>
    /// Whether piped stdin holds a line beyond the values the verb read: a script written for a verb that
    /// opened the vault itself pipes the master password first, which is then never sent.
    /// </summary>
    /// <remarks>A pipe left open and silent counts as holding nothing more once a short wait passes, as it would have held no master password.</remarks>
    private static async Task<bool> CarriesMoreInputAsync(CliContext context)
    {
        if (context.Prompt.IsInteractive)
        {
            return false;
        }

        var reading = Task.Run(() =>
        {
            using var more = context.Prompt.ReadSecret(string.Empty);
            return more is not null;
        });

        return await Task.WhenAny(reading, Task.Delay(_moreInputWait)) == reading && await reading;
    }
}
