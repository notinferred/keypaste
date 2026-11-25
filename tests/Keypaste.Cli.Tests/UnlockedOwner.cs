using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The harness's vault held unlocked and served on a pipe of its own, as <c>keypaste approve</c> composes
/// it, answering every env prompt and terminal edit the same way, saving each edit it allows, and issuing
/// the passes the app's Run confirmation issues.
/// </summary>
internal sealed class UnlockedOwner : IAsyncDisposable
{
    private readonly Vault _vault;
    private readonly LaunchPasses _launches;
    private readonly ApprovalGate _gate;
    private readonly AuditLog? _audit;
    private readonly string _auditPath;
    private readonly ServedOwner _served;

    private UnlockedOwner(CliHarness harness, string pipe, string master, ApprovalAnswer answer, HardwareKey? hardwareKey, string? auditPath, TimeProvider? clock)
    {
        var releasesOn = clock ?? TimeProvider.System;
        _vault = ClaimedVault.Open(harness.VaultPath, master, null, hardwareKey);
        _launches = new LaunchPasses(releasesOn);
        Lifetime.Own(_launches);
        Channel = new ScriptedChannel(answer);
        _gate = new ApprovalGate(Channel, TimeProvider.System, ApprovalLimits.Default);
        _auditPath = auditPath ?? KeypasteHome.AuditPath(harness.Directory);
        _audit = AuditLog.TryOpen(_auditPath, TimeProvider.System, out var audit, out var error)
            ? audit
            : throw new InvalidOperationException(error);

        var editor = new SessionEditor(
            _gate,
            asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null,
            Write,
            TimeProvider.System,
            narrate: null,
            edited: null);

        _served = ServedOwner.Serve(
            new SessionAuthority(
                VaultIdentity.Of(harness.Directory, harness.VaultPath),
                () => Lifetime,
                ServedOwner.VaultHandler(() => _vault, _gate, IAgentRules.Defaults),
                () => _audit,
                new SessionEnvironments(
                    _gate,
                    asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null,
                    releasesOn,
                    Launches: _launches),
                editor: editor),
            pipe);
    }

    internal ScriptedChannel Channel { get; }

    internal SessionLifetime Lifetime { get; } = new();

    /// <summary>Holds the harness's vault on <paramref name="pipe"/>; a vault that also needs <paramref name="hardwareKey"/> has it asked by every save the owner makes, as the app's saves do, and the caller disposes it after the owner.</summary>
    /// <remarks>
    /// It records in the harness home's log unless <paramref name="auditPath"/> names another, and times its releases and
    /// passes on <paramref name="clock"/>, the system's by default.
    /// </remarks>
    internal static UnlockedOwner Start(CliHarness harness, string pipe, string master, ApprovalAnswer answer, HardwareKey? hardwareKey = null, string? auditPath = null, TimeProvider? clock = null) =>
        new(harness, pipe, master, answer, hardwareKey, auditPath, clock);

    /// <summary>The command the run Terminal starts for a Run in the app sends: Terminal's fixed script around the person's command, the only one the owner issues a pass for.</summary>
    internal static string[] InTerminal(string command) => ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", command];

    /// <summary>Confirms a run as the app's card does: the project's set as the vault holds it now, for exactly this request.</summary>
    /// <returns>The pass the launcher hands <c>keypaste run</c>.</returns>
    internal string Confirm(string project, string profile, string directory, IReadOnlyList<string> command)
    {
        var preview = EnvResolution.Resolve(_vault, project, profile, keys: null, TimeProvider.System).Preview;

        return _served.Authority.IssueLaunch(new LaunchClaim(project, profile, directory, command, preview), Lifetime.Id)
            ?? throw new InvalidOperationException("the owner issued no pass");
    }

    /// <summary>Saves a terminal's edit the person allowed, as <c>keypaste approve</c> does under its claim.</summary>
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
        await _served.DisposeAsync();
        Lifetime.Dispose();
        _gate.Dispose();
        _launches.Dispose();
        _audit?.Dispose();
        _vault.Dispose();
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
