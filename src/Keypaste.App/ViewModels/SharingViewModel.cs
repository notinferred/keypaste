using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Sharing;

namespace Keypaste.App.ViewModels;

/// <summary>How a share's status dot is drawn.</summary>
internal enum ShareStatusTone
{
    /// <summary>Gone, expired or not known.</summary>
    Muted = 0,

    /// <summary>Opened at least once and still opening.</summary>
    Accent = 1,

    /// <summary>Still opening and not opened yet.</summary>
    Ok = 2,
}

/// <summary>
/// One share link in the Sharing screen's list: names and limits, never a key or a value.
/// <c>Opens</c> is whether the link may still open, so revoking it means something.
/// </summary>
internal sealed record SharingRow(string Id, string What, string Recipient, string Rule, string Status, ShareStatusTone StatusTone, bool Opens, bool Checked = true)
{
    /// <summary>The second line: who it went to and its limits.</summary>
    internal string Detail => $"{Recipient} · {Rule}";

    internal bool IsOk => StatusTone == ShareStatusTone.Ok;

    internal bool IsAccent => StatusTone == ShareStatusTone.Accent;

    /// <summary>A link that may still open is revoked; one that cannot is only forgotten.</summary>
    internal string RevokeLabel => Opens ? "Revoke" : "Remove";
}

/// <summary>
/// The Sharing screen: the links made from this vault, and the form that makes one (D-0354).
/// </summary>
/// <remarks>
/// The link carries the key, so it leaves only through the clipboard's countdown, and a link the
/// clipboard would not take is withdrawn at once. The screen keeps no link; the toast names the
/// limits, not the link. Everything read out of the vault goes on <see cref="Dispose"/>, which the
/// shell calls on every lock. Nothing is asked of the network because the screen opened: statuses
/// are checked on request, and whether the share server takes links is learned from its answer.
/// </remarks>
internal sealed class SharingViewModel : ObservableObject, IDisposable
{
    /// <summary>What <see cref="Ttl"/> offers.</summary>
    internal static readonly IReadOnlyList<string> TtlOptions = ["1h", "24h", "7d"];

    /// <summary>What <see cref="Views"/> offers.</summary>
    internal static readonly IReadOnlyList<int> ViewOptions = [1, 3, 10];

    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly ShareService _service;
    private readonly Action<string> _showToast;
    private readonly bool _misconfigured;
    private Dictionary<string, EntryName> _entries = new(StringComparer.Ordinal);

    private IReadOnlyList<SharingRow> _rows = [];
    private IReadOnlyList<string> _candidates = [];
    private SharingRow? _selected;
    private string? _selectedWhat;
    private string _field = "password";
    private string _recipient = string.Empty;
    private string _ttl = "24h";
    private int _views = 1;
    private bool _requirePassphrase;
    private string? _error;
    private string? _unavailable;

    /// <param name="session">The open vault.</param>
    /// <param name="clipboard">Where the link goes, with its clear countdown.</param>
    /// <param name="service">The share server's client.</param>
    /// <param name="showToast">Says what a share or revocation did.</param>
    /// <param name="unavailable">Why no link can be made from here at all, such as an endpoint that did not resolve, or null.</param>
    internal SharingViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        ShareService service,
        Action<string>? showToast = null,
        string? unavailable = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(service);

        _session = session;
        _clipboard = clipboard;
        _service = service;
        _showToast = showToast ?? (_ => { });
        _unavailable = unavailable;
        _misconfigured = unavailable is not null;
        Passphrase = new SecretField(clipboard);

        CreateCommand = new AsyncRelayCommand(CreateAsync, () => _selectedWhat is not null && _unavailable is null);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync, () => _selected is not null);
        RevokeRowCommand = new RelayCommand<SharingRow>(row =>
        {
            Selected = row;
            RevokeCommand.Execute(null);
        });
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(online: true));
        RetryCommand = new AsyncRelayCommand(RetryAsync);

        _ = LoadAsync(online: false);
    }

    /// <summary>What the Expires control offers.</summary>
#pragma warning disable CA1822
    internal IReadOnlyList<string> TtlChoices => TtlOptions;

    /// <summary>What the Views control offers.</summary>
    internal IReadOnlyList<int> ViewChoices => ViewOptions;

    /// <summary>The fields a link can carry.</summary>
    internal IReadOnlyList<string> FieldOptions => ShareService.Fields;
#pragma warning restore CA1822

    /// <summary>The links made from this vault, soonest to expire first.</summary>
    internal IReadOnlyList<SharingRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(IsEmpty));
                Raise(nameof(HasUnchecked));
            }
        }
    }

    /// <summary>Whether the vault remembers no link.</summary>
    internal bool IsEmpty => _rows.Count == 0;

    /// <summary>Whether a link's status has not been asked for yet, since opening the screen asks nothing.</summary>
    internal bool HasUnchecked => _rows.Any(row => !row.Checked);

    /// <summary>The entries that can be shared, by path; keypaste's own records are not among them.</summary>
    internal IReadOnlyList<string> Candidates
    {
        get => _candidates;
        private set => Set(ref _candidates, value);
    }

    /// <summary>The row <see cref="RevokeCommand"/> acts on.</summary>
    internal SharingRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                RevokeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The path of the entry to share, one of <see cref="Candidates"/>.</summary>
    internal string? SelectedWhat
    {
        get => _selectedWhat;
        set
        {
            if (Set(ref _selectedWhat, value))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>One of <see cref="FieldOptions"/>.</summary>
    internal string Field
    {
        get => _field;
        set => Set(ref _field, value ?? "password");
    }

    /// <summary>A label for the person's own list. Never sent.</summary>
    internal string Recipient
    {
        get => _recipient;
        set => Set(ref _recipient, value ?? string.Empty);
    }

    /// <summary>One of <see cref="TtlOptions"/>.</summary>
    internal string Ttl
    {
        get => _ttl;
        set => Set(ref _ttl, value ?? "24h");
    }

    /// <summary>One of <see cref="ViewOptions"/>.</summary>
    internal int Views
    {
        get => _views;
        set => Set(ref _views, value);
    }

    /// <summary>Whether the recipient must also type a passphrase, sent to them separately.</summary>
    internal bool RequirePassphrase
    {
        get => _requirePassphrase;
        set => Set(ref _requirePassphrase, value);
    }

    /// <summary>The passphrase, entered through the masked field.</summary>
    internal SecretField Passphrase { get; }

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    internal bool HasError => _error is not null;

    /// <summary>Why no link can be made right now, or null while sharing is thought to work.</summary>
    internal string? Unavailable
    {
        get => _unavailable;
        private set
        {
            if (Set(ref _unavailable, value))
            {
                Raise(nameof(IsUnavailable));
                Raise(nameof(CanRetry));
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsUnavailable => _unavailable is not null;

    /// <summary>Whether trying again could change the answer: the server's could, a bad endpoint's cannot.</summary>
    internal bool CanRetry => _unavailable is not null && !_misconfigured;

    /// <summary>Uploads the share and copies its link.</summary>
    internal AsyncRelayCommand CreateCommand { get; }

    /// <summary>Revokes the selected link and forgets it.</summary>
    internal AsyncRelayCommand RevokeCommand { get; }

    /// <summary>Revokes the link on the row it is pressed for.</summary>
    internal RelayCommand<SharingRow> RevokeRowCommand { get; }

    /// <summary>Asks the share server how many views each link has left.</summary>
    internal AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Makes the link again after the share server said it was unavailable.</summary>
    internal AsyncRelayCommand RetryCommand { get; }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Rows = [];
        Candidates = [];
        _entries = new(StringComparer.Ordinal);
        Selected = null;
        SelectedWhat = null;
        Error = null;
        Passphrase.Dispose();
    }

    private async Task CreateAsync()
    {
        Error = null;

        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        if (_selectedWhat is null || !_entries.TryGetValue(_selectedWhat, out var entry))
        {
            Error = "Choose what to share.";
            return;
        }

        if (_requirePassphrase && !Passphrase.HasValue)
        {
            Error = "Type the passphrase the recipient will need, or turn the passphrase off.";
            return;
        }

        using var passphrase = _requirePassphrase ? Buffer(Passphrase) : null;
        var request = new ShareRequest(entry, _field, TtlOf(_ttl), _views, passphrase, _recipient.Length == 0 ? null : _recipient);
        var outcome = await _service.CreateAsync(vault, request, CancellationToken.None).ConfigureAwait(true);

        if (!outcome.Ok)
        {
            if (outcome.Failure is ShareFailure.Unavailable or ShareFailure.Network)
            {
                Unavailable = Sentence(outcome.Message);
            }
            else
            {
                Error = Sentence(outcome.Message);
            }

            return;
        }

        await _clipboard.CopyAsync(outcome.Link!, "Share link").ConfigureAwait(true);
        if (_clipboard.Failure is not null)
        {
            var withdrawn = await _service.RevokeAsync(vault, outcome.Info!.Id, CancellationToken.None).ConfigureAwait(true);
            Error = withdrawn.Ok
                ? "The link could not be copied, so it was withdrawn."
                : $"The link could not be copied, and withdrawing it failed: {withdrawn.Message}. Revoke it from the list.";
            await LoadAsync(online: false).ConfigureAwait(true);
            return;
        }

        var info = outcome.Info!;
        _showToast($"Link copied. Expires in {ShareInfo.FormatTtl(info.Expires - info.Created)}, {(info.Views == 1 ? "1 view" : $"{info.Views} views")}.");
        Passphrase.Clear();

        await LoadAsync(online: true).ConfigureAwait(true);
    }

    private Task RetryAsync()
    {
        Unavailable = null;
        return CreateAsync();
    }

    private async Task RevokeAsync()
    {
        Error = null;

        if (_selected is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        var outcome = await _service.RevokeAsync(vault, row.Id, CancellationToken.None).ConfigureAwait(true);
        if (outcome.Ok)
        {
            _showToast(row.Opens ? "Revoked. The link no longer opens." : "Removed from the list.");
        }
        else
        {
            Error = outcome.Failure == ShareFailure.None
                ? Sentence(outcome.Message)
                : $"{Sentence(outcome.Message)} The link still opens.";
        }

        await LoadAsync(online: false).ConfigureAwait(true);
    }

    private async Task LoadAsync(bool online)
    {
        if (_session.Unlocked is not { } vault)
        {
            Rows = [];
            Candidates = [];
            return;
        }

        Dictionary<string, EntryName> entries = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);
        foreach (var entry in vault.ReadEntries().Where(entry => !ReservedGroups.IsReserved(entry.GroupPath)))
        {
            if (!entries.TryAdd(entry.Path, EntryName.Of(entry)))
            {
                ambiguous.Add(entry.Path);
            }
        }

        foreach (var path in ambiguous)
        {
            entries.Remove(path);
        }

        _entries = entries;
        Candidates = [.. entries.Keys.Order(StringComparer.OrdinalIgnoreCase)];

        var wanted = _selected?.Id;
        var listed = await _service.ListAsync(vault, online, CancellationToken.None).ConfigureAwait(true);
        Rows = [.. listed.Select(share => Row(share, online))];
        Selected = wanted is null ? null : Rows.FirstOrDefault(row => row.Id == wanted);
    }

    private static SharingRow Row((ShareInfo Info, string Status, int? ViewsLeft) listed, bool online)
    {
        var (info, status, left) = listed;

        // The server answers the same for a link opened to its last view and one revoked elsewhere.
        var (text, tone, opens) = (status, left) switch
        {
            ("gone", _) => ("Opened or revoked", ShareStatusTone.Muted, false),
            ("expired", _) => ("Expired", ShareStatusTone.Muted, false),
            (_, { } views) when views >= info.Views => ("Not opened yet", ShareStatusTone.Ok, true),
            (_, { } views) => ($"{info.Views - views} of {info.Views} views used", ShareStatusTone.Accent, true),
            _ => (online ? "Status unknown" : "Not checked", ShareStatusTone.Muted, true),
        };

        return new SharingRow(
            info.Id,
            EntryNameSanitizer.SanitizePath(info.What).Text,
            info.Recipient is { } to ? EntryNameSanitizer.SanitizeProse(to).Text : "Anyone with the link",
            info.Rule,
            text,
            tone,
            opens,
            Checked: online || !opens);
    }

    private static TimeSpan TtlOf(string ttl) => ttl switch
    {
        "1h" => TimeSpan.FromHours(1),
        "7d" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromHours(24),
    };

    private static SecretBuffer Buffer(SecretField field)
    {
        var buffer = new SecretBuffer();
        buffer.Append(field.Compose());
        return buffer;
    }

    // The wordmark stays lowercase even at the start of a sentence.
    private static string Sentence(string message) =>
        message.Length == 0 ? message
        : message.StartsWith("keypaste", StringComparison.Ordinal) ? message + "."
        : char.ToUpperInvariant(message[0]) + message[1..] + ".";
}
