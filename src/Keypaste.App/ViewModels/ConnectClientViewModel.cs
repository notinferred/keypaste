using Keypaste.App.Session;
using Keypaste.Core.Clients;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Connects an MCP client to the vault this app has unlocked, checks the connection and removes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written before the person has seen it.</b> Preview composes the plan in core
/// (<see cref="McpClientSetup"/>) and shows its display; Connect runs that same plan object, so the
/// steps confirmed and the steps run cannot differ. A client with no command of its own has only
/// keypaste's entry in its file changed, with the first original kept beside it. Inside an AppImage the
/// link a client starts is made or replaced first, as a step of the same plan.
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

    private McpClient _client;
    private string _label;
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
        Clients = McpClientCatalog.For(connector.Machine.Platform);
        _client = Clients[0];
        _label = _client.Id;

        PreviewConnectCommand = new AsyncRelayCommand(PreviewConnectAsync, () => !IsChecking);
        PreviewRemoveCommand = new AsyncRelayCommand(PreviewRemoveAsync, () => !IsChecking);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => _pending is not null);
        CancelCommand = new RelayCommand(Cancel, () => _pending is not null);
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => _registered is not null && !IsChecking);
        AskCommand = new AsyncRelayCommand(AskAsync, () => _check is not null && _checkEntry is not null && _checkEntries.Count > 0);

        DependsOn(nameof(HasPreview), nameof(Preview));
        DependsOn(nameof(HasMessage), nameof(Message));
        DependsOn(nameof(IsPicking), nameof(CheckEntries), nameof(IsChecking));
        DependsOn(nameof(HasCheckMessage), nameof(CheckMessage));
        DependsOn(nameof(NeedsRegistration), nameof(Registered));
        DependsOn(PreviewConnectCommand, nameof(IsChecking));
        DependsOn(PreviewRemoveCommand, nameof(IsChecking));
        DependsOn(ConfirmCommand, nameof(Pending));
        DependsOn(CancelCommand, nameof(Pending));
        DependsOn(CheckCommand, nameof(Registered), nameof(IsChecking));
        DependsOn(AskCommand, nameof(IsChecking), nameof(SelectedCheckEntry), nameof(CheckEntries));
    }

    /// <summary>Every client keypaste knows how to connect on this machine.</summary>
    internal IReadOnlyList<McpClient> Clients { get; }

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

    /// <summary>Extra globs, comma-separated; empty leaves the bridge's default of <c>tag:env:*</c>.</summary>
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

    /// <summary>Exactly what Connect or Remove will run, as core composed it.</summary>
    internal string Preview => _preview;

    internal bool HasPreview => _preview.Length > 0;

    /// <summary>What the preview does and does not do.</summary>
    internal string PreviewNote => _previewNote;

    /// <summary>What the last action did, or why it could not.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>The names the check's bridge listed, when there is more than one to choose from.</summary>
    internal IReadOnlyList<McpListedEntry> CheckEntries => _checkEntries;

    internal bool IsPicking => _checkEntries.Count > 1 && _check is not null;

    internal McpListedEntry? SelectedCheckEntry
    {
        get => _checkEntry;
        set => Set(ref _checkEntry, value);
    }

    /// <summary>What the check found, never a value.</summary>
    internal string CheckMessage
    {
        get => _checkMessage;
        private set => Set(ref _checkMessage, value);
    }

    internal bool HasCheckMessage => _checkMessage.Length > 0;

    internal bool IsChecking => _check is not null;

    /// <summary>The registration Connect ran, which the check starts.</summary>
    internal McpServerRegistration? Registered
    {
        get => _registered;
        private set => Set(ref _registered, value);
    }

    /// <summary>Whether Check waits for a Connect to start from.</summary>
    internal bool NeedsRegistration => _registered is null;

    internal AsyncRelayCommand PreviewConnectCommand { get; }

    internal AsyncRelayCommand PreviewRemoveCommand { get; }

    /// <summary>Runs the plan on screen, and only that.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    internal RelayCommand CancelCommand { get; }

    internal AsyncRelayCommand CheckCommand { get; }

    /// <summary>Asks for the entry picked from the check's listing.</summary>
    internal AsyncRelayCommand AskCommand { get; }

    private McpSetupPlan? Pending
    {
        get => _pending;
        set => Set(ref _pending, value);
    }

    private async Task PreviewConnectAsync()
    {
        Forget();

        // A client may start the bridge without KEYPASTE_HOME or KEYPASTE_APPROVER, and Gemini CLI strips both (D-0389).
        if (_session.VaultPath is not { } vault
            || !McpServerLocator.TryPin(_connector.Machine, _session.Home, _connector.ApproverOverride, vault, ChosenVault.Read(_session.Home), out var pins))
        {
            Message = "Unlock a vault before connecting a client to it.";
            return;
        }

        var found = _connector.FindServer();

        if (found.Target is not { } target)
        {
            Message = Sentence(found.Error ?? $"{McpServerLocator.FileName} was not found");
            return;
        }

        var globs = _exposure.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!McpServerRegistration.TryCreate(target.Server, pins, _label, globs, out var registration, out var error))
        {
            Message = Sentence(error);
            return;
        }

        var client = _client;
        var machine = _connector.Machine;
        var detection = await Task.Run(() => McpClientSetup.Detect(client, machine, _connector.Runner)).ConfigureAwait(true);

        if (!detection.Found)
        {
            Message = NotInstalled(client);
            return;
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

        var client = _client;
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
        if (_pending is not { } plan)
        {
            return;
        }

        var registration = _pendingRegistration;
        var result = await Task.Run(() => McpClientSetup.Apply(plan, _connector.Runner)).ConfigureAwait(true);

        Forget();

        var removing = plan.Action == McpSetupAction.Remove;
        var client = plan.Client;
        var name = client.DisplayName;
        Message = result.Status switch
        {
            McpSetupStatus.Done when removing => $"Removed. {name} can no longer start keypaste mcp for this vault." + Afterwards(client, result, removing),
            McpSetupStatus.Done => $"Connected. {name} starts keypaste mcp for this vault; Check asks through it once." + Afterwards(client, result, removing),
            McpSetupStatus.NotConnected => NotConnected(client),
            McpSetupStatus.NotInstalled => $"{name} could not be started, so nothing was changed.",
            McpSetupStatus.LinkFailed when plan.Steps.OfType<McpBridgeLink>().FirstOrDefault() is { } link =>
                $"keypaste could not {link.Verb} {link.Link.LinkPath}: {result.Said}. Nothing else was changed.",
            McpSetupStatus.Refused when client.Wiring == McpWiring.ConfigFile && result.Said is { } reason => Sentence(reason),
            _ => $"{name} refused" + (result.Said is { } said ? $": {said}" : "."),
        };

        Registered = result.Status == McpSetupStatus.Done && !removing ? registration : null;
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

        CheckMessage = "Starting keypaste mcp as the client would…";
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
                + "The audit record is in this session's history below.",
            McpCheckOutcome.Denied => $"Connected: the request for {entry.Name} reached you and was refused. {answer.Said}",
            _ => $"The check failed: {answer.Said}",
        }).ConfigureAwait(true);
    }

    private static string Shown(string field) => field == "password" ? "the password" : field;

    private void SetCheck(McpConnectionCheck? check)
    {
        _check = check;
        Raise(nameof(IsChecking));
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
        ? $"{client.DisplayName} is not installed here: `{command}` could not be started."
        : $"{client.DisplayName} is not installed here.";

    private static string NotConnected(McpClient client) => $"{client.DisplayName} is not connected; there is nothing to remove.";

    private static string ConnectNote(McpClient client, McpBridgeTarget target, McpSetupPlan plan)
    {
        List<string> notes =
        [
            plan.Steps.OfType<McpConfigEdit>().FirstOrDefault() is { } edit
                ? EditNote("Connect", edit) + " No password, keyfile or session goes into it."
                : $"Connect runs these commands. The first removes any earlier keypaste entry from {client.DisplayName}; "
                    + "the second adds this vault. No password, keyfile or session goes into its configuration.",
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
        ? $"{verb} changes only keypaste's entry in {edit.Path}, and keeps the first original as {edit.Backup ?? McpConfigFile.BackupPath(edit.Path)}."
        : $"{verb} creates {edit.Path} holding only keypaste's entry.";

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
        Raise(nameof(Preview));
        Raise(nameof(PreviewNote));
    }

    /// <summary>Drops a plan that was shown but not confirmed, so an edit can never run an old preview.</summary>
    private void Forget()
    {
        Pending = null;
        _pendingRegistration = null;
        _preview = string.Empty;
        _previewNote = string.Empty;
        Message = string.Empty;
        Raise(nameof(Preview));
        Raise(nameof(PreviewNote));
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
