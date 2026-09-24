using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Launch;
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
/// </remarks>
internal sealed class ProjectLaunchViewModel : ObservableObject, IDisposable
{
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
        private set
        {
            if (Set(ref _mapping, value))
            {
                Raise(nameof(IsMapped));
                RunProjectCommand.RaiseCanExecuteChanged();
                OpenTerminalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsMapped => _mapping is not null;

    /// <summary>Whether this platform has a terminal the app can open.</summary>
    internal bool IsSupported => _launching.Terminal.IsSupported;

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

        if (!_launching.Terminal.TryPlan(mapping.Directory, run ? mapping.Command : null, out var target, out var refusal))
        {
            _report($"Nothing was started: {refusal}.");
            return;
        }

        EnvResolved resolved;

        try
        {
            resolved = await _session.Environments
                .ResolveAsync(_project, (preview, token) => AskAsync(preview, target!, run, token), _withdrawn.Token)
                .ConfigureAwait(true);
        }
        finally
        {
            EndConfirmation();
        }

        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            _report(Refused(resolved));
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

    private async ValueTask<bool> AskAsync(EnvPreview preview, LaunchTarget target, bool run, CancellationToken token)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var withdrawn = token.Register(() => answer.TrySetCanceled(token));

        ConfirmTitle = run
            ? $"Run this in a terminal with {Count(preview.Keys.Count)} from {Shown(_project)}?"
            : $"Open a terminal with {Count(preview.Keys.Count)} from {Shown(_project)}?";
        ConfirmStarts = run ? Shown(_mapping?.Command ?? string.Empty) : Shown(target.FileName);
        ConfirmDirectory = Shown(target.WorkingDirectory ?? string.Empty);
        ConfirmKeys = preview.Keys.Count == 0 ? "(none)" : string.Join(", ", preview.Keys.Select(key => EntryNameSanitizer.Sanitize(key).Text));

        _answer = answer;
        RaiseConfirming();

        return await answer.Task.ConfigureAwait(true);
    }

    private void EndConfirmation()
    {
        _answer = null;
        ConfirmTitle = string.Empty;
        ConfirmStarts = string.Empty;
        ConfirmDirectory = string.Empty;
        ConfirmKeys = string.Empty;
        RaiseConfirming();
    }

    private void RaiseConfirming()
    {
        Raise(nameof(IsConfirming));
        ConfirmLaunchCommand.RaiseCanExecuteChanged();
        CancelLaunchCommand.RaiseCanExecuteChanged();
        RunProjectCommand.RaiseCanExecuteChanged();
        OpenTerminalCommand.RaiseCanExecuteChanged();
    }

    private static string Refused(EnvResolved resolved) => resolved.Outcome switch
    {
        EnvOutcome.Declined => "Nothing was started.",
        EnvOutcome.Locked => "The vault was locked, so nothing was started.",
        EnvOutcome.Unusable => Shown(
            $"{EnvConvention.GroupPath(resolved.Project)} cannot be used, so nothing was started: " +
            string.Join("; ", resolved.Problems.Select(problem => $"{EnvResolved.Display(problem.Key)} {problem.Reason}")) +
            ". Fix or remove them, then try again."),
        _ => Shown($"Nothing was started: {resolved.Refusal}."),
    };

    private static string Count(int keys) => keys == 1 ? "1 variable" : $"{keys} variables";

    private static string Shown(string text) => DisplayTextSanitizer.Sanitize(text, 2048).Text;
}
