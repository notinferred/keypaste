using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Ipc;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste lock</c>: asks the process holding the vault unlocked to lock now (D-0351).</summary>
/// <remarks>
/// It confirms only what it sees: after the owner agrees, the verb attaches again until the owner
/// refuses or is gone, so "locked" is never printed about a vault that is still being served.
/// </remarks>
internal static class LockCommand
{
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _settle = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _step = TimeSpan.FromMilliseconds(100);

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new(SessionPipe.ApproverOption, TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste lock: {error}");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine($"keypaste lock: unexpected argument '{line.Operands[0]}'");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vault, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste lock: {locateError}");
            return CliApp.ExitUsageError;
        }

        if (!SessionPipe.TryResolve(line, vault, context, out var pipe, out var pipeError))
        {
            context.Stderr.WriteLine($"keypaste lock: {pipeError}");
            return CliApp.ExitUsageError;
        }

        return LockAsync(vault, pipe, context).GetAwaiter().GetResult();
    }

    private static async Task<int> LockAsync(string vault, string pipe, CliContext context)
    {
        using var bound = new CancellationTokenSource(_answerBound);
        await using var attachment = await SessionPipe.AttachAsync(pipe, vault, bound.Token);

        if (attachment is not { Client: { } client, Session: { } session })
        {
            if (attachment.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"keypaste lock: {Shown(refusal)}");
                return CliApp.ExitInternalError;
            }

            context.Stderr.WriteLine($"  nothing to lock: no keypaste process holds {vault} unlocked");
            return CliApp.ExitSuccess;
        }

        var reply = await client.LockAsync(new LockRequest { Vault = vault, Session = session }, bound.Token);

        if (reply is not { Locking: true })
        {
            context.Stderr.WriteLine(reply is null
                ? "keypaste lock: the keypaste process holding the vault did not answer; it may be older than this keypaste"
                : $"keypaste lock: {Shown(reply.Reason)}");
            return CliApp.ExitInternalError;
        }

        if (!await LockedAsync(client, vault))
        {
            context.Stderr.WriteLine($"keypaste lock: the keypaste process holding {vault} did not lock");
            return CliApp.ExitInternalError;
        }

        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine($"  {done} locked {vault} {style.Glyph(context.Stderr, Mark.Dot)} agents paused");
        return CliApp.ExitSuccess;
    }

    /// <summary>Attaches again until the owner refuses or has gone, or the time to settle runs out.</summary>
    private static async Task<bool> LockedAsync(ApproverClient client, string vault)
    {
        using var settle = new CancellationTokenSource(_settle);

        try
        {
            while (true)
            {
                var again = await client.AttachAsync(new AttachRequest(vault), settle.Token);

                if (again is not { Attached: true })
                {
                    return !settle.IsCancellationRequested;
                }

                await Task.Delay(_step, settle.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Another process's words, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste lock [--vault <path>] [--approver <name>]");
        writer.WriteLine();
        writer.WriteLine("asks the keypaste app or `keypaste agent` holding the vault unlocked to lock it now.");
        writer.WriteLine("every grant ends and every agent is refused until somebody unlocks it again.");
    }
}
