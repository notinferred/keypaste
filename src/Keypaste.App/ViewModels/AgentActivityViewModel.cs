using System.Globalization;
using Keypaste.App.Session;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Whether agents can reach this app's unlocked vault, read from the session authority that answers
/// them rather than from a pipe that happens to accept connections.
/// </summary>
/// <remarks>The full activity screen, with requests, grants and audit outcomes, is STEPS 4.3b.</remarks>
internal sealed class AgentActivityViewModel : ObservableObject
{
    private readonly AppAuthority? _authority;
    private string _status = string.Empty;

    internal AgentActivityViewModel(AppAuthority? authority)
    {
        _authority = authority;
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
        Status = _authority is null ? Describe(new AuthorityStatus.Locked()) : Describe(_authority.Status);
        return Task.CompletedTask;
    }

    /// <summary>The status as the screen says it.</summary>
    internal static string Describe(AuthorityStatus status) => status switch
    {
        AuthorityStatus.Serving serving =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"This app (process {serving.Owner.ProcessId}) holds this vault for agents, in session {serving.Session}. ")
            + "MCP clients set up for this vault reach it here and can list the entry names they are allowed to see. "
            + "Each credential request is asked about in a prompt window, and nothing is released unless you press Approve.",
        AuthorityStatus.NotServing notServing => $"Agents cannot reach this vault: {notServing.Reason}",
        AuthorityStatus.HeldBy held => held.Sentence,
        _ => "Agents cannot reach this vault right now.",
    };
}
