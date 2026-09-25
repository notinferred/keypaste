using System.Globalization;
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

    /// <summary>Not opened yet.</summary>
    Ok = 2,
}

/// <summary>One share link in the Sharing screen's list: names and limits, never a key or a value.</summary>
internal sealed record SharingRow(string Id, string What, string Recipient, string Rule, string Status, ShareStatusTone StatusTone);

/// <summary>
/// The Sharing screen: the links made from this vault, and the form that makes one (D-0354).
/// </summary>
/// <remarks>
/// The link carries the key, so it leaves only through the clipboard's countdown, and a link the
/// clipboard would not take is withdrawn at once. The screen keeps no link; the toast names the
/// limits, not the link. Everything read out of the vault goes on <see cref="Dispose"/>, which the
/// shell calls on every lock.
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
    private string? _toast;
    private string? _error;

    internal SharingViewModel(AppVaultSession session, ClipboardCountdown clipboard, ShareService service)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(service);

        _session = session;
        _clipboard = clipboard;
        _service = service;
        Passphrase = new SecretField(clipboard);

        CreateCommand = new AsyncRelayCommand(CreateAsync, () => _selectedWhat is not null);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync, () => _selected is not null);
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(online: true));

        _ = LoadAsync(online: false);
    }

    /// <summary>The fields a link can carry.</summary>
#pragma warning disable CA1822
    internal IReadOnlyList<string> FieldOptions => ShareService.Fields;
#pragma warning restore CA1822

    /// <summary>The links made from this vault, soonest to expire first.</summary>
    internal IReadOnlyList<SharingRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

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
        set => Set(ref _field, value);
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
        set => Set(ref _ttl, value);
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

    /// <summary>What the last share or revocation did, or null.</summary>
    internal string? Toast
    {
        get => _toast;
        private set => Set(ref _toast, value);
    }

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Uploads the share and copies its link.</summary>
    internal AsyncRelayCommand CreateCommand { get; }

    /// <summary>Revokes the selected link and forgets it.</summary>
    internal AsyncRelayCommand RevokeCommand { get; }

    /// <summary>Asks keypaste.com how many views each link has left.</summary>
    internal AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Rows = [];
        Candidates = [];
        _entries = new(StringComparer.Ordinal);
        Selected = null;
        SelectedWhat = null;
        Toast = null;
        Error = null;
        Passphrase.Dispose();
    }

    private async Task CreateAsync()
    {
        Toast = null;
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
            Error = Sentence(outcome.Message);
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
        var until = TimeZoneInfo.ConvertTime(info.Expires, _session.Clock.LocalTimeZone).ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        Toast = $"Link copied. It opens {(info.Views == 1 ? "1 time" : $"{info.Views} times")}, until {until}.";
        Passphrase.Clear();

        await LoadAsync(online: true).ConfigureAwait(true);
    }

    private async Task RevokeAsync()
    {
        Toast = null;
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
            Toast = "Revoked. The link no longer opens.";
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
        Rows = [.. listed.Select(Row)];
        Selected = wanted is null ? null : Rows.FirstOrDefault(row => row.Id == wanted);
    }

    private static SharingRow Row((ShareInfo Info, string Status, int? ViewsLeft) listed)
    {
        var (info, status, left) = listed;
        var tone = left is not { } views ? ShareStatusTone.Muted
            : views == info.Views ? ShareStatusTone.Ok
            : ShareStatusTone.Accent;

        return new SharingRow(
            info.Id,
            EntryNameSanitizer.SanitizePath(info.What).Text,
            info.Recipient is { } to ? EntryNameSanitizer.SanitizeProse(to).Text : "Anyone with the link",
            info.Rule,
            status,
            tone);
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

    private static string Sentence(string message) =>
        message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..] + ".";
}
