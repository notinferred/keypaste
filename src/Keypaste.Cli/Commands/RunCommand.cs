using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;

namespace Keypaste.Cli.Commands;

/// <summary>Where keypaste's own arguments end and the child's command begins.</summary>
/// <param name="Left">Everything up to, and excluding, the first bare <c>--</c>.</param>
/// <param name="Command">Everything after it, verbatim. Empty when there was no separator.</param>
/// <param name="HasSeparator">Whether a bare <c>--</c> was present at all.</param>
internal readonly record struct RunArguments(
    string[] Left,
    IReadOnlyList<string> Command,
    bool HasSeparator);

/// <summary>
/// Runs a command with a project's variables in its environment:
/// <c>keypaste run &lt;project&gt; -- &lt;command...&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two phases, in order. The first opens the vault and takes the project's variables out of it;
/// the second starts the child, with no vault alive anywhere in the process — see
/// <see cref="VaultSession.OpenThen{T}"/> for why that ordering is a function rather than a habit.
/// </para>
/// <para>
/// Nothing is written to disk at any point. The values exist in this process's memory and then in
/// the child's environment, which is where a program can read them from and is also the limit of
/// what keypaste can promise about them (SECURITY.md).
/// </para>
/// <para>
/// With <c>--session</c> the first phase is a question instead: the set comes from the process
/// holding the vault unlocked, over its endpoint, after the person there approves this command, and
/// nothing here opens the vault or reads a password, then or as a fallback (D-0341).
/// </para>
/// </remarks>
internal static class RunCommand
{
    internal const string SessionOption = "session";
    internal const string ApproverOption = "approver";

    /// <summary>How long to wait for the owner's answer: its longest window, and time for the reply to arrive.</summary>
    /// <remarks>The owner decides the timeout; this only bounds an owner that never answers.</remarks>
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(SessionOption, TakesValue: false),
        new(ApproverOption, TakesValue: true),
        new("token", TakesValue: true),
        new("bundle", TakesValue: true),
        new("profile", TakesValue: true, 'p'),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        var split = Split(args);

        if (!CommandLine.TryParse(split.Left, 1, _options, out var line, out var error))
        {
            return Fail(context, error);
        }

        // Checked before the separator, so `keypaste run --help` prints usage instead of
        // complaining that a command is missing.
        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (!split.HasSeparator)
        {
            return Fail(context, "expected -- followed by a command, as in: keypaste run dev -- npm start");
        }

        if (split.Command.Count == 0)
        {
            return Fail(context, "no command given after --");
        }

        if (line.Value("token") is not null || line.Value("bundle") is not null)
        {
            return RunWithToken.Execute(line, split.Command, context);
        }

        if (line.Operands.Count != 1)
        {
            return Fail(context, "expected exactly one project name");
        }

        var project = line.Operands[0];

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, locateError);
        }

        if (line.HasFlag(SessionOption))
        {
            if (line.Value("keyfile") is not null)
            {
                return Fail(context, "--session uses the vault another keypaste process has unlocked, so it takes no --keyfile");
            }

            return FromSession(path, line.Value(ApproverOption), project, split.Command, context);
        }

        if (line.Value(ApproverOption) is not null)
        {
            return Fail(context, "--approver names the process to ask, which only --session does");
        }

        return VaultSession.OpenThen(
            path,
            line,
            context,
            vault => Load(vault, project, context),
            resolved => Start(split.Command, resolved, context));
    }

    /// <summary>
    /// Splits at the first bare <c>--</c> after the verb, before any option parsing happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done here rather than in <see cref="CommandLine"/> because <c>run</c> needs something the
    /// parser cannot express at any severity: the right-hand side must be exempt from option
    /// parsing <em>entirely</em>, so that <c>keypaste run p -- mytool --vault x</c> gives
    /// <c>mytool</c> its own <c>--vault</c>. A parser that returns "the operands after <c>--</c>"
    /// has already decided that flag was keypaste's.
    /// </para>
    /// <para>
    /// Only the first separator is a boundary; every later one belongs to the child verbatim,
    /// which is what makes <c>keypaste run p -- git log -- path</c> mean what it looks like.
    /// </para>
    /// </remarks>
    internal static RunArguments Split(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--", StringComparison.Ordinal))
            {
                return new RunArguments(args[..i], args[(i + 1)..], HasSeparator: true);
            }
        }

        return new RunArguments(args, [], HasSeparator: false);
    }

    /// <summary>Resolves the project's variables.</summary>
    /// <remarks>A set with any entry that cannot be released starts nothing, and each such entry is named.</remarks>
    private static (int Exit, EnvResolved? Loaded) Load(
        Vault vault,
        string project,
        CliContext context) =>
        Admit(EnvResolution.Resolve(vault, project, TimeProvider.System), project, refusal: null, context);

    /// <summary>Asks the process holding the vault for the set, and starts the child only with what it released.</summary>
    /// <remarks>Every way of not getting the set ends here with a reason and no child; none of them opens the vault.</remarks>
    private static int FromSession(
        string vaultPath,
        string? approver,
        string project,
        IReadOnlyList<string> command,
        CliContext context)
    {
        string pipe;

        try
        {
            var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));
            pipe = ApproverEndpoint.Resolve(
                approver,
                context.Environment.Get(ApproverEndpoint.EnvironmentVariable),
                VaultIdentity.Of(home, vaultPath))!;
        }
        catch (ArgumentException ex)
        {
            return Fail(context, $"--{ApproverOption}: {ex.Message}");
        }

        var (reply, refusal) = AskAsync(pipe, vaultPath, project, command, context).GetAwaiter().GetResult();

        if (reply is null)
        {
            context.Stderr.WriteLine($"keypaste run: {Shown(refusal)}, so nothing was started");
            return CliApp.ExitInternalError;
        }

        var (exit, resolved) = Admit(reply.Set, project, reply.Reason, context);

        return resolved is null ? exit : Start(command, resolved, context);
    }

    private static async Task<(EnvReply? Reply, string Refusal)> AskAsync(
        string pipe,
        string vaultPath,
        string project,
        IReadOnlyList<string> command,
        CliContext context)
    {
        using var bound = new CancellationTokenSource(_answerBound);

        await using var client = await ApproverClient.TryConnectAsync(pipe, TimeSpan.FromMilliseconds(500), bound.Token);

        if (client is null)
        {
            return (null, $"nothing holds {vaultPath} unlocked; unlock it in the keypaste app or start `keypaste agent`");
        }

        var attached = await client.AttachAsync(new AttachRequest(vaultPath), bound.Token);

        if (attached is not { Attached: true, Session: { } session })
        {
            return (null, attached?.Reason ?? "the keypaste process holding the vault did not answer");
        }

        context.Stderr.WriteLine(
            $"keypaste run: asking the keypaste process holding {vaultPath} to release '{project}'; answer in its prompt");
        context.Stderr.Flush();

        var reply = await client.ReleaseEnvAsync(
            new EnvRequest(project, command, Environment.CurrentDirectory) { Vault = vaultPath, Session = session },
            bound.Token);

        return (reply, bound.IsCancellationRequested
            ? "no answer came in time"
            : "the keypaste process holding the vault did not answer");
    }

    /// <summary>What a resolved set comes to: the set to start with, or the exit code and a reason on stderr.</summary>
    /// <param name="resolved">The set, however it was resolved.</param>
    /// <param name="project">The project asked for.</param>
    /// <param name="refusal">The owner's words for a refusal, when an owner resolved it.</param>
    /// <param name="context">Where the reason goes.</param>
    private static (int Exit, EnvResolved? Loaded) Admit(
        EnvResolved resolved,
        string project,
        string? refusal,
        CliContext context)
    {
        switch (resolved.Outcome)
        {
            case EnvOutcome.Resolved:
                break;

            case EnvOutcome.NoProject:
                context.Stderr.WriteLine($"keypaste run: {(refusal is null ? resolved.Refusal : Shown(resolved.Refusal))}");
                return (CliApp.ExitNotFound, null);

            case EnvOutcome.Unusable:
                context.Stderr.WriteLine(
                    $"keypaste run: '{EnvConvention.GroupPath(project)}' cannot be used, so nothing was started:");

                foreach (var problem in resolved.Problems)
                {
                    var line = $"{EnvResolved.Display(problem.Key)} {problem.Reason}";
                    context.Stderr.WriteLine($"  {EntryNameSanitizer.Sanitize(line, 512).Text}");
                }

                context.Stderr.WriteLine("Fix or remove them in KeePassXC or the app, then run again.");
                return (CliApp.ExitInternalError, null);

            default:
                context.Stderr.WriteLine(refusal is { Length: > 0 }
                    ? $"keypaste run: {Shown(refusal)}, so nothing was started"
                    : $"keypaste run: {resolved.Refusal}");
                return (CliApp.ExitInternalError, null);
        }

        if (EnvironmentMerge.OverridesPath(resolved.Variables))
        {
            context.Stderr.WriteLine(
                $"warning: '{project}' defines PATH; the command itself is still resolved against yours");
        }

        return (CliApp.ExitSuccess, resolved);
    }

    /// <summary>Starts the child and reports its exit code as keypaste's own.</summary>
    private static int Start(
        IReadOnlyList<string> command,
        EnvResolved resolved,
        CliContext context)
    {
        // Everything keypaste has to say is said before the child owns the console, so a warning
        // does not land in the middle of the child's output.
        context.Stderr.Flush();

        var arguments = new string[command.Count - 1];
        for (var i = 1; i < command.Count; i++)
        {
            arguments[i - 1] = command[i];
        }

        var result = EnvLaunch.Start(
            resolved,
            new LaunchTarget(command[0], arguments),
            context.Environment.All(),
            context.ProcessLauncher);

        switch (result.Outcome)
        {
            case ChildOutcome.Exited:
                return result.ExitCode;

            case ChildOutcome.NotFound:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitCommandNotFound;

            case ChildOutcome.NotExecutable:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitCommandNotExecutable;

            default:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitInternalError;
        }
    }

    /// <summary>Another process's words, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste run: {message}");
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste run [--vault <path>] [--keyfile <path>] <project> -- <command> [args...]");
        writer.WriteLine("       keypaste run --session [--vault <path>] [--approver <name>] <project> -- <command> [args...]");
        writer.WriteLine();
        writer.WriteLine("runs a command with the project's variables in its environment. nothing is");
        writer.WriteLine("written to disk, and the vault is closed before the command starts.");
        writer.WriteLine();
        writer.WriteLine("with --session, no password is asked for here: the keypaste app or `keypaste agent`");
        writer.WriteLine("holding the vault unlocked shows you the project, its variable names, the command");
        writer.WriteLine("and this directory, and the command starts only if you approve it there.");
        writer.WriteLine();
        writer.WriteLine("the -- is required: without it, 'keypaste run dev npm start' cannot be told");
        writer.WriteLine("apart from a project called 'npm'. everything after it belongs to the command.");
        writer.WriteLine();
        writer.WriteLine("once the command starts, its exit code is keypaste's. 127 means there is no");
        writer.WriteLine("such command and 126 means it is not executable, as in a shell.");
    }
}
