using System.Collections.ObjectModel;
using Avalonia.Controls;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Browser;
using Keypaste.Core.Totp;

namespace Keypaste.App.ViewModels;

/// <summary>What a new item starts from: a preset of fields, never a limit on them.</summary>
internal enum ItemTemplate
{
    Login,
    ApiKey,
    Environment,
    Card,
    Note,
    Identity,
    SshKey,
    Server,
    Database,
    Wifi,
    Blank,
}

/// <summary>A custom field a template starts with, named and empty.</summary>
internal sealed record PresetField(string Name, bool IsProtected);

/// <summary>One template as New item's grid, the list's "+" and the menus show it, with the fields it presets.</summary>
internal sealed record TemplateChoice(ItemTemplate Template, string Label, string Icon, string NewTitle)
{
    /// <summary>Every template, in the grid's order.</summary>
    internal static IReadOnlyList<TemplateChoice> All { get; } =
    [
        new(ItemTemplate.Login, "Login", "globe", "New login"),
        new(ItemTemplate.ApiKey, "API key", "key", "New API key"),
        new(ItemTemplate.Environment, "Environment", "braces", "New environment"),
        new(ItemTemplate.Card, "Card", "credit-card", "New card"),
        new(ItemTemplate.Note, "Note", "file-text", "New note"),
        new(ItemTemplate.Identity, "Identity", "id-card", "New identity"),
        new(ItemTemplate.SshKey, "SSH key", "key-square", "New SSH key"),
        new(ItemTemplate.Server, "Server", "server", "New server"),
        new(ItemTemplate.Database, "Database", "database", "New database"),
        new(ItemTemplate.Wifi, "Wi-Fi", "wifi", "New Wi-Fi network"),
        new(ItemTemplate.Blank, "Blank item", "plus", "New item"),
    ];

    /// <summary>The templates the list's "+" offers where its place holds every kind, before More templates….</summary>
    internal static IReadOnlyList<TemplateChoice> Common { get; } = [.. All.Take(5)];

    /// <summary>The templates that keep a kind, which an item's Change kind offers.</summary>
    internal static IReadOnlyList<TemplateChoice> Kept { get; } = [.. All.Where(choice => choice.Kind != ItemKind.None)];

    internal static TemplateChoice Of(ItemTemplate template) => All.First(choice => choice.Template == template);

    /// <summary>The kind the item keeps once created, as its icon; a blank item keeps none.</summary>
    internal ItemKind Kind => Template switch
    {
        ItemTemplate.Login => ItemKind.Login,
        ItemTemplate.ApiKey => ItemKind.ApiKey,
        ItemTemplate.Environment => ItemKind.Environment,
        ItemTemplate.Card => ItemKind.Card,
        ItemTemplate.Note => ItemKind.SecureNote,
        ItemTemplate.Identity => ItemKind.Identity,
        ItemTemplate.SshKey => ItemKind.SshKey,
        ItemTemplate.Server => ItemKind.Server,
        ItemTemplate.Database => ItemKind.Database,
        ItemTemplate.Wifi => ItemKind.Wifi,
        _ => ItemKind.None,
    };

    /// <summary>The standard fields the form starts with.</summary>
    internal IReadOnlyList<NewItemField> Fields => Template switch
    {
        ItemTemplate.Login => [NewItemField.Username, NewItemField.Password, NewItemField.Url, NewItemField.OneTimeCode],
        ItemTemplate.ApiKey => [NewItemField.Url],
        ItemTemplate.Environment => [NewItemField.Tags],
        ItemTemplate.Note => [NewItemField.Notes],
        ItemTemplate.Server or ItemTemplate.Database => [NewItemField.Username, NewItemField.Password, NewItemField.OneTimeCode],
        ItemTemplate.Wifi => [NewItemField.Password],
        _ => [],
    };

    /// <summary>The custom fields the form starts with, named and empty.</summary>
    internal IReadOnlyList<PresetField> CustomFields => Template switch
    {
        ItemTemplate.Card => [new("Cardholder name", false), new("Number", true), new("Expiration date", false), new("Security code", true)],
        ItemTemplate.Identity => [new("First name", false), new("Last name", false), new("Email", false), new("Phone", false), new("Passport number", true)],
        ItemTemplate.SshKey => [new("Private key", true), new("Public key", false)],
        ItemTemplate.Wifi => [new("Network name", false)],
        _ => [],
    };

    /// <summary>The tag a card or an identity starts with, which makes an agent's request for one of its protected fields a card or identity value.</summary>
    internal string? Tag => Template switch
    {
        ItemTemplate.Card => FieldCategories.CardTag,
        ItemTemplate.Identity => FieldCategories.IdentityTag,
        _ => null,
    };

    /// <summary>How the add menus name it: the form it opens, so with an ellipsis.</summary>
    internal string MenuLabel => $"{Label}…";

    public override string ToString() => Label;
}

/// <summary>An open vault a new item can be saved in, as the Save in pop-up names it.</summary>
internal sealed record SaveInChoice(AppVaultSession Session, string Account, string Vault)
{
    internal string Label => $"{Account} › {Vault}";

    public override string ToString() => Label;
}

/// <summary>A field the New item form can hold; the button on its left offers the ones not yet added.</summary>
internal enum NewItemField
{
    Username,
    Password,
    Url,
    OneTimeCode,
    Notes,
    Tags,
    CustomField,
    ProtectedField,
}

/// <summary>One field option as the add button's list shows it, with its icon.</summary>
internal sealed record NewItemFieldOption(NewItemField Field, string Label, string Icon);

/// <summary>A field the person named, added from the list; protected, its value is masked and read once, at Save.</summary>
internal sealed partial class NewItemCustomField : ObservableObject, IDisposable
{
    private readonly Action<NewItemCustomField> _remove;

    private readonly string _presetName;

    internal NewItemCustomField(bool isProtected, Action<NewItemCustomField> remove, ClipboardCountdown clipboard, string presetName = "")
    {
        IsProtected = isProtected;
        _remove = remove;
        _presetName = presetName;
        Name = presetName;
        Secret = new SecretField(clipboard);
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    internal bool IsProtected { get; }

    internal string Kind => IsProtected ? "Protected field" : "Custom field";

    /// <summary>What the name box and the remove button are called: the field's name, or its kind while it has none.</summary>
    internal string Spoken => Name.Trim().Length > 0 ? Name.Trim() : Kind;

    /// <summary>Whether a template named it, so its row shows the name as its label rather than a box to type one in.</summary>
    internal bool IsPreset => _presetName.Length > 0;

    /// <summary>Whether it is a template's field still as the template named it with nothing in it, which Save leaves out.</summary>
    internal bool IsUntouchedPreset => _presetName.Length > 0 && Name == _presetName && !HasValue;

    private bool HasValue => IsProtected ? Secret.HasValue : Value.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameProblem), nameof(Spoken))]
    internal partial string Name { get; set; } = string.Empty;

    /// <summary>The value of a plain field.</summary>
    [ObservableProperty]
    internal partial string Value { get; set; } = string.Empty;

    internal SecretField Secret { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameProblem))]
    internal partial string? NameProblem { get; set; }

    internal bool HasNameProblem => NameProblem is not null;

    internal RelayCommand RemoveCommand { get; }

    internal bool HoldsDraft => Name.Trim() != _presetName || (IsProtected ? Secret.HasValue && !Secret.IsGenerated : Value.Length > 0);

    partial void OnNameChanged(string value) => NameProblem = null;

    public void Dispose() => Secret.Dispose();
}

/// <summary>
/// New item: first the grid of templates, then the chosen template's form, its preset fields and any other, saved in any open
/// vault at its top level and created in one change with no history item.
/// </summary>
/// <remarks>
/// A secret it takes, a password, an API key's value or a one-time code's setup, lives in a <see cref="SecretField"/> and is
/// read once, when Save is pressed. A database's or a server's host is a plain custom field named
/// <see cref="HostField"/>, since KeePass has no standard field for one.
/// </remarks>
internal sealed partial class NewItemViewModel : ObservableObject, IDisposable
{
    /// <summary>The custom field a database's or a server's host is kept in.</summary>
    internal const string HostField = IndexedEntry.HostField;

    private const string _keyNameRule = "Name the key as a project reads it: capitals, digits and _, such as OPENAI_API_KEY.";

    private IReadOnlyList<string> _projectTags = [];
    private ItemTemplate? _template;
    private string _presetTitle = string.Empty;
    private string _presetUsername = string.Empty;
    private string _presetUrl = string.Empty;
    private IReadOnlyList<string> _presetTags = [];
    private bool _titleRefused;
    private bool _keyNameRefused;
    private SaveInChoice _saveIn;
    private bool _generatePassword = true;
    private readonly ClipboardCountdown _clipboard;
    private readonly HashSet<NewItemField> _added = [];
    private bool _hasUsername;
    private bool _hasPassword;
    private bool _hasUrl;
    private bool _hasOneTimeCode;
    private bool _hasHost;
    private bool _hasKey;
    private GeneratePasswordViewModel? _generatorPanel;
    private UsernameSuggestions _usernames = new(null);

    /// <param name="places">The open vaults it can be saved in, the one in view first, where it starts.</param>
    /// <param name="clipboard">What the masked fields paste from.</param>
    /// <param name="template">The template whose form opens, or null to choose one from the grid first.</param>
    internal NewItemViewModel(IReadOnlyList<SaveInChoice> places, ClipboardCountdown clipboard, ItemTemplate? template = null)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentOutOfRangeException.ThrowIfZero(places.Count);

        Places = places;
        _clipboard = clipboard;
        _saveIn = places[0];
        ReadVault();

        Password = new SecretField(clipboard);
        KeyValue = new SecretField(clipboard);
        OneTimeCode = new SecretField(clipboard);
        AddTagCommand = new RelayCommand(AddTag, () => DraftTag.Trim().Length > 0);
        GenerateCommand = new RelayCommand(Generate);
        OpenGeneratorCommand = new RelayCommand(OpenGenerator, () => ShowsPassword);
        CloseGeneratorCommand = new RelayCommand(CloseGenerator);
        AddFieldCommand = new RelayCommand<NewItemField>(AddField);
        RemoveFieldCommand = new RelayCommand<NewItemField>(RemoveField);
        ChooseTemplateCommand = new RelayCommand<ItemTemplate>(ChooseTemplate, _ => IsChoosingTemplate);

        // A new item starts with a generated password in its field, where it can be copied before Save.
        Password.GeneratedValueReplaced += (_, _) => SetProperty(ref _generatePassword, false, nameof(GeneratePassword));
        Generator.PropertyChanged += (_, _) =>
        {
            if (_generatePassword && ShowsPassword)
            {
                FillPassword();
            }
        };

        if (template is { } chosen)
        {
            ChooseTemplate(chosen);
        }
    }

    /// <summary>The open vaults the Save in pop-up lists, each under its account.</summary>
    internal IReadOnlyList<SaveInChoice> Places { get; }

    /// <summary>Whether Save in shows: only while there is another vault to save in.</summary>
    internal bool ShowsSaveIn => Places.Count > 1;

    /// <summary>The vault the item is saved in.</summary>
    internal SaveInChoice SaveIn
    {
        get => _saveIn;
        set
        {
            if (value is not null && SetProperty(ref _saveIn, value))
            {
                ReadVault();
                Error = null;
            }
        }
    }

    /// <summary>Reads the chosen vault's project tags and usernames, which the form offers.</summary>
    private void ReadVault()
    {
        var vault = _saveIn.Session.Unlocked;
        _projectTags = vault is null ? [] : ProjectCatalog.MemberTags(vault);
        _usernames = new UsernameSuggestions(vault);
        OnPropertyChanged(nameof(TagSuggestions));
        OnPropertyChanged(nameof(UsedUsernames));
        OnPropertyChanged(nameof(SuggestedUsernames));
        OnPropertyChanged(nameof(UsernameFilter));
    }

    /// <summary>Whether the grid of templates shows, before a template's form.</summary>
    internal bool IsChoosingTemplate => _template is null;

    /// <summary>A tile of the grid: opens that template's form.</summary>
    internal RelayCommand<ItemTemplate> ChooseTemplateCommand { get; }

    private void ChooseTemplate(ItemTemplate template)
    {
        if (_template is not null)
        {
            return;
        }

        _template = template;
        var choice = TemplateChoice.Of(template);
        _presetTitle = choice.NewTitle;
        Title = _presetTitle;
        ApplyTemplate(choice);

        if (choice.Tag is { } tag)
        {
            Tags = [new EntryTagChip(tag, RemoveTag)];
        }

        _presetTags = [.. Tags.Select(chip => chip.Tag)];

        if (_hasPassword)
        {
            FillPassword();
        }

        OnPropertyChanged(nameof(Template));
        OnPropertyChanged(nameof(SelectedTemplate));
        OnPropertyChanged(nameof(IsChoosingTemplate));
        ChooseTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Starts a login for the page a browser asked from: the page's site as its title, its origin as its website and the username
    /// this vault uses most, none of which counts as a draft until changed.
    /// </summary>
    /// <param name="page">What the browser's address for the page gives.</param>
    /// <param name="handBack">What Save and Cancel do once the form closes, to put the browser back in front.</param>
    internal void StartFrom(NewLoginPage page, Action handBack)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(handBack);

        if (page.Title.Length > 0)
        {
            _presetTitle = page.Title;
            Title = page.Title;
        }

        _presetUrl = page.Website;
        Url = page.Website;
        _presetUsername = _usernames.MostUsed ?? string.Empty;
        Username = _presetUsername;
        HandBack = handBack;
    }

    /// <summary>What closing this form by Save or Cancel does after it closes, for a form the browser asked for; null otherwise.</summary>
    internal Action? HandBack { get; private set; }

    /// <summary>The options the add button lists, those not yet in the form; a custom or protected field can be added again.</summary>
    internal IReadOnlyList<NewItemFieldOption> FieldOptions { get; private set; } = [];

    private static readonly string[] _fieldProperties =
    [
        nameof(ShowsUsername), nameof(ShowsPassword), nameof(ShowsUrl), nameof(ShowsOneTimeCode), nameof(ShowsHost), nameof(ShowsKey),
        nameof(ShowsNotes), nameof(ShowsTags), nameof(FillsInBrowser), nameof(HasPresetRows),
        nameof(FieldOptions), nameof(HasFieldOptions), nameof(PresetCustomFields), nameof(AddedCustomFields),
    ];

    private static readonly NewItemFieldOption[] _allOptions =
    [
        new(NewItemField.Username, "Username", "user"),
        new(NewItemField.Password, "Password", "key-round"),
        new(NewItemField.Url, "Website", "link"),
        new(NewItemField.OneTimeCode, "One-time code", "clock"),
        new(NewItemField.Notes, "Notes", "file-text"),
        new(NewItemField.Tags, "Tags and project", "tag"),
        new(NewItemField.CustomField, "Custom field", "plus"),
        new(NewItemField.ProtectedField, "Protected field", "shield-check"),
    ];

    /// <summary>Whether the item will fill in the browser, which any item with a username, a password and a website does, whatever its template.</summary>
    internal bool FillsInBrowser => _hasUsername && _hasPassword && _hasUrl;

    /// <summary>Whether any standard field is in the form, whose group shows above the custom fields.</summary>
    internal bool HasPresetRows => _hasKey || _hasHost || _hasUsername || _hasPassword || _hasUrl || _hasOneTimeCode || _added.Count > 0 || PresetCustomFields.Count > 0;

    /// <summary>The template's own custom fields, such as a card's Number, drawn among its rows.</summary>
    internal IReadOnlyList<NewItemCustomField> PresetCustomFields => [.. CustomFields.Where(custom => custom.IsPreset)];

    /// <summary>The fields the person added, under Custom fields.</summary>
    internal IReadOnlyList<NewItemCustomField> AddedCustomFields => [.. CustomFields.Where(custom => !custom.IsPreset)];

    /// <summary>The tags the Tags row draws: all but the one that makes a card or an identity what it is, which the form keeps without showing.</summary>
    internal IReadOnlyList<EntryTagChip> ShownTags => [.. Tags.Where(chip => !string.Equals(chip.Tag, SelectedTemplate?.Tag, StringComparison.Ordinal))];

    internal RelayCommand<NewItemField> AddFieldCommand { get; }

    internal RelayCommand<NewItemField> RemoveFieldCommand { get; }

    /// <summary>The fields the person added, in the order they were added.</summary>
    internal ObservableCollection<NewItemCustomField> CustomFields { get; } = [];

    internal bool ShowsNotes => _added.Contains(NewItemField.Notes);

    internal bool ShowsTags => _added.Contains(NewItemField.Tags);

    internal bool HasFieldOptions => FieldOptions.Count > 0;

    private void ApplyTemplate(TemplateChoice choice)
    {
        _hasUsername = choice.Fields.Contains(NewItemField.Username);
        _hasPassword = choice.Fields.Contains(NewItemField.Password);
        _hasUrl = choice.Fields.Contains(NewItemField.Url);
        _hasOneTimeCode = choice.Fields.Contains(NewItemField.OneTimeCode);
        _hasHost = choice.Template is ItemTemplate.Database or ItemTemplate.Server;
        _hasKey = choice.Template == ItemTemplate.ApiKey;

        foreach (var field in choice.Fields.Where(field => field is NewItemField.Notes or NewItemField.Tags))
        {
            _added.Add(field);
        }

        foreach (var preset in choice.CustomFields)
        {
            CustomFields.Add(new NewItemCustomField(preset.IsProtected, RemoveCustomField, _clipboard, preset.Name));
        }

        FieldsChanged();
    }

    private void FieldsChanged()
    {
        FieldOptions = [.. _allOptions.Where(option => option.Field is NewItemField.CustomField or NewItemField.ProtectedField || !Has(option.Field))];

        foreach (var name in _fieldProperties)
        {
            OnPropertyChanged(name);
        }

        OpenGeneratorCommand.NotifyCanExecuteChanged();
    }

    private bool Has(NewItemField field) => field switch
    {
        NewItemField.Username => _hasUsername,
        NewItemField.Password => _hasPassword,
        NewItemField.Url => _hasUrl,
        NewItemField.OneTimeCode => _hasOneTimeCode,
        _ => _added.Contains(field),
    };

    private void AddField(NewItemField field)
    {
        switch (field)
        {
            case NewItemField.Username:
                _hasUsername = true;
                break;
            case NewItemField.Password:
                _hasPassword = true;
                if (_generatePassword)
                {
                    FillPassword();
                }

                break;
            case NewItemField.Url:
                _hasUrl = true;
                break;
            case NewItemField.OneTimeCode:
                _hasOneTimeCode = true;
                break;
            case NewItemField.CustomField:
            case NewItemField.ProtectedField:
                CustomFields.Add(new NewItemCustomField(field == NewItemField.ProtectedField, RemoveCustomField, _clipboard));
                break;
            default:
                _added.Add(field);
                break;
        }

        FieldsChanged();
    }

    private void RemoveField(NewItemField field)
    {
        switch (field)
        {
            case NewItemField.Username:
                _hasUsername = false;
                Username = string.Empty;
                break;
            case NewItemField.Password:
                CloseGenerator();
                _hasPassword = false;
                Password.Clear();
                break;
            case NewItemField.Url:
                _hasUrl = false;
                Url = string.Empty;
                break;
            case NewItemField.OneTimeCode:
                _hasOneTimeCode = false;
                OneTimeCode.Clear();
                OneTimeCodeProblem = null;
                break;
            case NewItemField.Notes:
                _added.Remove(field);
                Notes = string.Empty;
                break;
            case NewItemField.Tags:
                _added.Remove(field);
                Tags = [.. Tags.Where(chip => SelectedTemplate?.Tag is { } defining && chip.Tag == defining)];
                DraftTag = string.Empty;
                break;
            default:
                _added.Remove(field);
                break;
        }

        FieldsChanged();
    }

    [RelayCommand]
    private void RemoveHost()
    {
        _hasHost = false;
        Host = string.Empty;
        FieldsChanged();
    }

    [RelayCommand]
    private void RemoveKey()
    {
        _hasKey = false;
        KeyName = string.Empty;
        KeyValue.Clear();
        KeyValueProblem = null;
        FieldsChanged();
    }

    private void RemoveCustomField(NewItemCustomField field)
    {
        CustomFields.Remove(field);
        field.Dispose();
        FieldsChanged();
    }

    internal RelayCommand OpenGeneratorCommand { get; }

    internal RelayCommand CloseGeneratorCommand { get; }

    /// <summary>The generator opened from the password field, or null while it is closed.</summary>
    internal GeneratePasswordViewModel? GeneratorPanel
    {
        get => _generatorPanel;
        private set
        {
            if (SetProperty(ref _generatorPanel, value))
            {
                OnPropertyChanged(nameof(HasGeneratorPanel));
            }
        }
    }

    internal bool HasGeneratorPanel
    {
        get => _generatorPanel is not null;
        set
        {
            if (!value)
            {
                CloseGenerator();
            }
        }
    }

    private void OpenGenerator()
    {
        if (GeneratorPanel is not null)
        {
            return;
        }

        GeneratorPanel = new GeneratePasswordViewModel(_clipboard, Generator, Password, ApplyGenerated, CloseGenerator);
    }

    private void ApplyGenerated(SecretField value)
    {
        SetProperty(ref _generatePassword, true, nameof(GeneratePassword));
        Password.TakeFrom(value);
        Error = null;
    }

    private void CloseGenerator()
    {
        var panel = GeneratorPanel;
        GeneratorPanel = null;
        panel?.Dispose();
    }

    /// <summary>The grid's tiles.</summary>
    internal IReadOnlyList<TemplateChoice> Templates { get; } = TemplateChoice.All;

    /// <summary>The template the form started from, or null while the grid shows.</summary>
    internal ItemTemplate? Template => _template;

    internal TemplateChoice? SelectedTemplate => _template is { } template ? TemplateChoice.Of(template) : null;

    internal bool ShowsUsername => _hasUsername;

    internal bool ShowsPassword => _hasPassword;

    internal bool ShowsUrl => _hasUrl;

    internal bool ShowsOneTimeCode => _hasOneTimeCode;

    internal bool ShowsHost => _hasHost;

    internal bool ShowsKey => _hasKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleProblem), nameof(HasTitleProblem))]
    internal partial string Title { get; set; } = string.Empty;

    /// <summary>Why the title typed cannot be used, or null; an empty one only once Save is pressed on it.</summary>
    internal string? TitleProblem =>
        !HasTitle
            ? (_titleRefused ? "An item needs a title. Type one." : null)
            : EntryNameSanitizer.Sanitize(Title.Trim()) is { WasAltered: true } sanitized
                ? $"That is not a name keypaste will create. Try '{sanitized.Text}'."
                : null;

    partial void OnTitleChanged(string value) => _titleRefused = false;

    internal bool HasTitleProblem => TitleProblem is not null;

    /// <summary>Whether Save can go on: it waits for a title.</summary>
    internal bool HasTitle => Title.Trim().Length > 0;

    /// <summary>What leaving would lose: anything typed, and never what the template or the browser's page preset or a password keypaste generated.</summary>
    internal string? UnsavedDraft =>
        Title.Trim() != _presetTitle
        || Username.Trim() != _presetUsername
        || Url.Trim() != _presetUrl
        || new[] { Host, KeyName, Notes, DraftTag }.Any(text => text.Trim().Length > 0)
        || !Tags.Select(chip => chip.Tag).SequenceEqual(_presetTags, StringComparer.Ordinal)
        || CustomFields.Any(custom => custom.HoldsDraft)
        || KeyValue.HasValue
        || OneTimeCode.HasValue
        || (Password.HasValue && !Password.IsGenerated)
            ? "this new item"
            : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedUsernames), nameof(SuggestedUsernamesSpoken))]
    internal partial string Username { get; set; } = string.Empty;

    /// <summary>The usernames this vault already uses that hold what is typed, which the username box offers; choosing one fills it.</summary>
    internal IReadOnlyList<string> SuggestedUsernames => _usernames.Matching(Username);

    /// <summary>The username box's filter, by the same rule as <see cref="SuggestedUsernames"/>.</summary>
    internal AutoCompleteFilterPredicate<object?> UsernameFilter => UsernameSuggestions.Filter(typed => _usernames.Matching(typed));

    internal string? SuggestedUsernamesSpoken => UsernameSuggestions.Spoken(Username, SuggestedUsernames);

    /// <summary>Every username this vault uses, the username box's items, which it narrows as one is typed.</summary>
    internal IReadOnlyList<string> UsedUsernames => _usernames.Used;

    [ObservableProperty]
    internal partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    internal partial string Host { get; set; } = string.Empty;

    /// <summary>An API key's name, as the environment variable a project would read it under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyNameProblem), nameof(HasKeyNameProblem))]
    internal partial string KeyName { get; set; } = string.Empty;

    /// <summary>Why the key name typed cannot be used, or null; an empty one only once Save is pressed on it.</summary>
    internal string? KeyNameProblem =>
        (KeyName.Trim().Length > 0 || _keyNameRefused) && !EnvConvention.IsEnvNamedField(KeyName.Trim()) ? _keyNameRule : null;

    partial void OnKeyNameChanged(string value) => _keyNameRefused = false;

    internal bool HasKeyNameProblem => KeyNameProblem is not null;

    [ObservableProperty]
    internal partial string Notes { get; set; } = string.Empty;

    /// <summary>Whether the password field holds a generated password, which it does unless the person enters their own.</summary>
    internal bool GeneratePassword
    {
        get => _generatePassword;
        set
        {
            if (!SetProperty(ref _generatePassword, value))
            {
                return;
            }

            if (value)
            {
                FillPassword();
            }
            else
            {
                Password.Clear();
            }
        }
    }

    /// <summary>Generate, beside the password: a new generated password, replacing what the field holds.</summary>
    internal RelayCommand GenerateCommand { get; }

    internal GeneratorViewModel Generator { get; } = new();

    /// <summary>The password: generated, or typed or pasted once <see cref="GeneratePassword"/> is off.</summary>
    internal SecretField Password { get; }

    /// <summary>An API key's value.</summary>
    internal SecretField KeyValue { get; }

    /// <summary>Why Save refused the key's value, or null; it names no value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyValueProblem))]
    internal partial string? KeyValueProblem { get; private set; }

    internal bool HasKeyValueProblem => KeyValueProblem is not null;

    /// <summary>The item's one-time code: its setup key or <c>otpauth://totp/</c> link, kept as its TOTP; empty for none.</summary>
    internal SecretField OneTimeCode { get; }

    /// <summary>Why Save refused the one-time code, or null; it names no value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOneTimeCodeProblem))]
    internal partial string? OneTimeCodeProblem { get; private set; }

    internal bool HasOneTimeCodeProblem => OneTimeCodeProblem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagSuggestions), nameof(TagProblem), nameof(HasTagProblem), nameof(ShownTags))]
    internal partial IReadOnlyList<EntryTagChip> Tags { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    [NotifyPropertyChangedFor(nameof(TagProblem), nameof(HasTagProblem))]
    internal partial string DraftTag { get; set; } = string.Empty;

    /// <summary>Why the tag typed cannot be added, or null.</summary>
    internal string? TagProblem
    {
        get
        {
            var tag = DraftTag.Trim();

            if (tag.Length == 0)
            {
                return null;
            }

            if (!TagRules.IsValid(tag, out var error))
            {
                return string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), ".");
            }

            return Tags.Any(chip => string.Equals(chip.Tag, tag, StringComparison.Ordinal))
                ? $"The item already has the tag {EntryNameSanitizer.Sanitize(tag).Text}."
                : null;
        }
    }

    internal bool HasTagProblem => TagProblem is not null;

    /// <summary>The vault's project tags not yet added, which the tag box offers as a tag is typed; choosing one only fills <see cref="DraftTag"/>.</summary>
    internal IReadOnlyList<string> TagSuggestions => [.. _projectTags.Except(Tags.Select(chip => chip.Tag), StringComparer.Ordinal)];

    internal RelayCommand AddTagCommand { get; }

    /// <summary>Why the last Save wrote nothing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; set; }

    internal bool HasError => Error is not null;

    /// <summary>Creates the item in the vault <see cref="SaveIn"/> names, with one core call and one save.</summary>
    /// <returns>The item's name, or null when it was refused and <see cref="Error"/> says why; nothing is written then.</returns>
    internal EntryName? Create()
    {
        var title = Title.Trim();

        if (IsChoosingTemplate)
        {
            return null;
        }

        if (title.Length == 0)
        {
            _titleRefused = true;
            OnPropertyChanged(nameof(TitleProblem));
            OnPropertyChanged(nameof(HasTitleProblem));
            return null;
        }

        if (HasTitleProblem)
        {
            return null;
        }

        List<FieldWrite> fields = [];

        if (ShowsKey)
        {
            var key = KeyName.Trim();

            if (!EnvConvention.IsEnvNamedField(key))
            {
                _keyNameRefused = true;
                OnPropertyChanged(nameof(KeyNameProblem));
                OnPropertyChanged(nameof(HasKeyNameProblem));
                return null;
            }

            var value = KeyValue.Compose();

            if (value.Length == 0)
            {
                KeyValueProblem = "Type or paste the key's value.";
                return null;
            }

            KeyValueProblem = null;

            fields.Add(new FieldWrite(key, value, Protect: true));
        }

        if (ShowsHost && Host.Trim().Length > 0)
        {
            fields.Add(new FieldWrite(HostField, Host.Trim(), Protect: false));
        }

        if (!TryCustomFields(fields))
        {
            return null;
        }

        var password = string.Empty;

        if (ShowsPassword && !TryNewPassword(out password))
        {
            return null;
        }

        string? oneTimeCode = null;

        if (ShowsOneTimeCode && OneTimeCode.HasValue)
        {
            oneTimeCode = OneTimeCode.Compose();

            if (!TotpSetup.IsAccepted(oneTimeCode, out var refusal))
            {
                OneTimeCodeProblem = DisplayTextSanitizer.Sanitize(refusal).Text;
                return null;
            }
        }

        OneTimeCodeProblem = null;

        var write = _saveIn.Session.Write(
            vault =>
            {
                vault.CreateEntry(
                    new VaultEntry
                    {
                        Title = title,
                        GroupPath = string.Empty,
                        Username = ShowsUsername ? Username.Trim() : string.Empty,
                        Password = password,
                        Url = ShowsUrl ? Url.Trim() : string.Empty,
                        Notes = Notes,
                        Kind = SelectedTemplate?.Kind ?? ItemKind.None,
                    },
                    fields,
                    [.. Tags.Select(chip => chip.Tag)],
                    oneTimeCode);
                return true;
            },
            created => created);

        if (write.Problem("add this again") is { } problem)
        {
            Error = problem;
            return null;
        }

        Error = null;
        return new EntryName(string.Empty, title);
    }

    /// <summary>Removes a draft tag before the item exists.</summary>
    internal void RemoveTag(EntryTagChip chip) => Tags = [.. Tags.Where(tag => !ReferenceEquals(tag, chip))];

    public void Dispose()
    {
        CloseGenerator();
        Password.Dispose();
        KeyValue.Dispose();
        OneTimeCode.Dispose();

        foreach (var field in CustomFields)
        {
            field.Dispose();
        }

        _projectTags = [];
        OnPropertyChanged(nameof(TagSuggestions));
        _usernames = new UsernameSuggestions(null);
        OnPropertyChanged(nameof(UsedUsernames));
    }

    private bool TryCustomFields(List<FieldWrite> fields)
    {
        var taken = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var refused = false;

        foreach (var field in CustomFields.Where(field => !field.IsUntouchedPreset))
        {
            var name = field.Name.Trim();

            field.NameProblem =
                name.Length == 0 ? "Name this field." :
                !FieldNameRules.IsWritable(name, out var error) ? string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), ".") :
                !taken.Add(name) ? $"The item already has a field named {EntryNameSanitizer.Sanitize(name).Text}." :
                null;

            if (field.HasNameProblem)
            {
                refused = true;
                continue;
            }

            fields.Add(new FieldWrite(name, field.IsProtected ? field.Secret.Compose() : field.Value, field.IsProtected));
        }

        return !refused;
    }

    private bool TryNewPassword(out string password)
    {
        if (_generatePassword && Generator.Recipe is null)
        {
            password = string.Empty;
            Error = Generator.Error;
            return false;
        }

        // Empty is allowed: KDBX permits an entry with no password.
        password = Password.Compose();
        return true;
    }

    private void Generate()
    {
        if (_generatePassword)
        {
            FillPassword();
        }
        else
        {
            GeneratePassword = true;
        }
    }

    private void FillPassword()
    {
        if (Generator.Recipe is { } recipe)
        {
            Password.Generate(recipe);
        }
        else
        {
            Password.Clear();
        }
    }

    private void AddTag()
    {
        if (HasTagProblem)
        {
            return;
        }

        Tags = [.. Tags, new EntryTagChip(DraftTag.Trim(), RemoveTag)];
        DraftTag = string.Empty;
        Error = null;
    }
}
