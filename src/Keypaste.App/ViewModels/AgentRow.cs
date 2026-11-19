using Keypaste.Core.Agents;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>One live session of an agent, as its detail lists it.</summary>
/// <param name="Folder">The folder it says it runs in, or a dash; display only (T-3).</param>
/// <param name="Status">When it last asked.</param>
internal sealed record AgentSession(string Folder, string Status);

/// <summary>A heading in Agents' list, or the look in progress as the list's quiet last row; neither can be chosen.</summary>
internal sealed class AgentListHeading
{
    private readonly Func<string> _title;

    private AgentListHeading(Func<string> title, bool spins)
    {
        _title = title;
        Spins = spins;
    }

    internal static AgentListHeading Connected { get; } = new(() => Resources.Strings.Connected, spins: false);

    internal static AgentListHeading Found { get; } = new(() => Resources.Strings.FoundOnThisComputer, spins: false);

    internal static AgentListHeading Looking { get; } = new(() => Resources.Strings.LookingForAIToolsOnThisComputer, spins: true);

    internal string Title => _title();

    /// <summary>Whether it is the look in progress, drawn with a turning mark.</summary>
    internal bool Spins { get; }

    internal bool IsHeading => !Spins;

    public override string ToString() => Title;
}

/// <summary>
/// One agent or AI tool on the Agents screen: who it is, whether its tool holds keypaste, whether it is
/// in use, its sessions, its settings when open, and whether its tool's entry needs repair. Never a value.
/// </summary>
/// <remarks>
/// Names come from what agents say about themselves and from the names their connections were given:
/// display only (THREATS.md T-3). Settings follow the name, so an agent with none is held to the defaults
/// and has no settings of its own (T-36). A row is kept by its key and updated in place, so an open
/// disclosure and its unsaved edits survive each refresh.
/// </remarks>
internal sealed partial class AgentRow : ObservableObject, IDisposable
{
    private readonly Func<AgentRow, AgentSettingsViewModel?> _openSettings;
    private McpClientCard _card;
    private DateTimeOffset _now;
    private bool _isOpen;
    private AgentSettingsViewModel? _settings;

    /// <param name="card">What Core built for this key.</param>
    /// <param name="now">What "last asked" is measured from.</param>
    /// <param name="openSettings">Opens the settings of the row given, or null when nothing answers agents.</param>
    internal AgentRow(McpClientCard card, DateTimeOffset now, Func<AgentRow, AgentSettingsViewModel?> openSettings)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(openSettings);

        _card = card;
        _now = now;
        _openSettings = openSettings;
        Key = card.Key;
        HasSettings = AgentNames.IsNamed(card.Label);
        Label = HasSettings ? card.Label : null;
        Title = card.Tool?.DisplayName ?? McpClientCatalog.Find(card.Name)?.DisplayName ?? card.Label ?? card.Name;
        Initials = InitialsOf(Title);
        Named = Label is { } label && !string.Equals(label, card.Tool?.Id ?? McpClientCatalog.Find(card.Name)?.Id, StringComparison.Ordinal) && !string.Equals(label, Title, StringComparison.Ordinal)
            ? label
            : string.Empty;

        Update(card, now);
    }

    /// <summary>Its label, else <c>name:</c> and the name it gave, or a tool's id.</summary>
    internal string Key { get; }

    /// <summary>The name its settings are kept under, or null for an agent with no name.</summary>
    internal string? Label { get; }

    /// <summary>The tool's name, else the agent's, as the row and its sentences call it.</summary>
    internal string Title { get; }

    /// <summary>Its label, where it differs from the tool's own, as a muted second part of the title; else empty.</summary>
    internal string Named { get; }

    internal bool HasNamed => Named.Length > 0;

    internal string Initials { get; }

    /// <summary>The name an agent gave, from which Give it a name… suggests a label.</summary>
    internal string GivenName => _card.Name;

    /// <summary>What it is now: whether its tool holds keypaste, how many sessions, whether it is in use, or when it last asked.</summary>
    [ObservableProperty]
    internal partial string Status { get; private set; } = string.Empty;

    /// <summary>Why keypaste can't read or connect its tool, for the status's tooltip; else null.</summary>
    [ObservableProperty]
    internal partial string? StatusDetail { get; private set; }

    /// <summary>Whether its status dot says all is well: its tool holds keypaste, or one of its bridges is attached now.</summary>
    [ObservableProperty]
    internal partial bool IsLive { get; private set; }

    /// <summary>The catalogue's tool this row is, or null for an agent of no tool.</summary>
    internal McpClient? Tool => _card.Tool;

    /// <summary>Whether a tool found on this computer holds keypaste; null for a row that is no found tool.</summary>
    internal McpToolPresence? Presence => _card.Presence;

    /// <summary>What the tool's own configuration starts for keypaste, which Repair compares with what Connect writes.</summary>
    internal McpEntryLaunch? Launch => _card.Launch;

    /// <summary>When it last asked, which orders the connected section.</summary>
    internal DateTimeOffset? LastSeen => _card.LastSeen;

    /// <summary>Whether it is listed under Connected: it asked, is attached, or its tool holds keypaste; else it is a tool merely found.</summary>
    internal bool IsConnected => Presence is null || Presence.State == McpToolState.Connected || _card.Connections > 0;

    /// <summary>Whether it can have settings of its own: a name they can be kept under.</summary>
    internal bool HasSettings { get; }

    /// <summary>Whether the detail offers Give it a name…: an agent with no name of its own.</summary>
    internal bool OffersNaming => !HasSettings;

    /// <summary>Its live sessions, each with its folder and when it last asked; display only (T-3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessions), nameof(SharesRules))]
    internal partial IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    internal bool HasSessions => Sessions.Count > 0;

    /// <summary>Whether several sessions share this one set of rules, which the detail says.</summary>
    internal bool SharesRules => Sessions.Count > 1;

    /// <summary>A found tool without keypaste, or one whose configuration keypaste cannot read, has Connect on its row.</summary>
    internal bool OffersConnect => Tool is not null && Presence?.State is McpToolState.NotConnected or McpToolState.CannotTell;

    /// <summary>What Connect says, on its button and in the menus.</summary>
    internal string ConnectLabel => $"Connect {Title}…";

    /// <summary>What Repair says, on its button and in the menus.</summary>
    internal string RepairLabel => $"Repair {Title}…";

    /// <summary>A connected tool, or one keypaste cannot read, can be disconnected.</summary>
    internal bool OffersDisconnect => Tool is not null && Presence?.State is McpToolState.Connected or McpToolState.CannotTell;

    /// <summary>Whether the connected tool's entry differs from what Connect would write, which Repair… writes again.</summary>
    [ObservableProperty]
    internal partial bool OffersRepair { get; set; }

    internal bool OffersToolActions => OffersRepair || OffersDisconnect;

    /// <summary>Whether its settings are open; opening them the first time reads them.</summary>
    internal bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!HasSettings || !SetProperty(ref _isOpen, value) || !value || _settings is not null)
            {
                return;
            }

            _settings = _openSettings(this);
            OnPropertyChanged(nameof(Settings));
        }
    }

    /// <summary>Its settings, once its detail has been opened.</summary>
    internal AgentSettingsViewModel? Settings => _settings;

    /// <summary>Takes what Core built for this key again, keeping what is open.</summary>
    internal void Update(McpClientCard card, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);

        var presence = !Equals(_card.Presence, card.Presence);
        _card = card;
        _now = now;

        if (presence)
        {
            OnPropertyChanged(nameof(Presence));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(OffersConnect));
            OnPropertyChanged(nameof(OffersDisconnect));
            OnPropertyChanged(nameof(OffersToolActions));
        }

        List<AgentSession> sessions = [.. card.Sessions.Select(session => new AgentSession(session.Folder ?? "—", Asked(session.LastRequestAt)))];

        if (!sessions.SequenceEqual(Sessions))
        {
            Sessions = sessions;
        }

        IsLive = card.Connections > 0 || card.Presence?.State == McpToolState.Connected;
        Status = string.Join(" · ", StatusParts(card));
        StatusDetail = card.Presence?.State is McpToolState.CannotTell or McpToolState.Problem ? card.Presence.Reason : null;
    }

    partial void OnOffersRepairChanged(bool value) => OnPropertyChanged(nameof(OffersToolActions));

    /// <summary>Re-reads the open settings, after a change made elsewhere on the screen.</summary>
    internal void ReadSettings() => _settings?.Refresh();

    private string Asked(DateTimeOffset at) => _now - at < TimeSpan.FromMinutes(1) ? "in use now" : "last asked " + UseText.Ago(at, _now);

    private IEnumerable<string> StatusParts(McpClientCard card)
    {
        if (!HasSettings)
        {
            yield return "No name: uses the defaults";
            yield break;
        }

        if (card.Presence is { } presence)
        {
            yield return presence.State switch
            {
                McpToolState.Connected => "Connected",
                McpToolState.NotConnected => "Not connected",
                McpToolState.CannotTell => "Can't read its settings",
                _ => $"{Title} can't be connected",
            };
        }

        if (card.Connections > 1)
        {
            yield return string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{card.Connections} running");
        }

        if (card.Connections > 0)
        {
            yield return "in use now";
        }
        else if (card.LastSeen is { } seen)
        {
            yield return "last asked " + UseText.Ago(seen, _now);
        }
        else if (card.Presence is null)
        {
            yield return "hasn't asked in the last 30 days";
        }
    }

    /// <summary>Two lower-case letters: the first of two words, or the first two of one (<c>cursor</c> is <c>cu</c>).</summary>
    private static string InitialsOf(string title)
    {
        var words = title.Split(['-', ' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : words.FirstOrDefault() ?? string.Empty;
        return new string([.. initials.Take(2)]).ToLowerInvariant();
    }

    /// <summary>What a screen reader says for the row in the list: its name and what it is now.</summary>
    public override string ToString() => $"{Title}, {Status}";

    public void Dispose() => _settings?.Dispose();
}
