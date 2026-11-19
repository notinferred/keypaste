using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Clients;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One AI tool in the Connect sheet: its server entry to copy, and Add it for me, which connects the tool to the vaults the
/// person chooses, the one in view by default; or Disconnect. Once connected, it checks that the tool can reach keypaste.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written before the person has seen what it changes.</b> The plan is composed in core
/// (<see cref="McpClientSetup"/>) when the tool is opened, and its note and tooltip say which file or command it changes;
/// Add it for me runs that same plan object, so the steps described and the steps run cannot differ. A tool with no command
/// of its own has only keypaste's entry in its file changed, with the first original kept beside it. Inside an AppImage the
/// link a tool starts is made or replaced first, as a step of the same plan.
/// </para>
/// <para>
/// <b>The tool is registered under its own id</b>, offering run and every item, because the owner holds
/// each agent to its settings, which follow that name.
/// </para>
/// <para>
/// <b>The check goes through the bridge the tool would start</b> and lists what the tool may see, as the tool would,
/// so it meets this app's session and its audit log. It asks for no value.
/// </para>
/// <para>
/// <b>Cursor and VS Code can add keypaste themselves.</b> Their install link carries the entry Add it for me would write
/// (<see cref="McpInstallLink"/>), so Open in the tool is offered beside it, until the tool is connected.
/// </para>
/// </remarks>
internal sealed partial class ConnectClientViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClientConnector _connector;
    private readonly Action? _ran;
    private readonly ClipboardCountdown? _clipboard;
    private readonly IWebLauncher? _web;
    private readonly CancellationTokenSource _closing = new();

    private McpServerRegistration? _pendingRegistration;
    private McpConnectionCheck? _check;
    private bool _disposed;

    /// <param name="session">The vault the tool is connected to.</param>
    /// <param name="connector">The machine, the tools' own commands, the bridge and the check.</param>
    /// <param name="client">The tool.</param>
    /// <param name="removes">Whether it disconnects the tool rather than connecting it.</param>
    /// <param name="ran">Called once a plan has run, whatever came of it.</param>
    /// <param name="clipboard">Where Copy puts the tool's server entry, or null to offer no copy.</param>
    /// <param name="web">What opens the tool's install link, or null to offer no opening.</param>
    /// <param name="accountVaults">The vaults of the local account the tool may also be allowed, or null for the one in view alone.</param>
    internal ConnectClientViewModel(
        AppVaultSession session,
        ClientConnector connector,
        McpClient client,
        bool removes,
        Action? ran = null,
        ClipboardCountdown? clipboard = null,
        IWebLauncher? web = null,
        IReadOnlyList<string>? accountVaults = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(client);

        _session = session;
        _connector = connector;
        _ran = ran;
        _clipboard = clipboard;
        _web = web;
        Client = client;
        Removes = removes;
        Vaults = Choices(session.VaultPath, accountVaults ?? []);
        ConfigText = ConnectSheetViewModel.ServerEntry(client.Id);

        foreach (var vault in Vaults)
        {
            vault.PropertyChanged += (_, _) => Choosing = PreviewAsync();
        }

        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => Pending is not null && !IsRunning);
        OpenInstallLinkCommand = new AsyncRelayCommand(OpenInstallLinkAsync, () => OffersInstallLink && _web is not null);
        CopyConfigCommand = new AsyncRelayCommand(CopyConfigAsync, () => _clipboard is not null);
    }

    /// <summary>The tool it connects or disconnects.</summary>
    internal McpClient Client { get; }

    /// <summary>Whether it disconnects the tool.</summary>
    internal bool Removes { get; }

    /// <summary>What the sheet is headed by.</summary>
    internal string Title => Removes ? $"Disconnect {Client.DisplayName}" : $"Connect {Client.DisplayName}";

    /// <summary>The server entry for the tool's MCP configuration, under the tool's own id, for setting it up by hand.</summary>
    internal string ConfigText { get; }

    /// <summary>Whether the entry to copy is shown: connecting, not disconnecting.</summary>
    internal bool ShowsConfig => !Removes;

    /// <summary>The vaults the tool may be allowed, the one in view first and chosen; any others the local account holds follow, unchosen.</summary>
    internal IReadOnlyList<ConnectVault> Vaults { get; }

    /// <summary>Whether a choice of vaults is offered: connecting, with more than one to choose from.</summary>
    internal bool ChoosesVaults => !Removes && Vaults.Count > 1;

    /// <summary>The plan composed again after the person last changed which vaults are chosen.</summary>
    internal Task Choosing { get; private set; } = Task.CompletedTask;

    /// <summary>Where the tool's file is, as one muted line under the entry; empty for a tool set up by its own command.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigPlace))]
    internal partial string ConfigPlace { get; private set; } = string.Empty;

    internal bool HasConfigPlace => ConfigPlace.Length > 0;

    /// <summary>What the action changes, in one short line.</summary>
    [ObservableProperty]
    internal partial string ActionNote { get; private set; } = string.Empty;

    /// <summary>Exactly what the action runs or writes, as core composed it.</summary>
    [ObservableProperty]
    internal partial string Preview { get; private set; } = string.Empty;

    /// <summary>The plan and what else it does, with full paths, as the note's tooltip.</summary>
    [ObservableProperty]
    internal partial string ActionTip { get; private set; } = string.Empty;

    /// <summary>The action's words: Add it for me, or Disconnect.</summary>
    internal string ActionLabel => Removes ? Resources.Strings.Disconnect : Resources.Strings.AddItForMe;

    /// <summary>Whether a plan is ready to run.</summary>
    internal bool HasPlan => Pending is not null;

    /// <summary>What the last action did, or why it could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>Whether the tool can reach keypaste, as the check found it, never a value; empty before it runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheckMessage))]
    internal partial string CheckMessage { get; private set; } = string.Empty;

    internal bool HasCheckMessage => CheckMessage.Length > 0;

    /// <summary>Whether the check found the tool can reach keypaste.</summary>
    [ObservableProperty]
    internal partial bool Reaches { get; private set; }

    internal bool IsChecking => _check is not null;

    /// <summary>Whether the action is running the plan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    internal partial bool IsRunning { get; private set; }

    /// <summary>Whether a plan or a check is running, which Done may not interrupt.</summary>
    internal bool IsBusy => IsRunning || IsChecking;

    /// <summary>The registration the action ran, which the check starts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersInstallLink))]
    [NotifyCanExecuteChangedFor(nameof(OpenInstallLinkCommand))]
    internal partial McpServerRegistration? Registered { get; private set; }

    /// <summary>Runs the plan described, and only that.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Copies the server entry.</summary>
    internal AsyncRelayCommand CopyConfigCommand { get; }

    /// <summary>The check started after the tool was connected, or a completed task.</summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>The link that has the tool add keypaste itself, holding the entry the action would write; null for a tool that takes none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersInstallLink))]
    [NotifyCanExecuteChangedFor(nameof(OpenInstallLinkCommand))]
    internal partial string? InstallLink { get; private set; }

    /// <summary>Whether Open in the tool is offered: until the tool is connected.</summary>
    internal bool OffersInstallLink => InstallLink is not null && Registered is null;

    internal string OpenInstallLinkLabel => $"Open in {Client.DisplayName}";

    /// <summary>Hands the link to the system, which opens it in the tool.</summary>
    internal AsyncRelayCommand OpenInstallLinkCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private partial McpSetupPlan? Pending { get; set; }

    /// <summary>Composes the plan the action runs; nothing is written.</summary>
    internal Task PreviewAsync() => Removes ? PreviewRemoveAsync() : PreviewConnectAsync();

    private async Task PreviewConnectAsync()
    {
        Forget();
        InstallLink = null;

        // A tool may start the bridge without KEYPASTE_HOME or KEYPASTE_APPROVER, and Gemini CLI strips both. The vaults
        // chosen are pinned even when one is the chosen vault, so the tool keeps reaching those alone whatever is opened or
        // chosen later.
        List<string> chosen = [.. Vaults.Where(vault => vault.IsChosen).Select(vault => vault.Path)];

        if (_session.VaultPath is null)
        {
            Message = $"Unlock a vault before connecting {Client.DisplayName} to it.";
            return;
        }

        if (chosen.Count == 0)
        {
            Message = $"Choose a vault {Client.DisplayName} may reach.";
            return;
        }

        if (!McpServerLocator.TryPin(_connector.Machine, _session.Home, _connector.ApproverOverride, chosen, chosenVault: null, out var pins))
        {
            Message = chosen.Count > 1
                ? $"{Client.DisplayName} can reach several vaults only while keypaste uses its usual home and pipes. Choose one vault."
                : $"Unlock a vault before connecting {Client.DisplayName} to it.";
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

        // Inside an AppImage the registration starts a link only the action makes, so the tool's own link would start nothing.
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
        Describe(plan, ConnectNotes(client, target));
    }

    /// <summary>The vault in view, chosen, then each other vault the account holds, unchosen.</summary>
    private static List<ConnectVault> Choices(string? inView, IReadOnlyList<string> accountVaults)
    {
        List<ConnectVault> choices = inView is null ? [] : [new ConnectVault(inView, chosen: true)];

        foreach (var vault in accountVaults.Where(vault => !choices.Any(choice => ChosenVault.Same(choice.Path, vault))))
        {
            choices.Add(new ConnectVault(vault, chosen: false));
        }

        return choices;
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
        Describe(plan, []);
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
            McpSetupStatus.Done when removing => $"Disconnected. {name} can no longer start keypaste for this vault." + Afterwards(client, removing),
            McpSetupStatus.Done => $"Connected {name} to {(registration?.VaultPaths.Count > 1 ? "these vaults" : "this vault")}." + Afterwards(client, removing),
            McpSetupStatus.NotConnected => NotConnected(client),
            McpSetupStatus.NotInstalled => $"{name} could not be started, so nothing was changed. Check that it is installed, then try again.",
            McpSetupStatus.LinkFailed when plan.Steps.OfType<McpBridgeLink>().FirstOrDefault() is { } link =>
                $"keypaste could not {link.Verb} {link.Link.LinkPath}: {result.Said}. Nothing else was changed. Correct that, then try again.",
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

        if (Registered is { } connected)
        {
            Checking = CheckAsync(connected);
            await Checking.ConfigureAwait(true);
        }
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
            Message = $"Your system didn't open {Client.DisplayName}. Copy the entry above into its MCP configuration instead.";
        }
    }

    private async Task CopyConfigAsync()
    {
        if (_clipboard is not null)
        {
            await _clipboard.CopyPlainAsync(ConfigText, Resources.Strings.ConfigurationCopied).ConfigureAwait(true);
        }
    }

    /// <summary>Lists through the bridge the tool would start, and says in one line whether the tool can reach keypaste.</summary>
    private async Task CheckAsync(McpServerRegistration registration)
    {
        var check = await Task.Run(() => _connector.StartCheck(registration)).ConfigureAwait(true);

        if (check is null)
        {
            Said(reaches: false, $"{Client.DisplayName} can't start keypaste from {registration.Server.Path}. Add it again, then restart {Client.DisplayName}.");
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
        finally
        {
            SetCheck(null);
            await check.DisposeAsync().ConfigureAwait(true);
        }

        Said(
            listing.Problem is null,
            listing.Problem is { } problem
                ? $"{Client.DisplayName} can't reach keypaste: {problem.TrimEnd('.')}. Add it again, then restart {Client.DisplayName}."
                : $"{Client.DisplayName} can reach keypaste.");
    }

    private void Said(bool reaches, string message)
    {
        Reaches = reaches;
        CheckMessage = message;
    }

    private void SetCheck(McpConnectionCheck? check)
    {
        _check = check;
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsBusy));
    }

    private static string NotInstalled(McpClient client) => client.Executable is { } command
        ? $"{client.DisplayName} is not installed here: `{command}` could not be started. Install it, then connect it."
        : $"{client.DisplayName} is not installed here.";

    private static string NotConnected(McpClient client) => $"{client.DisplayName} is not connected; there is nothing to remove.";

    /// <summary>What a connect plan does besides its edit or commands: a link it makes first, and where the tool starts keypaste.</summary>
    private static List<string> ConnectNotes(McpClient client, McpBridgeTarget target)
    {
        List<string> notes = [];

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

        return notes;
    }

    /// <summary>Says in one line what the plan changes, with its whole display and every full path in the tooltip.</summary>
    private void Describe(McpSetupPlan plan, List<string> notes)
    {
        var verb = Removes ? Resources.Strings.Disconnect : Resources.Strings.AddItForMe;

        if (plan.Steps.OfType<McpConfigEdit>().FirstOrDefault() is { } edit)
        {
            var exists = File.Exists(edit.Path);
            ConfigPlace = Removes ? string.Empty : UseText.GoesIn(Shortened(edit.Path));
            ActionNote = exists
                ? $"{verb} edits {Shortened(edit.Path)} and keeps a backup."
                : $"{verb} creates {Shortened(edit.Path)}.";
            notes.Insert(0, exists
                ? $"Changes only keypaste's part of {edit.Path}, and keeps the first original as {edit.Backup ?? McpConfigFile.BackupPath(edit.Path)}."
                : $"Creates {edit.Path} holding only keypaste's part.");
        }
        else
        {
            ConfigPlace = string.Empty;
            ActionNote = $"{verb} runs {Client.DisplayName}'s own command.";
        }

        Preview = plan.Display;
        ActionTip = string.Join('\n', [plan.Display, .. notes]);
    }

    private string Shortened(string path) =>
        _connector.Machine.Home is { Length: > 0 } home && path.StartsWith(home + Path.DirectorySeparatorChar, PathIdentity.Comparison)
            ? "~" + path[home.Length..]
            : path;

    private static string Afterwards(McpClient client, bool removing)
    {
        var notes = string.Empty;

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

    /// <summary>Drops a plan that was described but not run, so an edit can never run an old one.</summary>
    private void Forget()
    {
        Pending = null;
        _pendingRegistration = null;
        ConfigPlace = string.Empty;
        ActionNote = string.Empty;
        Preview = string.Empty;
        ActionTip = string.Empty;
        Message = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closing.Cancel();

        // A check still listing ends with the sheet: its bridge's input closes.
        if (_check is { } check)
        {
            _check = null;
            _ = check.DisposeAsync().AsTask();
        }
    }
}

/// <summary>A vault Connect may allow the tool, and whether the person chose it.</summary>
/// <param name="path">The vault's path.</param>
/// <param name="chosen">Whether it starts chosen.</param>
internal sealed partial class ConnectVault(string path, bool chosen) : ObservableObject
{
    /// <summary>The vault's path, which the registration pins.</summary>
    internal string Path { get; } = path;

    /// <summary>The vault's name, as prompts and the bridge's results give it.</summary>
    internal string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>Whether the tool may reach it.</summary>
    [ObservableProperty]
    internal partial bool IsChosen { get; set; } = chosen;
}
