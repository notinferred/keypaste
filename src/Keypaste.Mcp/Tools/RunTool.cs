using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keypaste.Mcp.Tools;

/// <summary>
/// The tool an agent uses to run one command with approved secrets in its environment. A person
/// approves the exact program, command line, directory and variable names (D-0358).
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the security property, and every call is recorded once.</b> The request is checked,
/// the directory and the program are resolved to what will really start, references outside the
/// exposure are refused, and only then is the vault's owner asked, which asks the person. The owner
/// records its answer before it replies, so the approved line is written before the child starts; a
/// reply it did not record starts nothing and drops the values. This file records only a call the
/// owner did not answer.
/// </para>
/// <para>
/// <b>The child runs here, not in the owner</b>: this is already the agent's process, with the
/// client's <c>PATH</c> and working environment, and the owner holding the unlocked vault must not
/// become a launcher of agent-chosen programs. There is still no vault code path in this file
/// (THREATS.md T-8).
/// </para>
/// <para>
/// <b>What comes back holds no value this tool knows how to find</b>: each literal and escaped form of
/// every value, and of every text a placeholder filled into one (D-0465), is scrubbed from the output.
/// The command itself can still reveal one in another form, to a file or over the network, which the
/// result and T-35 say.
/// </para>
/// </remarks>
internal sealed class RunTool(ServerOptions options, ApproverConnection approver, AuditLog audit) : McpServerTool
{
    private static readonly TimeSpan _heartbeat = TimeSpan.FromSeconds(10);

    private int _running;

    /// <inheritdoc/>
    public override IReadOnlyList<object> Metadata => [];

    /// <inheritdoc/>
    public override Tool ProtocolTool { get; } = new()
    {
        Name = ToolText.RunToolName,
        Title = "Run a command with secrets injected",
        Description = ToolText.RunDescription,
        InputSchema = ToolSchemas.RunInput,
        Annotations = new ToolAnnotations
        {
            // Honest: it runs a program the agent names, which can change anything and reach anything.
            ReadOnlyHint = false,
            DestructiveHint = true,
            IdempotentHint = false,
            OpenWorldHint = true,
        },
    };

    /// <inheritdoc/>
    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var call = Read(request.Params?.Arguments);
        var line = new Line(call);
        var (initialized, client) = await McpAudit.CallerAsync(request, options, cancellationToken).ConfigureAwait(false);

        if (!initialized)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.NotInitialized, "the agent called a tool before the initialize handshake completed", ToolText.NotInitialized), null);
        }

        // Taken now or refused now: a second run is never queued behind one that may be waiting on a person.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Busy, "another run is still going on this connection", ToolText.RunBusy), null);
        }

        try
        {
            return await RunAsync(request, client, call, line, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Cancelled, "the agent withdrew the request"), null);
        }
        catch (Exception)
        {
            // An exception is the shape most likely to skip the append (law 3.3); denying is the answer (law 3.7).
            return Finish(client, line, Verdict.Denied(AuditMethod.Failed, "the request could not be completed"), null);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async ValueTask<CallToolResult> RunAsync(
        RequestContext<CallToolRequestParams> request,
        AuditClient client,
        Call call,
        Line line,
        CancellationToken cancellationToken)
    {
        if (call.Malformed is { } malformed)
        {
            return Finish(client, line, Invalid(malformed, "is not the type the tool's schema gives"), null);
        }

        if (RunRequestRules.Check(call.Arguments) is { } problem)
        {
            return Finish(client, line, Invalid(problem.Argument, problem.Rule), null);
        }

        if (!RunProgram.TryResolveDirectory(call.Directory, out var directory, out _))
        {
            return Finish(client, line, Invalid("directory", "must be an existing directory"), null);
        }

        var inherited = EnvironmentMerge.Inherited();

        if (!RunProgram.TryResolve(call.Command[0], directory, inherited.GetValueOrDefault("PATH"), inherited.GetValueOrDefault("PATHEXT"), out var program, out _))
        {
            return Finish(client, line, Invalid("command", "must name a program on PATH, or a path to one relative to directory"), null);
        }

        line.Program = program;

        if (RunRequestRules.CheckProgram(program, call.Command) is { } programProblem)
        {
            return Finish(client, line, Invalid(programProblem.Argument, programProblem.Rule), null);
        }

        // An entry reference is checked by name before anybody is asked. An env reference names a
        // project's set, whose entries only the owner can find, so the owner applies the exposure
        // to each entry and field it resolves, as it does for every reference.
        foreach (var reference in call.References ?? [])
        {
            var forwarded = reference.Parsed switch
            {
                EnvReference => true,
                EntryReference named => options.Exposure.MayPermit(named.Entry, named.Field),
                _ => false,
            };

            if (!forwarded)
            {
                return Finish(client, line, Verdict.Denied(AuditMethod.OutOfScope, "a reference is outside what this agent can see through this bridge"), null);
            }
        }

        using var heartbeat = Heartbeat.Start(request, cancellationToken);

        var (reply, outcome, refusal) = await approver.RunAsync(
            new RunRequest
            {
                Program = program,
                Command = call.Command,
                Directory = directory,
                Project = call.Project,
                Profile = call.Profile ?? EnvProfileNames.Default,
                Keys = call.Keys,
                References = call.References,
                Reason = call.Reason,
                Exposure = options.Exposure.Globs,
                ClientName = client.Name,
                ClientVersion = client.Version,
                ClientLabel = options.ClientLabel,
            },
            cancellationToken).ConfigureAwait(false);

        line.Entries = reply?.Entries;
        line.Session = reply?.Session;

        // A value keypaste cannot record is one it does not hand over (T-6).
        if (reply is { Recorded: false })
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Failed, McpAudit.Unrecorded, ToolText.AuditUnavailable), null);
        }

        // A reply from here is one the owner recorded, a withdrawn request's included, so nothing that
        // follows adds a line: a release dropped after it stays recorded.
        line.Recorded = reply is not null;

        if (cancellationToken.IsCancellationRequested)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Cancelled, "the agent withdrew the request"), null);
        }

        if (outcome == ApproverOutcome.Busy)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Busy, "another exchange was already in flight on this connection"), null);
        }

        if (reply is null)
        {
            return Finish(client, line, outcome switch
            {
                ApproverOutcome.Refused when refusal?.Refusal is { } method => Verdict.Denied(method, refusal.Reason),
                ApproverOutcome.NoVault => Verdict.Denied(AuditMethod.NoSession, "this server was started without a vault to ask about", ToolText.NoVault),
                ApproverOutcome.Failed => Verdict.Denied(AuditMethod.Failed, "the owner did not answer a run request", ToolText.RunUnanswered),
                _ => Verdict.Denied(
                    AuditMethod.NoApprover,
                    "no keypaste process holds the vault unlocked",
                    outcome == ApproverOutcome.Unreachable && await LockedApp.TellAsync(options, cancellationToken).ConfigureAwait(false)
                        ? ToolText.AppLocked
                        : null),
            }, null);
        }

        if (reply.Set.Outcome != EnvOutcome.Resolved)
        {
            return Finish(client, line, Verdict.Denied(reply.Method, reply.Reason), null);
        }

        var names = reply.Set.Variables.Select(variable => variable.Key).ToList();

        // The owner checked this before it recorded the release; checked again here, before anything
        // starts, so a reply naming other variables never runs.
        if (!RunRequestRules.Answers(call.Arguments, names))
        {
            return ToolResults.Refuse(ToolText.RunMismatched);
        }

        var environment = EnvironmentMerge.Build(
            inherited
                .Where(pair => !pair.Key.StartsWith("KEYPASTE_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, EnvironmentMerge.Comparer),
            reply.Set.Variables);

        var result = await CapturedLaunch.RunAsync(
            new ChildStart(program, [.. call.Command.Skip(1)], environment, directory),
            CaptureLimits.Default,
            TimeSpan.FromSeconds(call.TimeoutSeconds),
            cancellationToken).ConfigureAwait(false);

        if (result.Outcome != ChildOutcome.Exited)
        {
            // The owner's approved line stands: the values reached this process, and nothing ran with them.
            return ToolResults.Refuse(ToolText.RunNotStarted(program, result.Error));
        }

        var scrubber = OutputScrubber.For([.. reply.Set.Variables, .. reply.Set.Fills]);

        return ToolResults.Ran(
            names,
            reply.Method,
            reply.GrantedSeconds,
            result.ExitCode,
            call.TimeoutSeconds,
            scrubber.Scrub(result.Stdout),
            scrubber.Scrub(result.Stderr));
    }

    /// <summary>Appends the line for an answer that starts nothing, unless the owner recorded the call, then answers.</summary>
    private CallToolResult Finish(AuditClient client, Line line, Verdict verdict, CallToolResult? answer)
    {
        if (!line.Recorded && !audit.TryAppend(line.Record(client, verdict, options), out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
        }

        return answer ?? ToolResults.Refuse(verdict.Refusal ?? ToolText.Refusal(verdict.Method));
    }

    private static Verdict Invalid(string argument, string rule) =>
        Verdict.Denied(AuditMethod.InvalidRequest, "the request did not satisfy the tool's schema", ToolText.Invalid(argument, rule));

    private static Call Read(IDictionary<string, JsonElement>? arguments)
    {
        string? malformed = null;

        string? Text(string name)
        {
            if (arguments is null || !arguments.TryGetValue(name, out var element))
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                malformed ??= name;
                return null;
            }

            return element.GetString();
        }

        IReadOnlyList<string>? Strings(string name)
        {
            if (arguments is null || !arguments.TryGetValue(name, out var element))
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.Array || element.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            {
                malformed ??= name;
                return null;
            }

            return [.. element.EnumerateArray().Select(item => item.GetString()!)];
        }

        IReadOnlyList<RunReference>? References()
        {
            if (arguments is null || !arguments.TryGetValue("env", out var element))
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.String))
            {
                malformed ??= "env";
                return null;
            }

            return [.. element.EnumerateObject().Select(property => new RunReference(property.Name, property.Value.GetString()!))];
        }

        var timeout = RunRequestRules.DefaultTimeoutSeconds;

        if (arguments is not null && arguments.TryGetValue("timeout_seconds", out var seconds))
        {
            timeout = seconds.ValueKind == JsonValueKind.Number && seconds.TryGetInt32(out var parsed) ? parsed : -1;
        }

        var call = new Call(
            Strings("command") ?? [],
            Text("directory") ?? string.Empty,
            Text("project"),
            Text("profile"),
            Strings("keys"),
            References(),
            Text("reason") ?? string.Empty,
            timeout);

        return call with { Malformed = malformed };
    }

    /// <summary>The arguments as the agent sent them.</summary>
    private sealed record Call(
        IReadOnlyList<string> Command,
        string Directory,
        string? Project,
        string? Profile,
        IReadOnlyList<string>? Keys,
        IReadOnlyList<RunReference>? References,
        string Reason,
        int TimeoutSeconds)
    {
        public string? Malformed { get; init; }

        public RunArguments Arguments => new(Command, Directory, Project, Profile, Keys, References, Reason, TimeoutSeconds);
    }

    /// <summary>Why a run was refused, for the log and for the agent.</summary>
    private sealed record Verdict(AuditMethod Method, string Reason, string? Refusal)
    {
        internal static Verdict Denied(AuditMethod method, string reason, string? refusal = null) => new(method, reason, refusal);
    }

    /// <summary>What the audit line says about the run, filled in as it is learned.</summary>
    private sealed class Line(Call call)
    {
        internal string? Program { get; set; }

        internal IReadOnlyList<string>? Entries { get; set; }

        internal string? Session { get; set; }

        /// <summary>Whether the owner recorded the call, so this bridge writes no line for it.</summary>
        internal bool Recorded { get; set; }

        internal AuditRecord Record(AuditClient client, Verdict verdict, ServerOptions options) =>
            RequestRecords.Run(
                client,
                call.Arguments,
                Program,
                AuditDecision.Denied,
                verdict.Method,
                verdict.Reason,
                grantedSeconds: null,
                Entries,
                options.Exposure.Globs,
                Session,
                options.VaultKey);
    }

    /// <summary>Tells a client that asked for progress that the run is alive, so its request timeout resets.</summary>
    private sealed class Heartbeat : IDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _beating;

        private Heartbeat(CancellationTokenSource stop, Task beating)
        {
            _stop = stop;
            _beating = beating;
        }

        internal static Heartbeat? Start(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            if (request.Params?.ProgressToken is not { } token || request.Server is not { } server)
            {
                return null;
            }

            var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new Heartbeat(stop, BeatAsync(server, token, stop.Token));
        }

        private static async Task BeatAsync(McpServer server, ProgressToken token, CancellationToken stop)
        {
            var beats = 0;

            try
            {
                while (true)
                {
                    await Task.Delay(_heartbeat, stop).ConfigureAwait(false);
                    beats++;
                    await server.NotifyProgressAsync(
                        token,
                        new ProgressNotificationValue { Progress = beats, Message = "keypaste: still waiting for the person or the command" },
                        cancellationToken: stop).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Stopped, or the client went away: progress is a courtesy, never a reason to fail the run.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _ = _beating.ContinueWith(_ => _stop.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
