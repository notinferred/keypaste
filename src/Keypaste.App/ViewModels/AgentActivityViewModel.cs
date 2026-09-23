using Keypaste.App.Session;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Whether agents can reach this app's unlocked vault, read from what this process serves rather
/// than from a pipe that happens to accept connections.
/// </summary>
/// <remarks>The full activity screen, with requests, grants and audit outcomes, is STEPS 4.3b.</remarks>
internal sealed class AgentActivityViewModel : ObservableObject
{
    private readonly SessionHost? _host;
    private string _status = string.Empty;

    internal AgentActivityViewModel(SessionHost? host)
    {
        _host = host;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <summary>One true sentence about what agents can do with this vault.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Reads the state again.</summary>
    internal AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Says what agents can do with this vault right now.</summary>
    internal Task RefreshAsync()
    {
        Status = _host switch
        {
            { Endpoint: not null } =>
                "MCP clients set up for this vault reach this app. They can list the entry names they are allowed "
                + "to see. Approving credential requests here is not available yet, so every credential request is "
                + "refused; to approve them, lock this vault here and run keypaste agent.",
            { Failure: { } failure } => $"Agents cannot reach this vault: {failure}",
            _ => "Agents cannot reach this vault right now.",
        };

        return Task.CompletedTask;
    }
}
