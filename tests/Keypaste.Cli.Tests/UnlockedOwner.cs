using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The harness's vault held unlocked and served on a pipe of its own, as <c>keypaste agent</c> composes
/// it, answering every env prompt and terminal edit the same way, saving each edit it allows, and issuing
/// the passes the app's Run confirmation issues.
/// </summary>
internal sealed class UnlockedOwner : IAsyncDisposable
{
    private readonly Vault _vault;
    private readonly GrantCache _grants = new(TimeProvider.System);
    private readonly LaunchPasses _launches = new(TimeProvider.System);
    private readonly ApprovalGate _gate;
    private readonly AuditLog? _audit;
    private readonly string _auditPath;
    private readonly SessionAuthority _authority;
    private readonly ApproverListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _running;

    private UnlockedOwner(CliHarness harness, string pipe, string master, ApprovalAnswer answer)
    {
        _vault = Vault.Open(harness.VaultPath, master);
        Lifetime.Own(_launches);
        Channel = new ScriptedChannel(answer);
        _gate = new ApprovalGate(Channel, TimeProvider.System, ApprovalLimits.Default);
        _auditPath = KeypasteHome.AuditPath(harness.Directory);
        _audit = AuditLog.TryOpen(_auditPath, TimeProvider.System, out var audit, out var error)
            ? audit
            : throw new InvalidOperationException(error);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _gate,
            _grants,
            IAgentSettings.Defaults);

        var editor = new SessionEditor(
            _gate,
            asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null,
            Write,
            KeypasteHome.ClientsPath(harness.Directory),
            TimeProvider.System,
            narrate: null,
            edited: null);

        _authority = new SessionAuthority(
            VaultIdentity.Of(harness.Directory, harness.VaultPath),
            () => Lifetime,
            handler,
            () => _audit,
            new SessionEnvironments(
                _gate,
                asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null,
                TimeProvider.System,
                Launches: _launches),
            editor: editor);

        _listener = new ApproverListener(pipe, _authority);
        _running = _listener.RunAsync(_stop.Token);
    }

    internal ScriptedChannel Channel { get; }

    internal SessionLifetime Lifetime { get; } = new();

    internal static UnlockedOwner Start(CliHarness harness, string pipe, string master, ApprovalAnswer answer) =>
        new(harness, pipe, master, answer);

    /// <summary>The command the run Terminal starts for a Run in the app sends: Terminal's fixed script around the person's command, the only one the owner issues a pass for.</summary>
    internal static string[] InTerminal(string command) => ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", command];

    /// <summary>Confirms a run as the app's card does: the project's set as the vault holds it now, for exactly this request.</summary>
    /// <returns>The pass the launcher hands <c>keypaste run</c>.</returns>
    internal string Confirm(string project, string profile, string directory, IReadOnlyList<string> command)
    {
        var preview = EnvResolution.Resolve(_vault, project, profile, keys: null, TimeProvider.System).Preview;

        return _authority.IssueLaunch(new LaunchClaim(project, profile, directory, command, preview), Lifetime.Id)
            ?? throw new InvalidOperationException("the owner issued no pass");
    }

    /// <summary>Saves a terminal's edit the person allowed, as <c>keypaste agent</c> does under its claim.</summary>
    private AgentSave Write(SessionLifetime asked, Func<Vault, bool> apply)
    {
        if (!ReferenceEquals(asked, Lifetime) || !asked.IsLive)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }

        if (!apply(_vault))
        {
            return AgentSave.Unchanged;
        }

        _vault.Save();
        return AgentSave.Saved;
    }

    /// <summary>Changes the vault the owner holds and saves it, as an edit in the app would.</summary>
    internal void Edit(Action<Vault> edit)
    {
        edit(_vault);
        _vault.Save();
    }

    /// <summary>The owner's log as it holds it open for writing, which File.ReadAllLines cannot share on Windows.</summary>
    internal List<string> AuditLines()
    {
        using var stream = new FileStream(_auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        try
        {
            await _running;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }

        _listener.Dispose();
        Lifetime.Dispose();
        _gate.Dispose();
        _grants.Dispose();
        _launches.Dispose();
        _audit?.Dispose();
        _vault.Dispose();
        _stop.Dispose();
    }

    /// <summary>Answers env prompts and terminal edits with a fixed answer and remembers the last of each.</summary>
    internal sealed class ScriptedChannel(ApprovalAnswer answer) : IApprovalChannel
    {
        internal EnvReleasePrompt? Last { get; private set; }

        internal TerminalEditPrompt? LastEdit { get; private set; }

        internal int EditsAsked { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken)
        {
            LastEdit = prompt;
            EditsAsked++;
            return ValueTask.FromResult(answer);
        }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken)
        {
            Last = prompt;
            return ValueTask.FromResult(answer);
        }
    }
}
