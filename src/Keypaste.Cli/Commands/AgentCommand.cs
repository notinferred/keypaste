using System.Globalization;
using Keypaste.Cli.Approval;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste agent</c> — holds the unlocked vault and asks you about every credential request.
/// </summary>
/// <remarks>
/// <para>
/// The process that makes CORE.md law 3.2 real. It is started by a human, in a terminal a human
/// opened, and the master password is typed there in response to a command they typed — so nothing
/// an agent does can raise a password prompt. That property is the whole reason the approval flow
/// is not built into <c>keypaste-mcp</c> (DECISIONS.md D-0023).
/// </para>
/// <para>
/// <b>Deliberately not a daemon.</b> No service, no launch agent, no PID file, no starting itself
/// on demand. It runs in the foreground, says what it is doing, and stops when you stop it — which
/// is also the honest answer to "is anything able to act as me right now?".
/// </para>
/// <para>
/// <b>It writes no audit lines.</b> <c>keypaste-mcp</c> is the only process that appends to the
/// log, so there is one writer, one key order and one schema (DECISIONS.md D-0020). What this
/// process prints is for the person watching it, not the record.
/// </para>
/// <para>
/// <b>The vault stays unlocked for as long as this runs.</b> There is no idle auto-lock in this
/// version — closing the terminal is the lock — and that is stated in docs/approvals.md rather than
/// left for somebody to discover. Stage 4.1 owns idle locking, and
/// <see cref="VaultCredentialSource"/> already takes the vault through a delegate so adding it
/// later changes nothing here.
/// </para>
/// </remarks>
internal static class AgentCommand
{
    internal const string ApproverOption = "approver";
    internal const string TimeoutOption = "approval-timeout";
    internal const string MaxTtlOption = "max-ttl";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new(ApproverOption, TakesValue: true),
        new(TimeoutOption, TakesValue: true),
        new(MaxTtlOption, TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var parseError))
        {
            context.Stderr.WriteLine($"keypaste: {parseError}");
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
            context.Stderr.WriteLine($"keypaste: unexpected argument '{line.Operands[0]}'");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (!TryLimits(line, context, out var limits))
        {
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste: {locateError}");
            return CliApp.ExitUsageError;
        }

        string pipeName;

        try
        {
            pipeName = ApproverEndpoint.Resolve(line.Value(ApproverOption), context.Environment.Get(ApproverEndpoint.EnvironmentVariable));
        }
        catch (ArgumentException ex)
        {
            context.Stderr.WriteLine($"keypaste: {ex.Message}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(vaultPath, context, vault => Serve(vault, vaultPath, pipeName, limits, context));
    }

    private static int Serve(Vault vault, string vaultPath, string pipeName, ApprovalLimits limits, CliContext context)
    {
        using var grants = new GrantCache(TimeProvider.System);
        using var gate = new ApprovalGate(
            new TerminalApprovalChannel(context.Prompt, context.Stderr),
            TimeProvider.System,
            limits);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => vault),
            new VaultEntryNameLister(() => vault),
            gate,
            grants,
            line => context.Stderr.WriteLine($"keypaste: {line}"));

        ApproverListener? listener = null;

        try
        {
            try
            {
                // Binding happens here, so a name somebody else already holds is a startup failure
                // rather than a server that looks up and never accepts anything.
                listener = new ApproverListener(pipeName, handler);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Stderr.WriteLine($"keypaste: could not listen on '{pipeName}': {ex.Message}");
                context.Stderr.WriteLine("keypaste: another keypaste agent may already be running.");
                return CliApp.ExitInternalError;
            }

            using var stop = new CancellationTokenSource();

            void Interrupt(object? sender, ConsoleCancelEventArgs e)
            {
                // Ctrl+C stops the listener rather than the process, so the vault is disposed and
                // the grants are zeroed on the way out instead of being abandoned mid-flight.
                e.Cancel = true;
                stop.Cancel();
            }

            Console.CancelKeyPress += Interrupt;

            try
            {
                Announce(vaultPath, pipeName, limits, context);

                // Blocking on the listener is the command. There is no synchronization context in
                // a console app, so this is a wait rather than a deadlock waiting to happen.
                listener.RunAsync(stop.Token).GetAwaiter().GetResult();
            }
            finally
            {
                Console.CancelKeyPress -= Interrupt;
            }
        }
        finally
        {
            listener?.Dispose();
        }

        context.Stderr.WriteLine("keypaste: the agent has stopped. The vault is locked and every grant is gone.");
        return CliApp.ExitSuccess;
    }

    private static void Announce(string vaultPath, string pipeName, ApprovalLimits limits, CliContext context)
    {
        context.Stderr.WriteLine($"keypaste: watching {vaultPath}");
        context.Stderr.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"keypaste: listening on {pipeName}, {limits.Window.TotalSeconds:0} seconds to answer, grants last at most {limits.MaximumTtlSeconds} seconds"));
        context.Stderr.WriteLine("keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.");
    }

    private static bool TryLimits(CommandLine line, CliContext context, out ApprovalLimits limits)
    {
        limits = ApprovalLimits.Default;

        if (!TrySeconds(
                line.Value(TimeoutOption),
                TimeoutOption,
                ApprovalLimits.MinimumWindowSeconds,
                ApprovalLimits.MaximumWindowSeconds,
                context,
                out var window))
        {
            return false;
        }

        if (!TrySeconds(line.Value(MaxTtlOption), MaxTtlOption, 1, ToolTtlCeiling, context, out var maxTtl))
        {
            return false;
        }

        limits = ApprovalLimits.Default with
        {
            Window = window is null ? ApprovalLimits.Default.Window : TimeSpan.FromSeconds(window.Value),
            MaximumTtlSeconds = maxTtl ?? ApprovalLimits.Default.MaximumTtlSeconds,
        };

        return true;
    }

    /// <summary>
    /// The ceiling the tool schema advertises. Kept in step by
    /// <c>ToolSchemasMatchTheCoreTests</c> rather than by hoping.
    /// </summary>
    internal const int ToolTtlCeiling = 3600;

    private static bool TrySeconds(
        string? raw,
        string option,
        int minimum,
        int maximum,
        CliContext context,
        out int? value)
    {
        value = null;

        if (raw is null)
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            context.Stderr.WriteLine($"keypaste: --{option} must be a whole number of seconds between {minimum} and {maximum}");
            return false;
        }

        value = parsed;
        return true;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste agent [--vault <path>] [--approver <name>]");
        writer.WriteLine("                      [--approval-timeout <seconds>] [--max-ttl <seconds>]");
        writer.WriteLine();
        writer.WriteLine("Unlocks your vault and waits. When an AI agent asks keypaste-mcp for a");
        writer.WriteLine("credential, the request appears here and nothing is released until you say yes.");
        writer.WriteLine("Leave this running in its own terminal; Ctrl+C locks the vault again.");
        writer.WriteLine();
        writer.WriteLine($"  --vault <path>             which vault to unlock, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine($"  --approver <name>          which pipe to listen on, or set {ApproverEndpoint.EnvironmentVariable}");
        writer.WriteLine($"  --approval-timeout <secs>  how long you have to answer, {ApprovalLimits.MinimumWindowSeconds}-{ApprovalLimits.MaximumWindowSeconds}, default {ApprovalLimits.DefaultWindowSeconds}");
        writer.WriteLine($"  --max-ttl <secs>           the longest grant to issue, default {ApprovalLimits.DefaultMaximumTtlSeconds}");
        writer.WriteLine();
        writer.WriteLine("Your master password is typed here, never in a window an agent caused to appear.");
    }
}
