using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>
/// What Stage 4.1 can honestly say about agent activity, which is one sentence.
/// </summary>
/// <remarks>
/// <para>
/// <b>The app is not the approver, and 4.1 must not pretend otherwise.</b>
/// <c>keypaste agent</c> binds the approver pipe at startup and a name already held is a startup
/// failure — so if this app also bound it, whichever of the two started second would fail, and the
/// loser would be a <em>silent</em> loss of the approval path. Stage 4.3 owns that hand-off and has
/// to design it properly.
/// </para>
/// <para>
/// So this screen probes and reports. It connects, observes that something is listening, and
/// disconnects. It never sends a request, never changes the protocol, and never binds. That is
/// enough to answer the only question a person has while looking at an empty feed — "is anything
/// able to act as me right now?" — and it is the seed of the screen 4.3 builds.
/// </para>
/// </remarks>
internal sealed class AgentActivityViewModel : ObservableObject
{
    private readonly string? _approverFromEnvironment;
    private string _status = string.Empty;

    internal AgentActivityViewModel(string? approverFromEnvironment)
    {
        _approverFromEnvironment = approverFromEnvironment;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <summary>One true sentence about whether an approver is listening.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Re-probes.</summary>
    internal AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Asks whether an approver is listening, and says so either way.
    /// </summary>
    /// <remarks>
    /// The wording changes completely between the two states because the consequence does. "No
    /// agent is running" is not a warning — it is the safe state, and the sentence says what it
    /// means for an agent rather than leaving somebody to infer it.
    /// </remarks>
    internal async Task RefreshAsync()
    {
        string pipe;

        try
        {
            pipe = ApproverEndpoint.Resolve(null, _approverFromEnvironment);
        }
        catch (ArgumentException)
        {
            Status = $"The {ApproverEndpoint.EnvironmentVariable} setting names something that cannot be a pipe.";
            return;
        }

        await using var client = await ApproverClient
            .TryConnectAsync(pipe, TimeSpan.FromMilliseconds(500), CancellationToken.None)
            .ConfigureAwait(true);

        Status = client is null
            ? "No keypaste agent is running. Agents cannot get credentials right now."
            : "A keypaste agent is running. Approvals appear in that terminal.";
    }
}
