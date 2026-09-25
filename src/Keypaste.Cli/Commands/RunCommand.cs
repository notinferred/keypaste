using Keypaste.Cli.Styling;
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
/// <c>keypaste run [-p &lt;profile&gt;] [project] [--env-file &lt;file&gt;] -- &lt;command...&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two phases, in order. The first opens the vault and takes the set out of it; the second starts
/// the child, with no vault alive anywhere in the process — see
/// <see cref="VaultSession.OpenThen{T}"/> for why that ordering is a function rather than a habit.
/// </para>
/// <para>
/// The set is a project's profile, named or inferred from <c>projects.json</c>, or a reference file
/// (D-0349). A <c>.env.keypaste</c> found in the directory is used only when it names env
/// references to the project <c>projects.json</c> maps the directory to, and reference mode always
/// says which file, project and profile it resolved and lists every literal, because a cloned
/// repository's file is not the person's (THREATS.md T-31).
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
    internal const string EnvFileOption = "env-file";

    private const int _nameColumn = 20;
    private const int _shownReferences = 3;

    /// <summary>How long to wait for the owner's answer: its longest window, and time for the reply to arrive.</summary>
    /// <remarks>The owner decides the timeout; this only bounds an owner that never answers.</remarks>
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(ApprovalLimits.MaximumWindowSeconds + 15);

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(SessionOption, TakesValue: false),
        new(ApproverOption, TakesValue: true),
        new(EnvFileOption, TakesValue: true),
        EnvCommand.ProfileOption,
        new(RunWithToken.TokenOption, TakesValue: true),
        new(RunWithToken.BundleOption, TakesValue: true),
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

        if (line.Value(RunWithToken.TokenOption) is not null || line.Value(RunWithToken.BundleOption) is not null)
        {
            return RunWithToken.Execute(line, split.Command, context);
        }

        if (line.Operands.Count > 1)
        {
            return Fail(context, "expected at most one project name");
        }

        var envFile = line.Value(EnvFileOption);

        if (envFile is not null && line.Operands.Count == 1)
        {
            return Fail(context, $"--{EnvFileOption} names the variables to run with, so it takes no project");
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            return Fail(context, profileError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, locateError);
        }

        var session = line.HasFlag(SessionOption);

        if (session && line.Value("keyfile") is not null)
        {
            return Fail(context, "--session uses the vault another keypaste process has unlocked, so it takes no --keyfile");
        }

        if (!session && line.Value(ApproverOption) is not null)
        {
            return Fail(context, "--approver names the process to ask, which only --session does");
        }

        if (!TryChoose(line, envFile, profile, path, context, out var set, out var exit))
        {
            return exit;
        }

        if (set.File is { } file)
        {
            // A literal is shown to the person before anything is unlocked, so one that cannot be
            // shown exactly as the child would get it is refused rather than shortened (T-31).
            if (file.Lines.FirstOrDefault(fileLine => fileLine.Reference is null && Literal(fileLine) != Written(fileLine)) is { } hidden)
            {
                return Fail(
                    context,
                    $"{OneLine(set.Label)} line {hidden.Line}: the value of {OneLine(hidden.Name)} is too long or holds characters that cannot be shown on one line");
            }

            context.Stderr.WriteLine($"keypaste run: resolving {OneLine(set.Label)} {Arrow(context.Stderr)} project {OneLine(set.Project)} profile {OneLine(set.Profile)}");

            foreach (var literal in file.Lines.Where(fileLine => fileLine.Reference is null))
            {
                context.Stderr.WriteLine($"keypaste run: literal {Literal(literal)}");
            }
        }

        if (session)
        {
            return FromSession(path, line.Value(ApproverOption), set, split.Command, context);
        }

        return VaultSession.OpenThen(
            path,
            line,
            context,
            vault => Admit(Resolve(vault, set, context), set, refusal: null, context),
            resolved => Start(split.Command, resolved, set, context));
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

    /// <summary>
    /// Decides what runs: the project operand, else <c>--env-file</c>, else the project
    /// <c>projects.json</c> maps this directory to, with its <c>.env.keypaste</c> only when that file
    /// names nothing but env references to that project and literals.
    /// </summary>
    private static bool TryChoose(
        CommandLine line, string? envFile, string profile, string vaultPath, CliContext context, out RunSet set, out int exit)
    {
        set = null!;
        exit = CliApp.ExitSuccess;
        var profileGiven = line.Value(EnvCommand.ProfileOption.Name) is not null;

        if (line.Operands.Count == 1)
        {
            set = new RunSet(line.Operands[0], profile, null, line.Operands[0]);
            return true;
        }

        if (envFile is not null)
        {
            if (!TryRead(Path.GetFullPath(envFile, context.WorkingDirectory), envFile, context, out var named, out exit))
            {
                return false;
            }

            // A file of literals only is a plaintext .env, whose every line would otherwise be echoed as a literal.
            if (named.Lines.All(fileLine => fileLine.Reference is null))
            {
                exit = Fail(
                    context,
                    $"{OneLine(envFile)} names no {KpReferences.Scheme} reference, so it looks like a plaintext .env; nothing was started and none of its values were shown. " +
                    $"Import it with `keypaste env pull <project> {OneLine(envFile)}`, then run with the project or an `env export` reference file");
                return false;
            }

            set = RunSet.For(profileGiven ? EnvReferenceFile.WithProfile(named, profile) : named, envFile);
            return true;
        }

        var discovered = Path.Combine(context.WorkingDirectory, EnvReferenceFile.FileName);
        var hasFile = File.Exists(discovered);

        if (!EnvCommand.TryInferProject(
            context,
            vaultPath,
            out var project,
            out var inferError,
            hasFile ? $"pass --{EnvFileOption} {EnvReferenceFile.FileName} or name a project" : EnvCommand.NameAProject))
        {
            exit = Fail(context, inferError);
            return false;
        }

        if (!hasFile)
        {
            set = new RunSet(project, profile, null, project);
            return true;
        }

        if (!TryRead(discovered, EnvReferenceFile.FileName, context, out var document, out exit))
        {
            return false;
        }

        if (!EnvReferenceFile.IsDiscoverableFor(document, project, out var what))
        {
            exit = Fail(
                context,
                $"{EnvReferenceFile.FileName} names {Shown(what)}, not this directory's project {project}; " +
                $"pass --{EnvFileOption} {EnvReferenceFile.FileName} to use it");
            return false;
        }

        set = RunSet.For(profileGiven ? EnvReferenceFile.WithProfile(document, profile) : document, EnvReferenceFile.FileName);
        return true;
    }

    /// <summary>Reads and checks a reference file, reporting everything wrong with it at once and never a value.</summary>
    private static bool TryRead(string path, string shown, CliContext context, out EnvReferenceDocument document, out int exit)
    {
        document = null!;
        exit = CliApp.ExitSuccess;

        byte[] bytes;

        try
        {
            if (new FileInfo(path).Length > DotEnv.MaximumBytes)
            {
                exit = Fail(context, $"{shown} is larger than a .env file can be");
                return false;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            context.Stderr.WriteLine($"keypaste run: no file at '{path}'");
            exit = CliApp.ExitNotFound;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Stderr.WriteLine($"keypaste run: could not read '{path}': {ex.Message}");
            exit = CliApp.ExitInternalError;
            return false;
        }

        if (EnvReferenceFile.TryParse(bytes, out document))
        {
            return true;
        }

        context.Stderr.WriteLine($"keypaste run: {shown} cannot be used, so nothing was started:");

        foreach (var problem in document.Problems.Take(10))
        {
            context.Stderr.WriteLine($"  {EntryNameSanitizer.SanitizeProse(problem.Message, 512).Text}");
        }

        exit = CliApp.ExitUsageError;
        return false;
    }

    private static EnvResolved Resolve(Vault vault, RunSet set, CliContext context) =>
        set.File is { } file
            ? EnvReferenceResolution.Resolve(vault, file, context.Clock)
            : EnvResolution.Resolve(vault, set.Project, set.Profile, context.Clock);

    /// <summary>Asks the process holding the vault for the set, and starts the child only with what it released.</summary>
    /// <remarks>Every way of not getting the set ends here with a reason and no child; none of them opens the vault.</remarks>
    private static int FromSession(
        string vaultPath,
        string? approver,
        RunSet set,
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

        if (!TryRequest(set, command, vaultPath, context, out var request, out var exit))
        {
            return exit;
        }

        var (reply, refusal) = AskAsync(pipe, vaultPath, request, context).GetAwaiter().GetResult();

        if (reply is null)
        {
            context.Stderr.WriteLine(refusal is null
                ? $"keypaste run: the keypaste process holding {vaultPath} is older and cannot release profiles; update it, so nothing was started"
                : $"keypaste run: {refusal}, so nothing was started");
            return CliApp.ExitInternalError;
        }

        if (reply.Set.Outcome == EnvOutcome.Resolved
            && !string.Equals(reply.Set.Profile, request.Profile, StringComparison.Ordinal))
        {
            context.Stderr.WriteLine(
                $"keypaste run: the keypaste process holding {vaultPath} released the '{Shown(reply.Set.Profile)}' profile " +
                $"when '{request.Profile}' was asked for, so nothing was started");
            return CliApp.ExitInternalError;
        }

        var (admitted, resolved) = Admit(reply.Set, set with { File = null }, reply.Reason, context);

        if (resolved is null)
        {
            return admitted;
        }

        if (set.File is { } file)
        {
            (admitted, resolved) = Admit(EnvReferenceResolution.Apply(file, resolved), set, refusal: null, context);

            if (resolved is null)
            {
                return admitted;
            }
        }

        return Start(command, resolved, set, context);
    }

    /// <summary>The request a set makes: a project's profile, or a reference file's keys and lines under one profile.</summary>
    private static bool TryRequest(
        RunSet set, IReadOnlyList<string> command, string vaultPath, CliContext context, out EnvRequest request, out int exit)
    {
        request = new EnvRequest(set.Project, command, context.WorkingDirectory) { Vault = vaultPath, Profile = set.Profile };
        exit = CliApp.ExitSuccess;

        if (set.File is not { } file)
        {
            return true;
        }

        var references = file.Lines.Select(fileLine => fileLine.Reference).Where(reference => reference is not null).ToList();
        var envs = references.OfType<EnvReference>().ToList();

        if (envs.Count != references.Count || envs.Select(env => env.Project).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            exit = Fail(context, "entry references and several projects need the vault opened directly; run without --session");
            return false;
        }

        if (envs.Count == 0 || envs.Select(env => env.Profile).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            exit = Fail(context, $"{set.Label} names {(envs.Count == 0 ? "no reference" : "several profiles")}; run without --session, or pass -p to choose one profile");
            return false;
        }

        var lines = file.Lines
            .Select(fileLine => fileLine.Reference is EnvReference env ? $"{fileLine.Name} ← {env.Key}" : Written(fileLine))
            .ToList();

        request = new EnvRequest(envs[0].Project, command, context.WorkingDirectory)
        {
            Vault = vaultPath,
            Profile = envs[0].Profile,
            Keys = [.. envs.Select(env => env.Key).Distinct(StringComparer.Ordinal)],
            FileLines = lines,
        };

        // Measured with a session of the length every owner issues, so a request the framer would
        // refuse is never sent and mistaken for an owner that did not answer.
        if (lines.Count > ApproverProtocol.MaximumEnvListLength
            || lines.Any(fileLine => fileLine.Length > ApproverProtocol.MaximumFileLineLength)
            || ApproverProtocol.Encode(request with { Session = VaultOwner.NewSession() }).Length > MessageFramer.MaximumPayloadBytes)
        {
            exit = Fail(context, $"{OneLine(set.Label)} is too long for the prompt to show whole; run without --session");
            return false;
        }

        return true;
    }

    /// <returns>The reply, or null with the reason ready to print; a null reason means an owner that did not answer a profile request at all.</returns>
    private static async Task<(EnvReply? Reply, string? Refusal)> AskAsync(
        string pipe,
        string vaultPath,
        EnvRequest request,
        CliContext context)
    {
        using var bound = new CancellationTokenSource(_answerBound);

        await using var client = await ApproverClient.TryConnectAsync(pipe, TimeSpan.FromMilliseconds(500), bound.Token);

        if (client is null)
        {
            return (null, $"nothing holds {OneLine(vaultPath)} unlocked; unlock it in the keypaste app or start `keypaste agent`");
        }

        var attached = await client.AttachAsync(new AttachRequest(vaultPath), bound.Token);

        if (attached is not { Attached: true, Session: { } session })
        {
            return (null, attached?.Reason is { } reason ? Shown(reason) : "the keypaste process holding the vault did not answer");
        }

        var profiled = !string.Equals(request.Profile, EnvProfileNames.Default, StringComparison.Ordinal)
            || request.Keys is not null
            || request.FileLines is not null;

        context.Stderr.WriteLine(profiled
            ? $"keypaste run: asking the keypaste process holding {vaultPath} to release '{request.Project}' profile '{request.Profile}'; answer in its prompt"
            : $"keypaste run: asking the keypaste process holding {vaultPath} to release '{request.Project}'; answer in its prompt");
        context.Stderr.Flush();

        var reply = await client.ReleaseEnvAsync(request with { Session = session }, bound.Token);

        return (reply, bound.IsCancellationRequested ? "no answer came in time"
            : profiled ? null
            : "the keypaste process holding the vault did not answer");
    }

    /// <summary>What a resolved set comes to: the set to start with, or the exit code and a reason on stderr.</summary>
    /// <param name="resolved">The set, however it was resolved.</param>
    /// <param name="set">What was asked for.</param>
    /// <param name="refusal">The owner's words for a refusal, when an owner resolved it.</param>
    /// <param name="context">Where the reason goes.</param>
    private static (int Exit, EnvResolved? Loaded) Admit(
        EnvResolved resolved,
        RunSet set,
        string? refusal,
        CliContext context)
    {
        switch (resolved.Outcome)
        {
            case EnvOutcome.Resolved:
                break;

            case EnvOutcome.NoProject:
            case EnvOutcome.NoProfile:
                context.Stderr.WriteLine($"keypaste run: {(refusal is null ? resolved.Refusal : Shown(resolved.Refusal))}");
                return (CliApp.ExitNotFound, null);

            case EnvOutcome.Unusable or EnvOutcome.Invalid when set.File is not null:
                context.Stderr.WriteLine($"keypaste run: {OneLine(set.Label)} cannot be resolved, so nothing was started:");

                foreach (var problem in resolved.Problems)
                {
                    var line = problem.Key.Length == 0 ? problem.Reason : $"{problem.Key} {problem.Reason}";
                    context.Stderr.WriteLine($"  {EntryNameSanitizer.SanitizeProse(line, 512).Text}");
                }

                return (CliApp.ExitInternalError, null);

            case EnvOutcome.Unusable:
                context.Stderr.WriteLine(
                    $"keypaste run: '{EnvProfileNames.GroupPath(set.Project, set.Profile)}' cannot be used, so nothing was started:");

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
                $"warning: '{OneLine(set.Label)}' defines PATH; the command itself is still resolved against yours");
        }

        return (CliApp.ExitSuccess, resolved);
    }

    /// <summary>Starts the child and reports its exit code as keypaste's own.</summary>
    private static int Start(
        IReadOnlyList<string> command,
        EnvResolved resolved,
        RunSet set,
        CliContext context)
    {
        if (context.ConsoleStyle.IsTerminal(context.Stderr))
        {
            Summarize(resolved, set, context);
        }

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

    /// <summary>What was injected and where each value came from: names and references, and a literal's own text.</summary>
    private static void Summarize(EnvResolved resolved, RunSet set, CliContext context)
    {
        var style = context.ConsoleStyle;
        var writer = context.Stderr;
        var done = style.Paint(writer, Tone.Ok, style.Glyph(writer, Mark.Done));

        var rows = set.File is { } file
            ? file.Lines
            : resolved.Variables.Select(variable => new ReferenceLine(variable.Key, new EnvReference(set.Project, set.Profile, variable.Key), null, 0));

        writer.WriteLine($"  resolving {OneLine(set.Label)}  {style.Paint(writer, Tone.Muted, "profile")} {OneLine(resolved.Profile)}");

        var references = 0;

        foreach (var row in rows)
        {
            if (row.Reference is null)
            {
                writer.WriteLine($"  {style.Paint(writer, Tone.Accent, "=")} {Literal(row)}");
            }
            else if (++references <= _shownReferences)
            {
                writer.WriteLine($"  {done} {OneLine(row.Name).PadRight(_nameColumn - 1)} {style.Paint(writer, Tone.Muted, OneLine(Place(row.Reference)))}");
            }
        }

        if (references > _shownReferences)
        {
            writer.WriteLine($"  + {references - _shownReferences} more");
        }

        writer.WriteLine($"  {resolved.Variables.Count} injected {style.Glyph(writer, Mark.Dot)} nothing written to disk");
    }

    /// <summary>Where a reference points, without the key or field it names.</summary>
    private static string Place(KpReference reference)
    {
        var text = reference.ToString();
        var fragment = text.IndexOf('#', StringComparison.Ordinal);
        var path = fragment < 0 ? text : text[..fragment];
        var place = path[..path.LastIndexOf('/')];

        return place.Length > KpReferences.Scheme.Length ? place : KpReferences.Scheme + "/";
    }

    /// <summary>A literal line as the child gets it.</summary>
    private static string Written(ReferenceLine line) => $"{line.Name}={line.Literal}";

    private static string Literal(ReferenceLine line) => OneLine(Written(line));

    /// <summary>→ where the writer is Unicode, else <c>-&gt;</c>: a legacy console code page writes a control character for it.</summary>
    private static string Arrow(TextWriter writer) => writer.Encoding.CodePage is 65001 or 1200 or 1201 ? "→" : "->";

    /// <summary>Another process's words, made safe for this terminal.</summary>
    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 512).Text;

    /// <summary>A path or a file's literal as written, on one line, with nothing that draws anything else.</summary>
    internal static string OneLine(string text) =>
        DisplayTextSanitizer.Sanitize(text.Replace('\n', '\0').Replace('\t', '\0'), 1024).Text;

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste run: {message}");
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste run [--vault <path>] [--keyfile <path>] [-p <profile>] [project] -- <command> [args...]");
        writer.WriteLine("       keypaste run [--vault <path>] [--keyfile <path>] [-p <profile>] --env-file <file> -- <command> [args...]");
        writer.WriteLine("       keypaste run --session [--vault <path>] [--approver <name>] [-p <profile>] [project] -- <command> [args...]");
        writer.WriteLine("       keypaste run --token <token|-|env> [--vault <path>] [--approver <name>] [-p <profile>] <project> -- <command> [args...]");
        writer.WriteLine("       keypaste run --bundle <file> [--token <token|-|env>] [-p <profile>] [project] -- <command> [args...]");
        writer.WriteLine();
        writer.WriteLine("runs a command with the project's variables in its environment. nothing is");
        writer.WriteLine("written to disk, and the vault is closed before the command starts.");
        writer.WriteLine($"-p picks the profile, {EnvProfileNames.Default} by default. with no project, the one projects.json");
        writer.WriteLine($"maps this directory to is used, and its {EnvReferenceFile.FileName} only when that file names");
        writer.WriteLine("nothing but that project's variables; --env-file uses any reference file you name.");
        writer.WriteLine();
        writer.WriteLine("with --session, no password is asked for here: the keypaste app or `keypaste agent`");
        writer.WriteLine("holding the vault unlocked shows you the project, its variable names, the command");
        writer.WriteLine("and this directory, and the command starts only if you allow it there.");
        writer.WriteLine();
        writer.WriteLine($"with --token, a scoped token (default: {RunWithToken.EnvironmentVariable}) is checked by the process");
        writer.WriteLine("holding the vault, which releases what its scope covers without asking, except a");
        writer.WriteLine("protected profile. --bundle opens a token bundle with no vault at all.");
        writer.WriteLine();
        writer.WriteLine("the -- is required: without it, 'keypaste run dev npm start' cannot be told");
        writer.WriteLine("apart from a project called 'npm'. everything after it belongs to the command.");
        writer.WriteLine();
        writer.WriteLine("once the command starts, its exit code is keypaste's. 127 means there is no");
        writer.WriteLine("such command and 126 means it is not executable, as in a shell.");
    }

    /// <summary>What a run injects: one profile of a project, or a reference file.</summary>
    /// <param name="Project">The project, or what the file names instead.</param>
    /// <param name="Profile">The profile, or <c>mixed</c> for a file naming several.</param>
    /// <param name="File">The reference file, or null in project mode.</param>
    /// <param name="Label">How the output names the set: the project, or the file as given.</param>
    private sealed record RunSet(string Project, string Profile, EnvReferenceDocument? File, string Label)
    {
        internal static RunSet For(EnvReferenceDocument file, string label)
        {
            var references = file.Lines.Select(line => line.Reference).Where(reference => reference is not null).ToList();
            var envs = references.OfType<EnvReference>().ToList();
            var named = envs.Select(env => env.Project).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var profiles = envs.Select(env => env.Profile).Distinct(StringComparer.Ordinal).ToList();

            if (envs.Count != references.Count)
            {
                named.Add("vault entries");
            }

            return new RunSet(
                named.Count == 0 ? "(none)" : string.Join(", ", named),
                profiles.Count == 1 ? profiles[0] : EnvReferenceResolution.MixedProfile,
                file,
                label);
        }
    }
}
