using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core.Clients;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The pane under an AI tool's row on Agents: connects the tool to the vault this app has unlocked, checks
/// the connection, or disconnects it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written before the person has seen it.</b> The pane composes its plan in core
/// (<see cref="McpClientSetup"/>) when it opens and shows its display; Connect runs that same plan object, so
/// the steps confirmed and the steps run cannot differ. A tool with no command of its own has only
/// keypaste's entry in its file changed, with the first original kept beside it. Inside an AppImage the
/// link a tool starts is made or replaced first, as a step of the same plan.
/// </para>
/// <para>
/// <b>The tool is registered under its own id</b>, offering run and every item, because the owner holds
/// each agent to its settings, which follow that name.
/// </para>
/// <para>
/// <b>The check goes through the bridge the tool would start.</b> It launches that registration's
/// command line and asks as the tool would, so the request meets this app's session, its prompt
/// window and its audit log. The value it is released is discarded unread.
/// </para>
/// <para>
/// <b>Cursor and VS Code can add keypaste themselves.</b> Their install link carries the entry Connect would write
/// (<see cref="McpInstallLink"/>), so it is offered beside the plan, and stays offered when keypaste cannot write the
/// tool's file, until Connect has connected the tool.
/// </para>
/// </remarks>
internal sealed partial class ConnectClientViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClientConnector _connector;
    private readonly Action? _ran;
    private readonly Action? _cancelled;
    private readonly ClipboardCountdown? _clipboard;
    private readonly IWebLauncher? _web;
    private readonly CancellationTokenSource _closing = new();

    private McpServerRegistration? _pendingRegistration;
    private string _preview = string.Empty;
    private string _previewNote = string.Empty;
    private McpConnectionCheck? _check;
    private bool _startingCheck;
    private IReadOnlyList<McpListedEntry> _checkEntries = [];
    private bool _disposed;

    /// <param name="session">The vault the tool is connected to.</param>
    /// <param name="connector">The machine, the tools' own commands, the bridge and the check.</param>
    /// <param name="client">The tool.</param>
    /// <param name="removes">Whether the pane disconnects the tool rather than connecting it.</param>
    /// <param name="ran">Called once Connect has run a plan, whatever came of it.</param>
    /// <param name="cancelled">Called once Cancel has dropped the plan.</param>
    /// <param name="clipboard">Where Copy link puts the tool's install link, or null to offer no copy.</param>
    /// <param name="web">What opens the tool's install link, or null to offer no opening.</param>
    internal ConnectClientViewModel(
        AppVaultSession session,
        ClientConnector connector,
        McpClient client,
        bool removes,
        Action? ran = null,
        Action? cancelled = null,
        ClipboardCountdown? clipboard = null,
        IWebLauncher? web = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(client);

        _session = session;
        _connector = connector;
        _ran = ran;
        _cancelled = cancelled;
        _clipboard = clipboard;
        _web = web;
        Client = client;
        Removes = removes;

        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => Pending is not null && !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => Pending is not null && !IsRunning);
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => Registered is not null && !IsChecking);
        AskCommand = new AsyncRelayCommand(AskAsync, () => _check is not null && SelectedCheckEntry is not null && _checkEntries.Count > 0);
        OpenInstallLinkCommand = new AsyncRelayCommand(OpenInstallLinkAsync, () => OffersInstallLink && _web is not null);
        CopyInstallLinkCommand = new AsyncRelayCommand(CopyInstallLinkAsync, () => OffersInstallLink && _clipboard is not null);
    }

    /// <summary>The tool this pane connects or disconnects.</summary>
    internal McpClient Client { get; }

    /// <summary>Whether this pane disconnects the tool.</summary>
    internal bool Removes { get; }

    /// <summary>What the pane is headed by.</summary>
    internal string Title => Removes ? $"Disconnect {Client.DisplayName}" : $"Connect {Client.DisplayName}";

    /// <summary>Exactly what Connect or Remove will run, as core composed it.</summary>
    internal string Preview => _preview;

    internal bool HasPreview => _preview.Length > 0;

    /// <summary>What the preview does and does not do.</summary>
    internal string PreviewNote => _previewNote;

    /// <summary>What the last action did, or why it could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>The names the check's bridge listed, when there is more than one to choose from.</summary>
    internal IReadOnlyList<McpListedEntry> CheckEntries => _checkEntries;

    internal bool IsPicking => _checkEntries.Count > 1 && _check is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    internal partial McpListedEntry? SelectedCheckEntry { get; set; }

    /// <summary>What the check found, never a value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheckMessage), nameof(OffersCheck))]
    internal partial string CheckMessage { get; private set; } = string.Empty;

    internal bool HasCheckMessage => CheckMessage.Length > 0;

    internal bool IsChecking => _check is not null;

    /// <summary>Whether the check is doing something itself: starting or waiting on the bridge, not on the person picking an item.</summary>
    internal bool IsCheckWorking => _startingCheck || (IsChecking && !IsPicking);

    /// <summary>Whether Connect is running the plan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    internal partial bool IsRunning { get; private set; }

    /// <summary>Whether a plan or a check is running, which no other pane may interrupt.</summary>
    internal bool IsBusy => IsRunning || IsChecking;

    /// <summary>Whether the check is offered: once a Connect has registered the tool, and while its words remain.</summary>
    internal bool OffersCheck => !Removes && (Registered is not null || IsChecking || HasCheckMessage);

    /// <summary>The registration Connect ran, which the check starts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersCheck), nameof(OffersInstallLink))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(OpenInstallLinkCommand), nameof(CopyInstallLinkCommand))]
    internal partial McpServerRegistration? Registered { get; private set; }

    /// <summary>Runs the plan on screen, and only that.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    internal RelayCommand CancelCommand { get; }

    internal AsyncRelayCommand CheckCommand { get; }

    /// <summary>Asks for the entry picked from the check's listing.</summary>
    internal AsyncRelayCommand AskCommand { get; }

    /// <summary>The link that has the tool add keypaste itself, holding the entry Connect would write; null for a tool that takes none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersInstallLink))]
    [NotifyCanExecuteChangedFor(nameof(OpenInstallLinkCommand), nameof(CopyInstallLinkCommand))]
    internal partial string? InstallLink { get; private set; }

    /// <summary>Whether the link is offered: until Connect has connected the tool.</summary>
    internal bool OffersInstallLink => InstallLink is not null && Registered is null;

    internal string OpenInstallLinkLabel => $"Open in {Client.DisplayName}";

    /// <summary>What the link does, beside the plan or after keypaste could not write the tool's file.</summary>
    internal string InstallLinkNote => $"Or let {Client.DisplayName} add keypaste itself: this link holds the same command.";

    /// <summary>Hands the link to the system, which opens it in the tool.</summary>
    internal AsyncRelayCommand OpenInstallLinkCommand { get; }

    internal AsyncRelayCommand CopyInstallLinkCommand { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand))]
    private partial McpSetupPlan? Pending { get; set; }

    /// <summary>Composes the plan the pane shows, which Connect runs; nothing is written.</summary>
    internal Task PreviewAsync() => Removes ? PreviewRemoveAsync() : PreviewConnectAsync();

    private async Task PreviewConnectAsync()
    {
        Forget();
        InstallLink = null;

        // A tool may start the bridge without KEYPASTE_HOME or KEYPASTE_APPROVER, and Gemini CLI strips both.
        if (_session.VaultPath is not { } vault
            || !McpServerLocator.TryPin(_connector.Machine, _session.Home, _connector.ApproverOverride, vault, ChosenVault.Read(_session.Home), out var pins))
        {
            Message = $"Unlock a vault before connecting {Client.DisplayName} to it.";
            return;
        }

        var found = _connector.FindServer();

        if (found.Target is not { } target)
        {
            Message = Sentence(found.Error ?? $"{McpServerLocator.FileName} was not found");
            return;
        }

        if (!McpServerRegistration.TryCreate(target.Server, pins, Client.Id, [], out var registration, out var error))
        {
            Message = Sentence(error);
            return;
        }

        var client = Client;
        var machine = _connector.Machine;
        var detection = await Task.Run(() => McpClientSetup.Detect(client, machine, _connector.Runner)).ConfigureAwait(true);

        if (!detection.Found)
        {
            Message = NotInstalled(client);
            return;
        }

        // Inside an AppImage the registration starts a link only Connect makes, so the tool's own link would start nothing.
        if (target.Link is null && McpInstallLink.TryCreate(client, machine.Platform, registration, out var installLink, out _))
        {
            InstallLink = installLink;
        }

        if (!McpClientSetup.TryConnect(client, detection, registration, target.Link, out var plan, out var refusal))
        {
            Message = Sentence(refusal);
            return;
        }

        Pending = plan;
        _pendingRegistration = registration;
        Show(plan, ConnectNote(client, target, plan));
    }

    private async Task PreviewRemoveAsync()
    {
        Forget();

        var client = Client;
        var machine = _connector.Machine;
        var detection = await Task.Run(() => McpClientSetup.Detect(client, machine, _connector.Runner)).ConfigureAwait(true);

        if (!detection.Found)
        {
            Message = NotInstalled(client);
            return;
        }

        if (detection.Problem is { } problem)
        {
            Message = Sentence(problem);
            return;
        }

        var reading = await Task.Run(() => McpClientConfig.Read(client, machine)).ConfigureAwait(true);
        var plan = McpClientSetup.Remove(client, detection, reading);

        if (plan.IsNotConnected)
        {
            Message = NotConnected(client);
            return;
        }

        Pending = plan;
        Show(plan, plan.Steps[0] is McpConfigEdit edit
            ? EditNote("Remove", edit)
            : $"Remove runs this command, which takes keypaste out of {client.DisplayName} and leaves its other servers alone.");
    }

    private async Task ConfirmAsync()
    {
        if (Pending is not { } plan)
        {
            return;
        }

        var registration = _pendingRegistration;
        IsRunning = true;
        McpSetupResult result;

        try
        {
            result = await Task.Run(() => McpClientSetup.Apply(plan, _connector.Runner)).ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
        }

        Forget();

        var removing = plan.Action == McpSetupAction.Remove;
        var client = plan.Client;
        var name = client.DisplayName;
        Message = result.Status switch
        {
            McpSetupStatus.Done when removing => $"Disconnected. {name} can no longer start keypaste mcp for this vault." + Afterwards(client, result, removing),
            McpSetupStatus.Done => $"Connected. {name} starts keypaste mcp for this vault; Check asks through it once." + Afterwards(client, result, removing),
            McpSetupStatus.NotConnected => NotConnected(client),
            McpSetupStatus.NotInstalled => $"{name} could not be started, so nothing was changed. Check that it is installed, then run this again.",
            McpSetupStatus.LinkFailed when plan.Steps.OfType<McpBridgeLink>().FirstOrDefault() is { } link =>
                $"keypaste could not {link.Verb} {link.Link.LinkPath}: {result.Said}. Nothing else was changed. Correct that, then run this again.",
            McpSetupStatus.Refused when client.Wiring == McpWiring.ConfigFile && result.Said is { } reason => Sentence(reason),
            _ => $"{name} refused" + (result.Said is { } said ? $": {said}" : "."),
        };

        // A refusal comes after the run made the link, so the tool's own link now starts this copy.
        if (result.Status == McpSetupStatus.Refused
            && InstallLink is null
            && registration is not null
            && plan.Steps.OfType<McpBridgeLink>().Any()
            && McpInstallLink.TryCreate(client, _connector.Machine.Platform, registration, out var installLink, out _))
        {
            InstallLink = installLink;
        }

        Registered = result.Status == McpSetupStatus.Done && !removing ? registration : null;
        _ran?.Invoke();
    }

    private void Cancel()
    {
        Forget();
        Message = "Cancelled. Nothing was changed.";
        _cancelled?.Invoke();
    }

    private async Task OpenInstallLinkAsync()
    {
        if (InstallLink is not { } link || _web is null)
        {
            return;
        }

#pragma warning disable RS0030 // The install link the person asked to open.
        if (!await _web.OpenAsync(new Uri(link)).ConfigureAwait(true))
#pragma warning restore RS0030
        {
            Message = $"Your system didn't open the link. Copy it and paste it into your browser, which hands it to {Client.DisplayName}.";
        }
    }

    private async Task CopyInstallLinkAsync()
    {
        if (InstallLink is { } link && _clipboard is not null)
        {
            await _clipboard.CopyPlainAsync(link, $"{Client.DisplayName} install link").ConfigureAwait(true);
        }
    }

    private async Task CheckAsync()
    {
        if (Registered is not { } registration)
        {
            return;
        }

        CheckMessage = $"Starting keypaste mcp as {Client.DisplayName} would…";
        SetStartingCheck(true);
        var check = await Task.Run(() => _connector.StartCheck(registration)).ConfigureAwait(true);
        SetStartingCheck(false);
        if (check is null)
        {
            CheckMessage = $"The check could not start {registration.Server.Path}. Connect the tool again, then check.";
            return;
        }

        if (_disposed)
        {
            await check.DisposeAsync().ConfigureAwait(true);
            return;
        }

        SetCheck(check);

        McpCheckListing listing;
        try
        {
            listing = await check.ListAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }

        if (listing.Problem is { } problem)
        {
            await EndCheckAsync($"keypaste mcp started but did not list: {problem.TrimEnd('.')}. Connect the tool again, then check.").ConfigureAwait(true);
            return;
        }

        switch (listing.Entries.Count)
        {
            case 0:
                await EndCheckAsync($"keypaste mcp reached this vault and lists nothing {Client.DisplayName} may see, so there is nothing to ask for.")
                    .ConfigureAwait(true);
                return;
            case 1:
                await RequestAsync(listing.Entries[0]).ConfigureAwait(true);
                return;
            default:
                _checkEntries = listing.Entries;
                OnPropertyChanged(nameof(CheckEntries));
                OnPropertyChanged(nameof(IsPicking));
                OnPropertyChanged(nameof(IsCheckWorking));
                AskCommand.NotifyCanExecuteChanged();
                SelectedCheckEntry = listing.Entries[0];
                CheckMessage = $"keypaste mcp lists {listing.Entries.Count} items. Pick the one to ask for.";
                return;
        }
    }

    private Task AskAsync() => SelectedCheckEntry is { } entry ? RequestAsync(entry) : Task.CompletedTask;

    private async Task RequestAsync(McpListedEntry entry)
    {
        if (_check is not { } check)
        {
            return;
        }

        _checkEntries = [];
        OnPropertyChanged(nameof(CheckEntries));
        OnPropertyChanged(nameof(IsPicking));
        OnPropertyChanged(nameof(IsCheckWorking));
        AskCommand.NotifyCanExecuteChanged();

        CheckMessage = $"Asking for {Shown(entry.Field)} of {entry.Name}. Answer in the prompt window.";

        McpCheckAnswer answer;
        try
        {
            answer = await check.RequestAsync(entry, _closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }

        await EndCheckAsync(answer.Outcome switch
        {
            McpCheckOutcome.Granted => $"Connected: you approved {entry.Name}, and {Shown(entry.Field)} was released to the check, which discarded it unread. "
                + "Its record is in this screen's History.",
            McpCheckOutcome.Denied => $"Connected: the request for {entry.Name} reached you and was refused. {answer.Said}",
            _ => $"The check failed: {answer.Said?.TrimEnd('.')}. Connect the tool again, then check.",
        }).ConfigureAwait(true);
    }

    private static string Shown(string field) => field == "password" ? "the password" : field;

    private void SetStartingCheck(bool starting)
    {
        _startingCheck = starting;
        OnPropertyChanged(nameof(IsCheckWorking));
    }

    private void SetCheck(McpConnectionCheck? check)
    {
        _check = check;
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsCheckWorking));
        OnPropertyChanged(nameof(IsPicking));
        OnPropertyChanged(nameof(OffersCheck));
        OnPropertyChanged(nameof(IsBusy));
        CheckCommand.NotifyCanExecuteChanged();
        AskCommand.NotifyCanExecuteChanged();
    }

    private async Task EndCheckAsync(string message)
    {
        var check = _check;
        SetCheck(null);
        CheckMessage = message;

        if (check is not null)
        {
            await check.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static string NotInstalled(McpClient client) => client.Executable is { } command
        ? $"{client.DisplayName} is not installed here: `{command}` could not be started. Install it, then connect it."
        : $"{client.DisplayName} is not installed here.";

    private static string NotConnected(McpClient client) => $"{client.DisplayName} is not connected; there is nothing to remove.";

    private static string ConnectNote(McpClient client, McpBridgeTarget target, McpSetupPlan plan)
    {
        List<string> notes =
        [
            plan.Steps.OfType<McpConfigEdit>().FirstOrDefault() is { } edit
                ? EditNote("Connect", edit) + " No password or key file goes into it."
                : $"Connect runs these commands. The first removes keypaste from {client.DisplayName} if it was there before; "
                    + "the second adds this vault. No password or key file goes into its configuration.",
        ];

        if (target.Link is { } link)
        {
            notes.Add(link.Change switch
            {
                McpLinkChange.Create => $"It first creates {link.Link.LinkPath}, which starts this AppImage.",
                McpLinkChange.Repoint => $"It first re-points {link.Link.LinkPath} from {link.Was} to this AppImage.",
                _ => $"It first replaces {link.Link.LinkPath}, which is {link.Was}, keeping it as {link.Link.BackupPath}.",
            });
        }

        if (client.StartsOnlyInTrustedFolders)
        {
            notes.Add(Trusted(client));
        }

        if (target.Note is { } note)
        {
            notes.Add(note + ".");
        }

        return string.Join(' ', notes);
    }

    private static string EditNote(string verb, McpConfigEdit edit) => File.Exists(edit.Path)
        ? $"{verb} changes only keypaste's part of {edit.Path}, and keeps the first original as {edit.Backup ?? McpConfigFile.BackupPath(edit.Path)}."
        : $"{verb} creates {edit.Path} holding only keypaste's part.";

    private static string Afterwards(McpClient client, McpSetupResult result, bool removing)
    {
        var notes = string.Empty;

        if (result.Backup is { } backup)
        {
            notes += $" The original is kept as {backup}.";
        }

        if (!removing && client.StartsOnlyInTrustedFolders)
        {
            notes += " " + Trusted(client);
        }

        if (client.ReadsAtLaunch)
        {
            notes += $" Restart {client.DisplayName} to load it.";
        }

        return notes;
    }

    private static string Trusted(McpClient client) => $"{client.DisplayName} starts keypaste only in folders you have trusted.";

    // keypaste is written in lower case even where it begins a sentence.
    private static string Sentence(string text)
    {
        var opened = text.StartsWith(McpServerLocator.FileName, StringComparison.Ordinal) ? text : char.ToUpperInvariant(text[0]) + text[1..];
        return opened.EndsWith('.') ? opened : opened + ".";
    }

    private void Show(McpSetupPlan plan, string note)
    {
        _preview = plan.Display;
        _previewNote = note;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewNote));
        OnPropertyChanged(nameof(HasPreview));
    }

    /// <summary>Drops a plan that was shown but not confirmed, so an edit can never run an old preview.</summary>
    private void Forget()
    {
        Pending = null;
        _pendingRegistration = null;
        _preview = string.Empty;
        _previewNote = string.Empty;
        Message = string.Empty;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewNote));
        OnPropertyChanged(nameof(HasPreview));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closing.Cancel();

        // A check waiting on the prompt ends with the screen: its bridge's input closes, the request
        // is withdrawn, and the audit log records that the client stopped waiting.
        if (_check is { } check)
        {
            _check = null;
            _ = check.DisposeAsync().AsTask();
        }
    }
}
