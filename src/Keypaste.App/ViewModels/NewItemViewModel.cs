using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Totp;

namespace Keypaste.App.ViewModels;

/// <summary>What a new item starts from (N.4).</summary>
internal enum ItemTemplate
{
    Login,
    ApiKey,
    Database,
    Server,
    SecureNote,
}

/// <summary>One template as the form's choice shows it.</summary>
internal sealed record TemplateChoice(ItemTemplate Template, string Label, string Icon)
{
    public override string ToString() => Label;
}

/// <summary>A group a new item can go in, as the folder picker lists it.</summary>
internal sealed record FolderChoice(string Path, string Label, int Depth)
{
    public override string ToString() => Label;
}

/// <summary>
/// The New item form: a template's own fields, a folder chosen from the vault's groups, tags and
/// notes, created in one change with no history item (N.4, D-0379).
/// </summary>
/// <remarks>
/// A secret it takes, a password, an API key's value or a one-time code's setup, lives in a <see cref="SecretField"/> and is
/// read once, when Create is pressed. A database's or a server's host is a plain custom field named
/// <see cref="HostField"/>, since KeePass has no standard field for one.
/// </remarks>
internal sealed partial class NewItemViewModel : ObservableObject, IDisposable
{
    /// <summary>The custom field a database's or a server's host is kept in.</summary>
    internal const string HostField = "Host";

    private const string _keyNameRule = "Name the key as a project reads it: capitals, digits and _, such as OPENAI_API_KEY.";

    private readonly AppVaultSession _session;
    private IReadOnlyList<string> _projectTags;
    private ItemTemplate _template = ItemTemplate.Login;
    private bool _titleRefused;
    private bool _keyNameRefused;
    private FolderChoice _folder;
    private bool _generatePassword = true;

    /// <param name="session">The unlocked vault the item is made in.</param>
    /// <param name="clipboard">What the masked fields paste from.</param>
    /// <param name="groupPaths">Every group the vault has.</param>
    /// <param name="currentGroup">The group in view, which the folder starts at.</param>
    internal NewItemViewModel(AppVaultSession session, ClipboardCountdown clipboard, IEnumerable<string> groupPaths, string? currentGroup)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(groupPaths);

        _session = session;
        _projectTags = session.Unlocked is { } vault ? ProjectCatalog.MemberTags(vault) : [];

        Folders =
        [
            .. GroupNode.Flatten(groupPaths)
                .Select(node => new FolderChoice(node.Path, node.IsEverything ? "Top level" : node.Label, node.Depth)),
        ];
        _folder = Folders.FirstOrDefault(folder => string.Equals(folder.Path, currentGroup, StringComparison.Ordinal)) ?? Folders[0];

        Password = new SecretField(clipboard);
        KeyValue = new SecretField(clipboard);
        OneTimeCode = new SecretField(clipboard);
        AddTagCommand = new RelayCommand(AddTag, () => DraftTag.Trim().Length > 0);
        GenerateCommand = new RelayCommand(Generate);

        // A new item starts with a generated password in its field, where it can be copied before Create (V.12).
        Password.GeneratedValueReplaced += (_, _) => SetProperty(ref _generatePassword, false, nameof(GeneratePassword));
        Generator.PropertyChanged += (_, _) =>
        {
            if (_generatePassword)
            {
                FillPassword();
            }
        };
        FillPassword();
    }

    /// <summary>The five things a new item can be.</summary>
    internal IReadOnlyList<TemplateChoice> Templates { get; } =
    [
        new(ItemTemplate.Login, "Login", "globe"),
        new(ItemTemplate.ApiKey, "API key", "key-round"),
        new(ItemTemplate.Database, "Database", "database"),
        new(ItemTemplate.Server, "Server", "server"),
        new(ItemTemplate.SecureNote, "Secure note", "sticky-note"),
    ];

    /// <summary>The template the form shows. Changing it keeps the title, folder, tags and notes.</summary>
    internal ItemTemplate Template
    {
        get => _template;
        set
        {
            if (SetProperty(ref _template, value))
            {
                OnPropertyChanged(nameof(SelectedTemplate));
                OnPropertyChanged(nameof(ShowsUsername));
                OnPropertyChanged(nameof(ShowsUrl));
                OnPropertyChanged(nameof(ShowsHost));
                OnPropertyChanged(nameof(ShowsKey));
                OnPropertyChanged(nameof(ShowsPassword));
                KeyValueProblem = null;
                OneTimeCodeProblem = null;
                Error = null;

                // Create ignores a one-time code the template has no field for, so it must not stay as a draft.
                if (!ShowsPassword)
                {
                    OneTimeCode.Clear();
                }
            }
        }
    }

    /// <summary>The template as the choice selects it.</summary>
    internal TemplateChoice SelectedTemplate
    {
        get => Templates.First(choice => choice.Template == _template);
        set
        {
            if (value is not null)
            {
                Template = value.Template;
            }
        }
    }

    internal bool ShowsUsername => _template is ItemTemplate.Login or ItemTemplate.Database or ItemTemplate.Server;

    internal bool ShowsPassword => ShowsUsername;

    internal bool ShowsUrl => _template == ItemTemplate.Login;

    internal bool ShowsHost => _template is ItemTemplate.Database or ItemTemplate.Server;

    internal bool ShowsKey => _template == ItemTemplate.ApiKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleProblem), nameof(HasTitleProblem))]
    internal partial string Title { get; set; } = string.Empty;

    /// <summary>Why the title typed cannot be used, or null; an empty one only once Create is pressed on it.</summary>
    internal string? TitleProblem =>
        !HasTitle
            ? (_titleRefused ? "An item needs a title. Type one." : null)
            : EntryNameSanitizer.Sanitize(Title.Trim()) is { WasAltered: true } sanitized
                ? $"That is not a name keypaste will create. Try '{sanitized.Text}'."
                : null;

    partial void OnTitleChanged(string value) => _titleRefused = false;

    internal bool HasTitleProblem => TitleProblem is not null;

    /// <summary>Whether Create can go on: it waits for a title.</summary>
    internal bool HasTitle => Title.Trim().Length > 0;

    /// <summary>What leaving would lose: anything typed, and never a password keypaste generated.</summary>
    internal string? UnsavedDraft =>
        new[] { Title, Username, Url, Host, KeyName, Notes, DraftTag }.Any(text => text.Trim().Length > 0)
        || Tags.Count > 0
        || KeyValue.HasValue
        || OneTimeCode.HasValue
        || (Password.HasValue && !Password.IsGenerated)
            ? "this new item"
            : null;

    /// <summary>The vault's groups a new item may go in, the top level first.</summary>
    internal IReadOnlyList<FolderChoice> Folders { get; }

    internal FolderChoice Folder
    {
        get => _folder;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _folder, value);
            }
        }
    }

    [ObservableProperty]
    internal partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    internal partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    internal partial string Host { get; set; } = string.Empty;

    /// <summary>An API key's name, as the environment variable a project would read it under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyNameProblem), nameof(HasKeyNameProblem))]
    internal partial string KeyName { get; set; } = string.Empty;

    /// <summary>Why the key name typed cannot be used, or null; an empty one only once Create is pressed on it.</summary>
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

    /// <summary>Why Create refused the key's value, or null; it names no value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyValueProblem))]
    internal partial string? KeyValueProblem { get; private set; }

    internal bool HasKeyValueProblem => KeyValueProblem is not null;

    /// <summary>A login's, database's or server's one-time code: its setup key or <c>otpauth://totp/</c> link, kept as its TOTP; empty for none.</summary>
    internal SecretField OneTimeCode { get; }

    /// <summary>Why Create refused the one-time code, or null; it names no value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOneTimeCodeProblem))]
    internal partial string? OneTimeCodeProblem { get; private set; }

    internal bool HasOneTimeCodeProblem => OneTimeCodeProblem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagSuggestions), nameof(TagProblem), nameof(HasTagProblem))]
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

    /// <summary>Why the last Create wrote nothing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; set; }

    internal bool HasError => Error is not null;

    /// <summary>Creates the item with one core call and one save.</summary>
    /// <returns>The item's name, or null when it was refused and <see cref="Error"/> says why; nothing is written then.</returns>
    internal EntryName? Create()
    {
        var title = Title.Trim();

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

        var password = string.Empty;

        if (ShowsPassword && !TryNewPassword(out password))
        {
            return null;
        }

        string? oneTimeCode = null;

        if (ShowsPassword && OneTimeCode.HasValue)
        {
            oneTimeCode = OneTimeCode.Compose();

            if (!TotpSetup.IsAccepted(oneTimeCode, out var refusal))
            {
                OneTimeCodeProblem = DisplayTextSanitizer.Sanitize(refusal).Text;
                return null;
            }
        }

        OneTimeCodeProblem = null;

        var write = _session.Write(
            vault =>
            {
                vault.CreateEntry(
                    new VaultEntry
                    {
                        Title = title,
                        GroupPath = _folder.Path,
                        Username = ShowsUsername ? Username.Trim() : string.Empty,
                        Password = password,
                        Url = ShowsUrl ? Url.Trim() : string.Empty,
                        Notes = Notes,
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
        return new EntryName(_folder.Path, title);
    }

    /// <summary>Removes a draft tag before the item exists.</summary>
    internal void RemoveTag(EntryTagChip chip) => Tags = [.. Tags.Where(tag => !ReferenceEquals(tag, chip))];

    public void Dispose()
    {
        Password.Dispose();
        KeyValue.Dispose();
        OneTimeCode.Dispose();
        _projectTags = [];
        OnPropertyChanged(nameof(TagSuggestions));
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
