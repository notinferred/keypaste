using System.Security.Cryptography;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tokens;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste run --token</c> and <c>keypaste run --bundle</c>: a set released under a scoped token,
/// from the process holding the vault or from a bundle, with nobody asked unless the profile is protected.
/// </summary>
/// <remarks>
/// <para>
/// Against a session the owner verifies the token and writes the audit line; this process opens no
/// vault and writes nothing. A bundle is opened here with no vault, no pipe and no audit line: its
/// creation was audited, and the machine running CI is not the person's log.
/// </para>
/// <para>
/// Either way the child's environment is the parent's without <c>KEYPASTE_TOKEN</c>, so the token
/// is not handed on to whatever the command starts.
/// </para>
/// </remarks>
internal static class RunWithToken
{
    internal const string TokenOption = "token";
    internal const string BundleOption = "bundle";
    internal const string ProfileOption = "profile";
    internal const string EnvironmentVariable = "KEYPASTE_TOKEN";

    /// <summary>How long to wait for the owner's answer: its longest window, for a protected profile, and time for the reply.</summary>
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    internal static int Execute(CommandLine line, IReadOnlyList<string> command, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (line.Operands.Count > 1)
        {
            return Fail(context, "expected at most one project name");
        }

        if (line.Value("keyfile") is not null)
        {
            return Fail(context, "a token opens no vault here, so it takes no --keyfile");
        }

        if (line.Value(RunCommand.EnvFileOption) is not null)
        {
            return Fail(context, $"a token names its set, so it takes no --{RunCommand.EnvFileOption}");
        }

        var profile = line.Value(ProfileOption);

        if (profile is not null && !EnvProfileNames.IsValid(profile, out var invalid))
        {
            return Fail(context, invalid);
        }

        if (!TryReadToken(line.Value(TokenOption) ?? "env", "keypaste run", context, out var token, out var id, out var exit))
        {
            return exit;
        }

        var project = line.Operands.Count == 1 ? line.Operands[0] : null;

        return line.Value(BundleOption) is { } bundle
            ? FromBundle(bundle, token, id, project, profile, command, context)
            : FromSession(line, token, id, project, profile ?? EnvProfileNames.Default, command, context);
    }

    /// <summary>Reads the token <c>--token</c> names: a literal, <c>-</c> for one line of stdin, or <c>env</c> for <see cref="EnvironmentVariable"/>.</summary>
    /// <remarks>A malformed token is refused without being repeated, since it may be a secret pasted into the wrong place.</remarks>
    internal static bool TryReadToken(string source, string verb, CliContext context, out string token, out string id, out int exit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);

        token = string.Empty;
        id = string.Empty;
        exit = CliApp.ExitUsageError;

        switch (source)
        {
            case "env":
                if (context.Environment.Get(EnvironmentVariable) is not { Length: > 0 } fromEnvironment)
                {
                    context.Stderr.WriteLine($"{verb}: {EnvironmentVariable} is not set");
                    return false;
                }

                token = fromEnvironment.Trim();
                break;

            case "-":
                using (var read = context.Prompt.ReadSecret("Token: "))
                {
                    if (read is null)
                    {
                        context.Stderr.WriteLine($"{verb}: no token given");
                        return false;
                    }

                    token = new string(read.Value).Trim();
                }

                break;

            default:
                context.Stderr.WriteLine($"{verb}: a token on the command line is visible to other processes; prefer {EnvironmentVariable}");
                token = source;
                break;
        }

        if (!TokenSecret.TryParse(token, out var parsed, out var secret))
        {
            context.Stderr.WriteLine($"{verb}: that is not a keypaste token");
            token = string.Empty;
            return false;
        }

        CryptographicOperations.ZeroMemory(secret);
        id = parsed;
        exit = CliApp.ExitSuccess;
        return true;
    }

    private static int FromBundle(
        string path,
        string token,
        string id,
        string? project,
        string? profile,
        IReadOnlyList<string> command,
        CliContext context)
    {
        byte[] file;

        try
        {
            if (!File.Exists(path))
            {
                context.Stderr.WriteLine($"keypaste run: no bundle at '{path}'");
                return CliApp.ExitNotFound;
            }

            if (new FileInfo(path).Length > TokenBundle.MaximumBytes)
            {
                context.Stderr.WriteLine("keypaste run: the bundle is larger than any keypaste makes, so nothing was started");
                return CliApp.ExitInternalError;
            }

            file = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Stderr.WriteLine($"keypaste run: the bundle could not be read: {ex.Message}");
            return CliApp.ExitInternalError;
        }

        if (!TokenBundle.TryOpen(file, token, context.Clock.GetUtcNow(), out var contents, out var error))
        {
            context.Stderr.WriteLine($"keypaste run: {error}, so nothing was started");
            return CliApp.ExitInternalError;
        }

        var choices = contents.Pairs
            .Where(pair => project is null || string.Equals(pair.Project, project, StringComparison.Ordinal))
            .Where(pair => profile is null || string.Equals(pair.Profile, profile, StringComparison.Ordinal))
            .ToList();

        if (choices.Count != 1)
        {
            var held = string.Join(", ", contents.Pairs.Select(pair => $"{pair.Project} -p {pair.Profile}"));

            context.Stderr.WriteLine(choices.Count == 0
                ? $"keypaste run: the bundle holds no {project ?? "such"} set{(profile is null ? string.Empty : $" for '{profile}'")}; it holds {held}"
                : $"keypaste run: the bundle holds several sets; name one: {held}");
            return choices.Count == 0 ? CliApp.ExitNotFound : CliApp.ExitUsageError;
        }

        return Start(command, contents.Resolve(choices[0].Project, choices[0].Profile), id, context);
    }

    private static int FromSession(
        CommandLine line,
        string token,
        string id,
        string? project,
        string profile,
        IReadOnlyList<string> command,
        CliContext context)
    {
        if (project is null)
        {
            return Fail(context, "expected a project name");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            return Fail(context, locateError);
        }

        string pipe;

        try
        {
            var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));
            pipe = ApproverEndpoint.Resolve(
                line.Value(RunCommand.ApproverOption),
                context.Environment.Get(ApproverEndpoint.EnvironmentVariable),
                VaultIdentity.Of(home, vaultPath))!;
        }
        catch (ArgumentException ex)
        {
            return Fail(context, $"--{RunCommand.ApproverOption}: {ex.Message}");
        }

        var request = new TokenEnvRequest(token, project, profile, command, Environment.CurrentDirectory);
        var (reply, refusal) = AskAsync(pipe, vaultPath, request, context).GetAwaiter().GetResult();

        if (reply is null)
        {
            context.Stderr.WriteLine($"keypaste run: {Shown(refusal)}, so nothing was started");
            return CliApp.ExitInternalError;
        }

        switch (reply.Set.Outcome)
        {
            case EnvOutcome.Resolved:
                return Start(command, reply.Set, id, context);

            case EnvOutcome.NoProject or EnvOutcome.NoProfile:
                context.Stderr.WriteLine($"keypaste run: {Shown(reply.Reason)}");
                return CliApp.ExitNotFound;

            case EnvOutcome.Unusable:
                context.Stderr.WriteLine(
                    $"keypaste run: '{EnvProfileNames.GroupPath(project, profile)}' cannot be used, so nothing was started:");

                foreach (var problem in reply.Set.Problems)
                {
                    context.Stderr.WriteLine($"  {EntryNameSanitizer.Sanitize($"{EnvResolved.Display(problem.Key)} {problem.Reason}", 512).Text}");
                }

                return CliApp.ExitInternalError;

            default:
                context.Stderr.WriteLine($"keypaste run: {Shown(reply.Reason.Length > 0 ? reply.Reason : reply.Set.Refusal)}, so nothing was started");
                return CliApp.ExitInternalError;
        }
    }

    private static async Task<(EnvReply? Reply, string Refusal)> AskAsync(
        string pipe,
        string vaultPath,
        TokenEnvRequest request,
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

        if (EnvProfileNames.IsProtected(request.Profile))
        {
            context.Stderr.WriteLine(
                $"keypaste run: '{request.Profile}' is protected; answer in the prompt of the keypaste process holding {vaultPath}");
            context.Stderr.Flush();
        }

        var reply = await client.ReleaseTokenEnvAsync(request with { Vault = vaultPath, Session = session }, bound.Token);

        return (reply, bound.IsCancellationRequested
            ? "no answer came in time"
            : "the keypaste process holding the vault did not answer");
    }

    /// <summary>Starts the child with the released set over this environment less the token, and reports its exit code as keypaste's own.</summary>
    private static int Start(IReadOnlyList<string> command, EnvResolved resolved, string id, CliContext context)
    {
        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            context.Stderr.WriteLine($"keypaste run: {resolved.Refusal}, so nothing was started");
            return CliApp.ExitInternalError;
        }

        if (EnvironmentMerge.OverridesPath(resolved.Variables))
        {
            context.Stderr.WriteLine(
                $"warning: '{resolved.Project}' defines PATH; the command itself is still resolved against yours");
        }

        if (context.ConsoleStyle.IsTerminal(context.Stderr))
        {
            var dot = context.ConsoleStyle.Glyph(context.Stderr, Mark.Dot);
            context.Stderr.WriteLine(
                $"  {context.ConsoleStyle.Paint(context.Stderr, Tone.Ok, context.ConsoleStyle.Glyph(context.Stderr, Mark.Done))} " +
                $"{resolved.Variables.Count} injected under {TokenSecret.Display(id)} {dot} nothing written to disk");
        }

        // Everything keypaste has to say is said before the child owns the console.
        context.Stderr.Flush();

        var parent = new Dictionary<string, string>(EnvironmentMerge.Comparer);
        foreach (var (name, value) in context.Environment.All())
        {
            if (!EnvironmentMerge.Comparer.Equals(name, EnvironmentVariable))
            {
                parent[name] = value;
            }
        }

        var result = EnvLaunch.Start(
            resolved,
            new LaunchTarget(command[0], [.. command.Skip(1)]),
            parent,
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
}
