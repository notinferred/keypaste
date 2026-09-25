using Keypaste.App.Session;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Connects an MCP client to the vault this app has unlocked, checks the connection and removes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written before the person has seen it.</b> Preview composes the plan in core
/// (<see cref="McpClientSetup"/>) and shows its display; Connect runs that same plan object, so the
/// commands confirmed and the commands run cannot differ. A client with no command of its own is
/// shown the block to paste, and nothing is claimed for it.
/// </para>
/// <para>
/// <b>The check goes through the bridge a client would start.</b> It launches that registration's
/// command line and asks as a client would, so the request meets this app's session, its prompt
/// window and its audit log. The value it is released is discarded unread.
/// </para>
/// </remarks>
internal sealed class ConnectClientViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClientConnector _connector;
    private readonly CancellationTokenSource _closing = new();

    private McpClient _client = McpClientCatalog.All[0];
    private string _label = McpClientCatalog.All[0].Id;
    private string _exposure = string.Empty;
    private McpSetupPlan? _pending;
    private McpServerRegistration? _pendingRegistration;
    private McpServerRegistration? _registered;
    private string _preview = string.Empty;
    private string _previewNote = string.Empty;
    private string _message = string.Empty;
    private McpConnectionCheck? _check;
    private IReadOnlyList<McpListedEntry> _checkEntries = [];
    private McpListedEntry? _checkEntry;
    private string _checkMessage = string.Empty;
    private bool _disposed;

    internal ConnectClientViewModel(AppVaultSession session, ClientConnector connector)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(connector);

        _session = session;
        _connector = connector;

        PreviewConnectCommand = new AsyncRelayCommand(PreviewConnectAsync, () => !IsChecking);
        PreviewRemoveCommand = new AsyncRelayCommand(PreviewRemoveAsync, () => !IsChecking);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => _pending is { RunsCommands: true });
        CancelCommand = new RelayCommand(Cancel, () => _pending is not null);
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => _registered is not null && !IsChecking);
        AskCommand = new AsyncRelayCommand(AskAsync, () => _check is not null && _checkEntry is not null && _checkEntries.Count > 0);
    }

    /// <summary>Every client keypaste knows how to connect; an instance property because a binding needs one.</summary>
#pragma warning disable CA1822
    internal IReadOnlyList<McpClient> Clients => McpClientCatalog.All;
#pragma warning restore CA1822

    internal McpClient SelectedClient
    {
        get => _client;
        set
        {
            var previous = _client;
            if (value is null || !Set(ref _client, value))
            {
                return;
            }

            if (_label == previous.Id)
            {
                Label = value.Id;
            }

            Forget();
        }
    }

    /// <summary>What the audit log and the prompt will call this client.</summary>
    internal string Label
    {
        get => _label;
        set
        {
            if (Set(ref _label, value ?? string.Empty))
            {
                Forget();
            }
        }
    }

    /// <summary>Extra globs, comma-separated; empty leaves the bridge's default of <c>env/**</c>.</summary>
    internal string Exposure
    {
        get => _exposure;
        set
        {
            if (Set(ref _exposure, value ?? string.Empty))
            {
                Forget();
            }
        }
    }

    /// <summary>Exactly what Connect or Remove will run, or the block to paste, as core composed it.</summary>
    internal string Preview => _preview;

    internal bool HasPreview => _preview.Length > 0;

    /// <summary>What the preview does and does not do.</summary>
    internal string PreviewNote => _previewNote;

    /// <summary>What the last action did, or why it could not.</summary>
    internal string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>The names the check's bridge listed, when there is more than one to choose from.</summary>
    internal IReadOnlyList<McpListedEntry> CheckEntries => _checkEntries;

    internal bool IsPicking => _checkEntries.Count > 1 && _check is not null;

    internal McpListedEntry? SelectedCheckEntry
    {
        get => _checkEntry;
        set
        {
            if (Set(ref _checkEntry, value))
            {
                AskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>What the check found, never a value.</summary>
    internal string CheckMessage
    {
        get => _checkMessage;
        private set
        {
            if (Set(ref _checkMessage, value))
            {
                Raise(nameof(HasCheckMessage));
            }
        }
    }

    internal bool HasCheckMessage => _checkMessage.Length > 0;

    internal bool IsChecking => _check is not null;

    /// <summary>The registration Connect ran, or whose block was shown, which the check starts.</summary>
    internal McpServerRegistration? Registered
    {
        get => _registered;
        private set
        {
            if (Set(ref _registered, value))
            {
                Raise(nameof(NeedsRegistration));
            }
        }
    }

    /// <summary>Whether Check waits for a Connect or a shown block to start from.</summary>
    internal bool NeedsRegistration => _registered is null;

    internal AsyncRelayCommand PreviewConnectCommand { get; }

    internal AsyncRelayCommand PreviewRemoveCommand { get; }

    /// <summary>Runs the plan on screen, and only that.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    internal RelayCommand CancelCommand { get; }

    internal AsyncRelayCommand CheckCommand { get; }

    /// <summary>Asks for the entry picked from the check's listing.</summary>
    internal AsyncRelayCommand AskCommand { get; }

    private async Task PreviewConnectAsync()
    {
        Forget();

        if (_session.VaultPath is not { } vault)
        {
            Message = "Unlock a vault before connecting a client to it.";
            return;
        }

        if (_connector.FindServer() is not { } server)
        {
            Message = $"{McpServerLocator.FileName} was not found {_connector.Places}, so there is nothing a client could start.";
            return;
        }

        var globs = _exposure.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!McpServerRegistration.TryCreate(server, vault, _label, globs, out var registration, out var error))
        {
            Message = char.ToUpperInvariant(error[0]) + error[1..] + ".";
            return;
        }

        var plan = McpClientSetup.Connect(_client, registration);

        if (!plan.RunsCommands)
        {
            // Nothing will be written, so this block is already what the client would start.
            Registered = registration;
            Show(plan, $"{_client.DisplayName} has no command of its own. Add this to its configuration file yourself; "
                + "keypaste wrote nothing. docs/mcp-setup.md says which file.");
            return;
        }

        if (!await Task.Run(() => McpClientSetup.IsInstalled(_client, _connector.Runner)).ConfigureAwait(true))
        {
            Message = $"{_client.DisplayName} is not installed here: `{_client.Executable}` could not be started.";
            return;
        }

        _pending = plan;
        _pendingRegistration = registration;
        Show(plan, $"Connect runs these commands. The first removes any earlier keypaste entry from {_client.DisplayName}; "
            + "the second adds this vault. No password, keyfile or session goes into its configuration.");
    }

    private async Task PreviewRemoveAsync()
    {
        Forget();

        var plan = McpClientSetup.Remove(_client);

        if (!plan.RunsCommands)
        {
            Message = $"{_client.DisplayName} has no command of its own. Delete the \"keypaste\" entry from its configuration file yourself; keypaste changed nothing.";
            return;
        }

        if (!await Task.Run(() => McpClientSetup.IsInstalled(_client, _connector.Runner)).ConfigureAwait(true))
        {
            Message = $"{_client.DisplayName} is not installed here: `{_client.Executable}` could not be started.";
            return;
        }

        _pending = plan;
        Show(plan, $"Remove runs this command, which takes keypaste out of {_client.DisplayName} and leaves its other servers alone.");
    }

    private async Task ConfirmAsync()
    {
        if (_pending is not { } plan)
        {
            return;
        }

        var registration = _pendingRegistration;
        var result = await Task.Run(() => McpClientSetup.Apply(plan, _connector.Runner)).ConfigureAwait(true);

        Forget();

        var removing = plan.Action == McpSetupAction.Remove;
        Message = result.Status switch
        {
            McpSetupStatus.Done when removing => $"Removed. {plan.Client.DisplayName} can no longer start keypaste-mcp for this vault.",
            McpSetupStatus.Done => $"Connected. {plan.Client.DisplayName} starts keypaste-mcp for this vault; Check asks through it once.",
            McpSetupStatus.NotInstalled => $"{plan.Client.DisplayName} could not be started, so nothing was changed.",
            _ => $"{plan.Client.DisplayName} refused" + (result.ClientSaid is { } said ? $": {said}" : "."),
        };

        Registered = result.Status == McpSetupStatus.Done && !removing ? registration : null;
        CheckCommand.RaiseCanExecuteChanged();
    }

    private void Cancel()
    {
        Forget();
        Message = "Cancelled. Nothing was changed.";
    }

    private async Task CheckAsync()
    {
        if (_registered is not { } registration)
        {
            return;
        }

        CheckMessage = "Starting keypaste-mcp as the client would…";
        var check = await Task.Run(() => _connector.StartCheck(registration)).ConfigureAwait(true);
        if (check is null)
        {
            CheckMessage = $"The check could not start {registration.Server.Path}.";
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
            await EndCheckAsync($"The bridge started but did not list: {problem}").ConfigureAwait(true);
            return;
        }

        switch (listing.Entries.Count)
        {
            case 0:
                await EndCheckAsync("The bridge reached this vault and lists nothing this client may name, so there is nothing to ask for.")
                    .ConfigureAwait(true);
                return;
            case 1:
                await RequestAsync(listing.Entries[0]).ConfigureAwait(true);
                return;
            default:
                _checkEntries = listing.Entries;
                Raise(nameof(CheckEntries));
                Raise(nameof(IsPicking));
                SelectedCheckEntry = listing.Entries[0];
                CheckMessage = $"The bridge lists {listing.Entries.Count} entries. Pick the one to ask for.";
                return;
        }
    }

    private Task AskAsync() => _checkEntry is { } entry ? RequestAsync(entry) : Task.CompletedTask;

    private async Task RequestAsync(McpListedEntry entry)
    {
        if (_check is not { } check)
        {
            return;
        }

        _checkEntries = [];
        Raise(nameof(CheckEntries));
        Raise(nameof(IsPicking));
        AskCommand.RaiseCanExecuteChanged();

        CheckMessage = $"Asking for the password of {entry.Name}. Answer in the prompt window.";

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
            McpCheckOutcome.Granted => $"Connected: you approved {entry.Name}, and the password was released to the check, which discarded it unread. "
                + "The audit record is in this session's history below.",
            McpCheckOutcome.Denied => $"Connected: the request for {entry.Name} reached you and was refused. {answer.Said}",
            _ => $"The check failed: {answer.Said}",
        }).ConfigureAwait(true);
    }

    private void SetCheck(McpConnectionCheck? check)
    {
        _check = check;
        Raise(nameof(IsChecking));
        Raise(nameof(IsPicking));
        CheckCommand.RaiseCanExecuteChanged();
        PreviewConnectCommand.RaiseCanExecuteChanged();
        PreviewRemoveCommand.RaiseCanExecuteChanged();
        AskCommand.RaiseCanExecuteChanged();
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

    private void Show(McpSetupPlan plan, string note)
    {
        _preview = plan.Display;
        _previewNote = note;
        Raise(nameof(Preview));
        Raise(nameof(HasPreview));
        Raise(nameof(PreviewNote));
        ConfirmCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Drops a plan that was shown but not confirmed, so an edit can never run an old preview.</summary>
    private void Forget()
    {
        _pending = null;
        _pendingRegistration = null;
        _preview = string.Empty;
        _previewNote = string.Empty;
        Message = string.Empty;
        Raise(nameof(Preview));
        Raise(nameof(HasPreview));
        Raise(nameof(PreviewNote));
        ConfirmCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
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
