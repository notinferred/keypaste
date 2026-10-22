using System.Runtime.Versioning;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Processes;
using Keypaste.Core.Projects;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One project's directory and command on this machine, and the Run and Open terminal that start
/// the platform's terminal there with the project's set (E.1b).
/// </summary>
/// <remarks>
/// <para>
/// Every launch goes through <see cref="AppVaultSession.Environments"/>, and the confirmation card
/// this screen shows is the resolver's confirmation: it sees names, never values, and the set is
/// read again after the person answers. A lock while the card is up withdraws it, and a lock before
/// the resolver commits starts nothing.
/// </para>
/// <para>
/// The released set is held only for the call that starts the terminal. The mapping saved to
/// <c>projects.json</c> holds a directory and a command and nothing else (D-0339).
/// </para>
/// <para>
/// On macOS the set never reaches Terminal: the confirmation is the same card, and the run
/// Terminal starts presents the launch pass it earned to the app's launch endpoint, which answers
/// whether or not agents can reach the vault (D-0451, D-0452). On Windows and Linux the app's
/// authority writes the same History line before the terminal starts, and a Run whose line cannot
/// be written starts nothing (D-0496).
/// </para>
/// </remarks>
internal sealed class ProjectLaunchViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _openerWait = TimeSpan.FromSeconds(10);

    private readonly AppVaultSession _session;
    private readonly string _project;
    private readonly IVaultFilePicker? _picker;
    private readonly ProjectLaunching _launching;
    private readonly Action<string?> _report;
    private readonly Action<string?> _announce;
    private readonly CancellationTokenSource _withdrawn = new();

    private ProjectMapping? _mapping;
    private string _directory = string.Empty;
    private string _command = string.Empty;
    private TaskCompletionSource<bool>? _answer;
    private string _confirmTitle = string.Empty;
    private string _confirmStarts = string.Empty;
    private string _confirmDirectory = string.Empty;
    private string _confirmKeys = string.Empty;
    private string _confirmSources = string.Empty;

    internal ProjectLaunchViewModel(
        AppVaultSession session,
        string project,
        IVaultFilePicker? picker,
        ProjectLaunching launching,
        Action<string?> report,
        Action<string?> announce)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(launching);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(announce);

        _session = session;
        _project = project;
        _picker = picker;
        _launching = launching;
        _report = report;
        _announce = announce;

        ChooseDirectoryCommand = new AsyncRelayCommand(ChooseDirectoryAsync, () => _picker is not null);
        SaveCommand = new RelayCommand(Save);
        RunProjectCommand = new AsyncRelayCommand(() => LaunchAsync(run: true), CanLaunch);
        OpenTerminalCommand = new AsyncRelayCommand(() => LaunchAsync(run: false), CanLaunch);
        ConfirmLaunchCommand = new RelayCommand(() => _answer?.TrySetResult(true), () => IsConfirming);
        CancelLaunchCommand = new RelayCommand(() => _answer?.TrySetResult(false), () => IsConfirming);

        DependsOn(nameof(IsMapped), nameof(Mapping));
        DependsOn(RunProjectCommand, nameof(IsMapped), nameof(IsConfirming));
        DependsOn(OpenTerminalCommand, nameof(IsMapped), nameof(IsConfirming));
        DependsOn(ConfirmLaunchCommand, nameof(IsConfirming));
        DependsOn(CancelLaunchCommand, nameof(IsConfirming));

        if (ProjectMappings.TryLoad(MappingsPath, out var mappings)
            && _session.VaultPath is { } vault
            && ProjectMappings.Find(mappings, vault, project) is { } mapping)
        {
            Mapping = mapping;
            _directory = mapping.Directory;
            _command = mapping.Command;
        }
    }

    /// <summary>The directory the project runs in, as typed or chosen.</summary>
    internal string Directory
    {
        get => _directory;
        set => Set(ref _directory, value);
    }

    /// <summary>The command, as a person would type it in that directory.</summary>
    internal string Command
    {
        get => _command;
        set => Set(ref _command, value);
    }

    /// <summary>The saved mapping Run and Open terminal use, or null until one is saved.</summary>
    internal ProjectMapping? Mapping
    {
        get => _mapping;
        private set => Set(ref _mapping, value);
    }

    internal bool IsMapped => _mapping is not null;

    /// <summary>The profile Run and Open terminal start with; the project card sets it.</summary>
    internal string Profile { get; set; } = EnvProfileNames.Default;

    /// <summary>Whether this platform has a terminal the app can open.</summary>
    internal bool IsSupported => _launching.Terminal.IsSupported;

    /// <summary>Why this copy of the app opens no terminal, or null when it opens one.</summary>
    internal string? UnsupportedReason => _launching.Terminal.UnsupportedReason;

    /// <summary>A launch waiting on the person's answer.</summary>
    internal bool IsConfirming => _answer is not null;

    /// <summary>What the card asks.</summary>
    internal string ConfirmTitle
    {
        get => _confirmTitle;
        private set => Set(ref _confirmTitle, value);
    }

    /// <summary>Exactly what starts: the command for Run, the terminal for Open terminal.</summary>
    internal string ConfirmStarts
    {
        get => _confirmStarts;
        private set => Set(ref _confirmStarts, value);
    }

    /// <summary>The directory it starts in.</summary>
    internal string ConfirmDirectory
    {
        get => _confirmDirectory;
        private set => Set(ref _confirmDirectory, value);
    }

    /// <summary>The names of the variables it gets, never their values.</summary>
    internal string ConfirmKeys
    {
        get => _confirmKeys;
        private set => Set(ref _confirmKeys, value);
    }

    /// <summary>The entries the values come from, each once.</summary>
    internal string ConfirmSources
    {
        get => _confirmSources;
        private set => Set(ref _confirmSources, value);
    }

    /// <summary>How the last start went, for a test to find the child it started.</summary>
    internal ChildResult? LastStart { get; private set; }

    internal AsyncRelayCommand ChooseDirectoryCommand { get; }

    internal RelayCommand SaveCommand { get; }

    internal AsyncRelayCommand RunProjectCommand { get; }

    internal AsyncRelayCommand OpenTerminalCommand { get; }

    internal RelayCommand ConfirmLaunchCommand { get; }

    internal RelayCommand CancelLaunchCommand { get; }

    private string MappingsPath => KeypasteHome.ProjectsPath(_session.Home);

    /// <summary>Withdraws a waiting confirmation, which starts nothing.</summary>
    public void Dispose()
    {
        _withdrawn.Cancel();
        _answer?.TrySetResult(false);
        _withdrawn.Dispose();
    }

    private bool CanLaunch() => IsMapped && IsSupported && !IsConfirming;

    private async Task ChooseDirectoryAsync()
    {
        if (_picker is not null && await _picker.PickFolderAsync().ConfigureAwait(true) is { } picked)
        {
            Directory = picked;
        }
    }

    private void Save()
    {
        if (_session.VaultPath is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        var directory = Directory.Trim();
        var command = Command.Trim();

        if (!ProjectMappings.IsUsable(directory, command, out var invalid))
        {
            _report(invalid);
            return;
        }

        if (!System.IO.Directory.Exists(directory))
        {
            _report($"There is no directory at {directory}.");
            return;
        }

        // A file that could not be read is left as it is rather than replaced by this one mapping.
        if (!ProjectMappings.TryLoad(MappingsPath, out var mappings))
        {
            _report($"{MappingsPath} could not be read, so it was left as it is. Fix or delete it, then save again.");
            return;
        }

        var mapping = new ProjectMapping(vault, _project, directory, command);

        if (!ProjectMappings.Save(MappingsPath, ProjectMappings.Put(mappings, mapping)))
        {
            _report($"{MappingsPath} could not be written.");
            return;
        }

        Mapping = mapping;
        Directory = directory;
        Command = command;
        _report(null);
        _announce("Saved. Run starts the command in a terminal there, after you confirm it.");
    }

    /// <summary>Asks, resolves and, only on a released set, starts the terminal.</summary>
    /// <param name="run">True to run the command, false for a prompt.</param>
    internal async Task LaunchAsync(bool run)
    {
        _report(null);

        if (_mapping is not { } mapping)
        {
            _report("Save a directory and a command first.");
            return;
        }

        if (OperatingSystem.IsMacOS() && _launching.Terminal.Platform == TerminalPlatform.MacOS)
        {
            await OpenTerminalAsync(mapping, run).ConfigureAwait(true);
            return;
        }

        if (!_launching.Terminal.TryPlan(mapping.Directory, run ? mapping.Command : null, out var target, out var refusal))
        {
            _report($"Nothing was started: {refusal}.");
            return;
        }

        if (_launching.Authority is not { } authority)
        {
            _report("Nothing was started: a Run from the app writes a History line, and nothing in this app records one now. Copy the run command to start it in a terminal instead.");
            return;
        }

        // A Run's line is written before it starts, so it is not confirmed while the log will not open (T-6).
        if (authority.AuditFailure is { } unopened)
        {
            _report(Shown($"Nothing was started: a Run from the app writes a History line, and {unopened.TrimEnd('.')}. Copy the run command to start it in a terminal instead."));
            return;
        }

        // The line names the session the card is answered in, which must still be the live one after Start.
        var lifetime = _session.Lifetime?.Id;
        string? unrecordable = null;
        EnvResolved resolved;

        // A set its History line could not name is refused before the card, as on macOS.
        ValueTask<bool> ConfirmAsync(EnvPreview preview, CancellationToken token)
        {
            unrecordable = SessionAuthority.Unrecordable(preview);

            return unrecordable is null
                ? AskAsync(preview, run ? mapping.Command : target!.FileName, target!.WorkingDirectory ?? string.Empty, run, token)
                : ValueTask.FromResult(false);
        }

        try
        {
            resolved = await _session.Environments
                .ResolveAsync(_project, Profile, keys: null, ConfirmAsync, _withdrawn.Token)
                .ConfigureAwait(true);
        }
        finally
        {
            EndConfirmation();
        }

        if (unrecordable is not null)
        {
            _report(Shown($"Nothing was started: {Source(resolved.Profile)}'s variables come from more items than one History line can name, and a Run from the app writes one. Copy the run command to start it in a terminal instead."));
            return;
        }

        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            _report(Refused(resolved));
            return;
        }

        if (lifetime is null || !authority.RecordRun(resolved, lifetime))
        {
            _report("Nothing was started: its History line could not be written. Check that the audit log can be written, then run it again.");
            return;
        }

        var result = EnvLaunch.Start(resolved, target!, _launching.Parent(), _launching.Launcher);
        LastStart = result;

        if (result.Outcome == ChildOutcome.Started)
        {
            _announce(run
                ? $"Started {mapping.Command} in a terminal in {mapping.Directory}."
                : $"Opened a terminal in {mapping.Directory}.");
        }
        else
        {
            _report($"Nothing was started: {result.Error}.");
        }
    }

    /// <summary>
    /// Asks, then hands Terminal the run the person confirmed: the window's <c>keypaste run --session</c>
    /// presents the launch pass this issues, and the owner releases the set to it unasked.
    /// </summary>
    /// <remarks>
    /// The set is resolved only so the card is the resolver's, and goes no further. Once Start has
    /// released it the launch is the person's, whatever the screen does next, as on Windows and Linux;
    /// a lock ends the unlock holding the pass, so a request it leaves behind starts nothing.
    /// </remarks>
    [SupportedOSPlatform("macos")]
    private async Task OpenTerminalAsync(ProjectMapping mapping, bool run)
    {
        if (!_launching.Terminal.TryPlanThroughSession(mapping.Directory, run ? mapping.Command : null, _launching.Parent(), out var plan, out var refusal))
        {
            _report(Shown($"Nothing was started: {refusal}."));
            return;
        }

        if (_session.VaultPath is not { } vault || _session.Lifetime?.Id is not { } lifetime)
        {
            _report("The vault was locked, so nothing was started.");
            return;
        }

        // The owner's rule reads the request and not the set, which the card has yet to show.
        if (TerminalLaunch.ClaimProblem(new LaunchClaim(_project, Profile, plan.Directory, plan.Arguments, new EnvPreview(_project, []))) is { } unhandable)
        {
            _report(Shown($"Nothing was started: {unhandable}."));
            return;
        }

        if (_launching.Requests is not { } requests)
        {
            _report("Nothing was started: macOS named no private temporary folder to hand the run to Terminal through. Copy the run command instead.");
            return;
        }

        if (_launching.Authority is not { } authority || authority.LaunchFailure is not null)
        {
            _report(Unserved(_launching.Authority));
            return;
        }

        // The owner refuses a request it cannot record (T-6), so a Run is not confirmed while the log will not open.
        if (authority.AuditFailure is { } unopened)
        {
            _report(Shown($"Nothing was started: a Run from the app writes a History line, and {unopened.TrimEnd('.')}. Copy the run command to start it in a terminal instead."));
            return;
        }

        string? unrecordable = null;
        EnvResolved resolved;

        // The owner refuses unasked a set its History line could not name, so the card is not shown for one.
        ValueTask<bool> ConfirmAsync(EnvPreview preview, CancellationToken token)
        {
            unrecordable = SessionAuthority.Unrecordable(preview);

            return unrecordable is null
                ? AskAsync(preview, run ? mapping.Command : TerminalOpen.ShownName, plan.Directory, run, token)
                : ValueTask.FromResult(false);
        }

        try
        {
            resolved = await _session.Environments
                .ResolveAsync(_project, Profile, keys: null, ConfirmAsync, _withdrawn.Token)
                .ConfigureAwait(true);
        }
        finally
        {
            EndConfirmation();
        }

        if (unrecordable is not null)
        {
            _report(Shown($"Nothing was started: {Source(resolved.Profile)}'s variables come from more items than one History line can name, and a Run from the app writes one. Copy the run command to start it in a terminal instead."));
            return;
        }

        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            _report(Refused(resolved));
            return;
        }

        if (!SessionAuthority.Sendable(resolved))
        {
            _report(Shown($"Nothing was started: {Source(resolved.Profile)}'s variables are too large for keypaste to hand to Terminal."));
            return;
        }

        // The set read again after Start, which the resolver held to the card's environment, names, items and protection.
        var claim = new LaunchClaim(_project, resolved.Profile, plan.Directory, plan.Arguments, resolved.Preview);

        if (authority.IssueLaunch(claim, lifetime) is not { } issued)
        {
            _report(Unserved(authority));
            return;
        }

        var request = new TerminalRequest(issued.Pass, _session.Home, vault, issued.Endpoint, Path.GetTempPath(), _project, resolved.Profile, plan.Directory, plan.Command);

        if (!requests.TryWrite(request, out var path, out refusal))
        {
            authority.WithdrawLaunch(issued.Pass);
            _report(Shown($"Nothing was started: {refusal}."));
            return;
        }

        var opened = await Task.Run(() => _launching.Opener(plan, _openerWait)).ConfigureAwait(true);

        if (!opened.Succeeded)
        {
            requests.Withdraw(path);
            authority.WithdrawLaunch(issued.Pass);
            _report(Shown($"Nothing was started: Terminal did not open: {OpenerFailure(plan.Opener, opened)}."));
            return;
        }

        _announce(run
            ? $"Started {mapping.Command} in a terminal in {mapping.Directory}."
            : $"Opened a terminal in {mapping.Directory}.");
    }

    private async ValueTask<bool> AskAsync(EnvPreview preview, string starts, string directory, bool run, CancellationToken token)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var withdrawn = token.Register(() => answer.TrySetCanceled(token));

        ConfirmTitle = run
            ? $"Run this in a terminal with {Count(preview.Keys.Count)} from {Source(preview.Profile)}?"
            : $"Open a terminal with {Count(preview.Keys.Count)} from {Source(preview.Profile)}?";
        ConfirmStarts = DisplayTextSanitizer.Sanitize(starts, ProjectMappings.MaximumCommandLength).Text;
        ConfirmDirectory = Shown(directory);
        ConfirmKeys = preview.Keys.Count == 0 ? "(none)" : string.Join(", ", preview.Keys.Select(key => EntryNameSanitizer.Sanitize(key).Text));
        ConfirmSources = preview.Sources.Count == 0
            ? string.Empty
            : "From " + string.Join(", ", preview.Sources.Select(source => source.Entry).Distinct().Select(ApprovalPrompt.Shown));

        _answer = answer;
        Raise(nameof(IsConfirming));

        return await answer.Task.ConfigureAwait(true);
    }

    private void EndConfirmation()
    {
        _answer = null;
        ConfirmTitle = string.Empty;
        ConfirmStarts = string.Empty;
        ConfirmDirectory = string.Empty;
        ConfirmKeys = string.Empty;
        ConfirmSources = string.Empty;
        Raise(nameof(IsConfirming));
    }

    private static string Refused(EnvResolved resolved) => resolved.Outcome switch
    {
        EnvOutcome.Declined => "Nothing was started.",
        EnvOutcome.Locked => "The vault was locked, so nothing was started.",
        EnvOutcome.Unusable => Shown(
            $"{resolved.Project}/{resolved.Profile} cannot be used, so nothing was started: " +
            string.Join("; ", resolved.Problems.Select(problem => $"{problem.Key} {problem.Reason}")) +
            ". Fix or remove them, then try again."),
        _ => Shown($"Nothing was started: {resolved.Refusal}."),
    };

    /// <summary>Why the run Terminal would start could not ask this app for the set.</summary>
    /// <remarks>A launch endpoint that answers yet issues no pass means the card's unlock ended.</remarks>
    private static string Unserved(AppAuthority? authority) => authority switch
    {
        null => Unreachable("nothing in this app answers Terminal"),
        { LaunchFailure: { } failure } => Unreachable(failure),
        _ => "The vault was locked, so nothing was started.",
    };

    private static string Unreachable(string reason) =>
        Shown($"Nothing was started: Terminal gets the variables from this app, and {reason}. Copy the run command instead.");

    private static string OpenerFailure(string opener, ProcessResult opened) =>
        !opened.ToolFound ? $"{opener} could not be started"
        : opened.StandardError.Trim() is { Length: > 0 } said ? said
        : $"{opener} exited with {opened.ExitCode}";

    /// <summary>The project and, for any profile but the default, the profile, as the confirmation names them.</summary>
    private string Source(string profile) => string.Equals(profile, EnvProfileNames.Default, StringComparison.Ordinal)
        ? Shown(_project)
        : $"{Shown(_project)} ({Shown(profile)})";

    private static string Count(int keys) => keys == 1 ? "1 variable" : $"{keys} variables";

    private static string Shown(string text) => DisplayTextSanitizer.Sanitize(text, 2048).Text;
}
