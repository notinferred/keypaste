using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

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
/// A secret it takes, a password or an API key's value, lives in a <see cref="SecretField"/> and is
/// read once, when Create is pressed. A database's or a server's host is a plain custom field named
/// <see cref="HostField"/>, since KeePass has no standard field for one.
/// </remarks>
internal sealed class NewItemViewModel : ObservableObject, IDisposable
{
    /// <summary>The custom field a database's or a server's host is kept in.</summary>
    internal const string HostField = "Host";

    private readonly AppVaultSession _session;
    private ItemTemplate _template = ItemTemplate.Login;
    private FolderChoice _folder;
    private string _title = string.Empty;
    private string _username = string.Empty;
    private string _url = string.Empty;
    private string _host = string.Empty;
    private string _keyName = string.Empty;
    private string _notes = string.Empty;
    private string _draftTag = string.Empty;
    private bool _generatePassword = true;
    private IReadOnlyList<EntryTagChip> _tags = [];
    private string? _error;

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

        Folders =
        [
            .. GroupNode.Flatten(groupPaths)
                .Select(node => new FolderChoice(node.Path, node.IsEverything ? "Top level" : node.Label, node.Depth)),
        ];
        _folder = Folders.FirstOrDefault(folder => string.Equals(folder.Path, currentGroup, StringComparison.Ordinal)) ?? Folders[0];

        Password = new SecretField(clipboard);
        KeyValue = new SecretField(clipboard);
        AddTagCommand = new RelayCommand(AddTag, () => _draftTag.Trim().Length > 0);
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
            if (Set(ref _template, value))
            {
                Raise(nameof(SelectedTemplate));
                Raise(nameof(ShowsUsername));
                Raise(nameof(ShowsPassword));
                Raise(nameof(ShowsUrl));
                Raise(nameof(ShowsHost));
                Raise(nameof(ShowsKey));
                Error = null;
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

    internal string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>The vault's groups a new item may go in, the top level first.</summary>
    internal IReadOnlyList<FolderChoice> Folders { get; }

    internal FolderChoice Folder
    {
        get => _folder;
        set
        {
            if (value is not null)
            {
                Set(ref _folder, value);
            }
        }
    }

    internal string Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    internal string Url
    {
        get => _url;
        set => Set(ref _url, value);
    }

    internal string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    /// <summary>An API key's name, as the environment variable a project would read it under.</summary>
    internal string KeyName
    {
        get => _keyName;
        set => Set(ref _keyName, value);
    }

    internal string Notes
    {
        get => _notes;
        set => Set(ref _notes, value);
    }

    /// <summary>Whether the password is generated, which it is unless the person has one to enter.</summary>
    internal bool GeneratePassword
    {
        get => _generatePassword;
        set
        {
            if (Set(ref _generatePassword, value) && value)
            {
                Password.Clear();
            }
        }
    }

    internal GeneratorViewModel Generator { get; } = new();

    /// <summary>A password typed or pasted, while <see cref="GeneratePassword"/> is off.</summary>
    internal SecretField Password { get; }

    /// <summary>An API key's value.</summary>
    internal SecretField KeyValue { get; }

    internal IReadOnlyList<EntryTagChip> Tags
    {
        get => _tags;
        private set => Set(ref _tags, value);
    }

    internal string DraftTag
    {
        get => _draftTag;
        set
        {
            if (Set(ref _draftTag, value))
            {
                AddTagCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal RelayCommand AddTagCommand { get; }

    /// <summary>Why the last Create wrote nothing, or null.</summary>
    internal string? Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    internal bool HasError => _error is not null;

    /// <summary>Creates the item with one core call and one save.</summary>
    /// <returns>The item's name, or null when it was refused and <see cref="Error"/> says why; nothing is written then.</returns>
    internal EntryName? Create()
    {
        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return null;
        }

        var title = _title.Trim();

        if (title.Length == 0)
        {
            Error = "An item needs a title.";
            return null;
        }

        var sanitized = EntryNameSanitizer.Sanitize(title);

        if (sanitized.WasAltered)
        {
            Error = $"That is not a name keypaste will create. Try '{sanitized.Text}'.";
            return null;
        }

        List<FieldWrite> fields = [];

        if (ShowsKey)
        {
            var key = _keyName.Trim();

            if (!EnvConvention.IsEnvNamedField(key))
            {
                Error = "Name the key as a project reads it: capitals, digits and _, such as OPENAI_API_KEY.";
                return null;
            }

            var value = KeyValue.Compose();

            if (value.Length == 0)
            {
                Error = "Type or paste the key's value.";
                return null;
            }

            fields.Add(new FieldWrite(key, value, Protect: true));
        }

        if (ShowsHost && _host.Trim().Length > 0)
        {
            fields.Add(new FieldWrite(HostField, _host.Trim(), Protect: false));
        }

        var password = string.Empty;

        if (ShowsPassword && !TryNewPassword(out password))
        {
            return null;
        }

        try
        {
            vault.CreateEntry(
                new VaultEntry
                {
                    Title = title,
                    GroupPath = _folder.Path,
                    Username = ShowsUsername ? _username.Trim() : string.Empty,
                    Password = password,
                    Url = ShowsUrl ? _url.Trim() : string.Empty,
                    Notes = _notes,
                },
                fields,
                [.. _tags.Select(chip => chip.Tag)]);
            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Reload to see it, then add this again.";
            return null;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return null;
        }

        Error = null;
        return new EntryName(_folder.Path, title);
    }

    /// <summary>Removes a draft tag before the item exists.</summary>
    internal void RemoveTag(EntryTagChip chip) => Tags = [.. _tags.Where(tag => !ReferenceEquals(tag, chip))];

    public void Dispose()
    {
        Password.Dispose();
        KeyValue.Dispose();
    }

    private bool TryNewPassword(out string password)
    {
        if (!_generatePassword)
        {
            // Empty is allowed: KDBX permits an entry with no password.
            password = Password.Compose();
            return true;
        }

        if (Generator.Recipe is not { } recipe)
        {
            password = string.Empty;
            Error = Generator.Error;
            return false;
        }

        using var buffer = new SecretBuffer();
        PasswordGenerator.Append(recipe, buffer);
        password = new string(buffer.Value);
        return true;
    }

    private void AddTag()
    {
        var tag = _draftTag.Trim();

        if (!TagRules.IsValid(tag, out var error))
        {
            Error = string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), ".");
            return;
        }

        if (_tags.Any(chip => string.Equals(chip.Tag, tag, StringComparison.Ordinal)))
        {
            Error = $"The item already has the tag {EntryNameSanitizer.Sanitize(tag).Text}.";
            return;
        }

        Tags = [.. _tags, new EntryTagChip(tag, RemoveTag)];
        DraftTag = string.Empty;
        Error = null;
    }
}
