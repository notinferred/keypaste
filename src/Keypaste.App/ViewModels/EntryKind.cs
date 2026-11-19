using Keypaste.App.Resources;
using Keypaste.Core;
using Keypaste.Core.Agents;

namespace Keypaste.App.ViewModels;

/// <summary>What sort of thing an entry is, as the Secrets list and pane name it.</summary>
internal enum EntryKind
{
    /// <summary>A password with a username or a URL beside it.</summary>
    Login = 0,

    /// <summary>A password on its own.</summary>
    Password = 1,

    /// <summary>Notes and no password.</summary>
    Note = 2,

    /// <summary>An API key or token, as its icon keeps it.</summary>
    ApiKey = 3,

    /// <summary>A database's credentials, as its icon keeps it.</summary>
    Database = 4,

    /// <summary>A server's credentials, as its icon keeps it.</summary>
    Server = 5,

    /// <summary>A payment card, as its icon keeps it.</summary>
    Card = 6,

    /// <summary>A person's identity details, as its icon keeps it.</summary>
    Identity = 7,

    /// <summary>An SSH key pair, as its icon keeps it.</summary>
    SshKey = 8,

    /// <summary>A Wi-Fi network, as its icon keeps it.</summary>
    Wifi = 9,

    /// <summary>A project's .env variables, as its icon keeps it.</summary>
    Environment = 10,
}

/// <summary>Reads an entry's kind from the icon that keeps one, and otherwise from which of its fields are filled in, never from what they hold.</summary>
internal static class EntryKinds
{
    internal static EntryKind Of(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        switch (entry.Kind)
        {
            case ItemKind.Login: return EntryKind.Login;
            case ItemKind.SecureNote: return EntryKind.Note;
            case ItemKind.ApiKey: return EntryKind.ApiKey;
            case ItemKind.Database: return EntryKind.Database;
            case ItemKind.Server: return EntryKind.Server;
            case ItemKind.Card: return EntryKind.Card;
            case ItemKind.Identity: return EntryKind.Identity;
            case ItemKind.SshKey: return EntryKind.SshKey;
            case ItemKind.Wifi: return EntryKind.Wifi;
            case ItemKind.Environment: return EntryKind.Environment;
        }

        if (entry.Username.Length > 0 || entry.Url.Length > 0)
        {
            return EntryKind.Login;
        }

        return entry.Password.Length == 0 && entry.Notes.Length > 0 ? EntryKind.Note : EntryKind.Password;
    }

    /// <summary>The kind with its tags read too: a card's or an identity's tag makes it one whatever its icon, as agent field categories read it.</summary>
    internal static EntryKind Of(VaultEntry entry, IEnumerable<string> tags) => Tagged(Of(entry), tags);

    /// <summary>A kind read from the icon, made a card or an identity by the tag that makes one, and a password a project holds made its environment.</summary>
    internal static EntryKind Tagged(EntryKind kind, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        List<string> held = [.. tags];
        return held.Contains(FieldCategories.CardTag, StringComparer.OrdinalIgnoreCase) ? EntryKind.Card
            : held.Contains(FieldCategories.IdentityTag, StringComparer.OrdinalIgnoreCase) ? EntryKind.Identity
            : kind == EntryKind.Password && held.Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member) ? EntryKind.Environment
            : kind;
    }

    internal static string Label(EntryKind kind) => kind switch
    {
        EntryKind.Login => Strings.Login,
        EntryKind.Note => "Note",
        EntryKind.ApiKey => "API key",
        EntryKind.Database => "Database",
        EntryKind.Server => "Server",
        EntryKind.Card => "Card",
        EntryKind.Identity => "Identity",
        EntryKind.SshKey => "SSH key",
        EntryKind.Wifi => "Wi-Fi",
        EntryKind.Environment => "Environment",
        _ => Strings.Password,
    };

    /// <summary>The Lucide icon the row draws, as the design system's icon table gives each kind one.</summary>
    internal static string Icon(EntryKind kind) => kind switch
    {
        EntryKind.Login => "globe",
        EntryKind.Note => "file-text",
        EntryKind.ApiKey => "key",
        EntryKind.Database => "database",
        EntryKind.Server => "server",
        EntryKind.Card => "credit-card",
        EntryKind.Identity => "id-card",
        EntryKind.SshKey => "key-square",
        EntryKind.Wifi => "wifi",
        EntryKind.Environment => "braces",
        _ => "lock",
    };
}
