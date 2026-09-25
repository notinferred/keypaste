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
/// <b>The order is the security property, and every exit goes through the one audit append.</b>
/// The request is checked, the directory and the program are resolved to what will really start,
/// references outside the exposure are refused, and only then is the vault's owner asked, which asks
/// the person. The approved line is appended before the child starts; a failed append starts nothing
/// and drops the values.
/// </para>
/// <para>
/// <b>The child runs here, not in the owner</b>: this is already the agent's process, with the
/// client's <c>PATH</c> and working environment, and the owner holding the unlocked vault must not
/// become a launcher of agent-chosen programs. There is still no vault code path in this file
/// (THREATS.md T-8).
/// </para>
/// <para>
/// <b>What comes back holds no value this tool knows how to find</b>: each literal and escaped form is
/// scrubbed from the output. The command itself can still reveal one in another form, to a file or
/// over the network, which the result and T-35 say.
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

        var client = McpAudit.ClientOf(request, options);
        var call = Read(request.Params?.Arguments);
        var line = new Line(call);

        if (!await McpAudit.HandshakeCompleteAsync(request, cancellationToken).ConfigureAwait(false))
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.NotInitialized, "the client called a tool before the initialize handshake completed", ToolText.NotInitialized), null);
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
            return Finish(client, line, Verdict.Denied(AuditMethod.Cancelled, "the client withdrew the request"), null);
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

        var arguments = new RunArguments(call.Command, call.Directory, call.Project, call.Profile, call.Keys, call.References, call.Reason, call.TimeoutSeconds);

        if (RunRequestRules.Check(arguments) is { } problem)
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

        // Every reference names its entry without the vault, so the exposure is applied here before
        // anybody is asked; the owner applies it again after resolving.
        foreach (var reference in call.References ?? [])
        {
            _ = KpReferences.TryParse(reference.Reference, out var parsed, out _);

            var entry = parsed switch
            {
                EnvReference env => new EntryName(EnvProfileNames.GroupPath(env.Project, env.Profile), env.Key),
                EntryReference named => named.Entry,
                _ => null,
            };

            if (entry is null || !options.Exposure.Allows(entry))
            {
                return Finish(client, line, Verdict.Denied(AuditMethod.OutOfScope, "a reference is outside this server's configured exposure"), null);
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

        if (cancellationToken.IsCancellationRequested)
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Cancelled, "the client withdrew the request"), null);
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
                _ => Verdict.Denied(AuditMethod.NoApprover, "no keypaste process holds the vault unlocked"),
            }, null);
        }

        line.Entries = reply.Entries;
        line.Session = reply.Session;

        if (reply.Set.Outcome != EnvOutcome.Resolved)
        {
            return Finish(client, line, Verdict.Denied(reply.Method, reply.Reason), null);
        }

        var names = reply.Set.Variables.Select(variable => variable.Key).ToList();

        if (!Expected(call, names))
        {
            return Finish(client, line, Verdict.Denied(AuditMethod.Failed, "the owner's reply named variables the request did not", ToolText.RunMismatched), null);
        }

        var granted = Verdict.Granted(reply.Method, reply.Reason, reply.GrantedSeconds);

        // Before the child starts, and the only way it starts: a release keypaste cannot record is
        // one it does not make (T-6).
        if (!audit.TryAppend(line.Record(client, granted, options), out _))
        {
            return ToolResults.Refuse(ToolText.AuditUnavailable);
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
            // The approved line stands: the values reached this process, and nothing ran with them.
            return ToolResults.Refuse(ToolText.RunNotStarted(program, result.Error));
        }

        var scrubber = OutputScrubber.For(reply.Set.Variables);

        return ToolResults.Ran(
            names,
            reply.Method,
            reply.GrantedSeconds,
            result.ExitCode,
            call.TimeoutSeconds,
            scrubber.Scrub(result.Stdout),
            scrubber.Scrub(result.Stderr));
    }

    /// <summary>Whether the owner's names are the ones asked for: exactly, or for a whole set, ones the name rule allows.</summary>
    private static bool Expected(Call call, List<string> names) =>
        call.References is { } references
            ? names.SequenceEqual(references.Select(reference => reference.Name), StringComparer.Ordinal)
            : call.Keys is { } keys
                ? names.SequenceEqual(keys, StringComparer.Ordinal)
                : RunRequestRules.NameProblem(names) is null;

    /// <summary>Appends the line for an answer that starts nothing, then answers.</summary>
    private CallToolResult Finish(AuditClient client, Line line, Verdict verdict, CallToolResult? answer)
    {
        if (!audit.TryAppend(line.Record(client, verdict, options), out _))
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
    }

    /// <summary>What was decided, for the log and for the agent.</summary>
    private sealed record Verdict(AuditDecision Decision, AuditMethod Method, string Reason, string? Refusal, int GrantedSeconds)
    {
        internal static Verdict Denied(AuditMethod method, string reason, string? refusal = null) =>
            new(AuditDecision.Denied, method, reason, refusal, 0);

        internal static Verdict Granted(AuditMethod method, string reason, int grantedSeconds) =>
            new(AuditDecision.Granted, method, reason, null, grantedSeconds);
    }

    /// <summary>What the audit line says about the run, filled in as it is learned.</summary>
    private sealed class Line(Call call)
    {
        internal string? Program { get; set; }

        internal IReadOnlyList<string>? Entries { get; set; }

        internal string? Session { get; set; }

        internal AuditRecord Record(AuditClient client, Verdict verdict, ServerOptions options)
        {
            var group = call.Project is { } project && call.References is null
                ? $"{EnvConvention.RootGroup}/{project}{(call.Profile is { } profile && profile != EnvProfileNames.Default ? "/" + profile : string.Empty)}"
                : null;
            var shown = DisplayTextSanitizer.Sanitize(EnvReleasePrompt.CommandLine(call.Command), EnvReleasePrompt.MaximumCommandLength).Text;

            return McpAudit.Line(
                ToolText.RunToolName,
                client,
                verdict.Decision,
                verdict.Method,
                verdict.Reason,
                options.Exposure,
                AuditArgs.ForRun(group, call.Reason),
                options.VaultKey) with
            {
                Session = Session,
                GrantedSeconds = verdict.Decision == AuditDecision.Granted ? verdict.GrantedSeconds : null,
                Entries = Entries,
                Command = shown.Length > AuditRecord.CommandLength ? shown[..(AuditRecord.CommandLength - 1)] + "…" : shown,
                CommandSha256 = call.Command.Count == 0 ? null : AuditRecord.HashOf([Program ?? call.Command[0], .. call.Command.Skip(1)]),
            };
        }
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
