using Keypaste.App.Resources;
using Keypaste.Core;

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
        }

        if (entry.Username.Length > 0 || entry.Url.Length > 0)
        {
            return EntryKind.Login;
        }

        return entry.Password.Length == 0 && entry.Notes.Length > 0 ? EntryKind.Note : EntryKind.Password;
    }

    internal static string Label(EntryKind kind) => kind switch
    {
        EntryKind.Login => Strings.Login,
        EntryKind.Note => "Secure note",
        EntryKind.ApiKey => "API key",
        EntryKind.Database => "Database",
        EntryKind.Server => "Server",
        _ => Strings.Password,
    };

    /// <summary>The Lucide icon the row draws.</summary>
    internal static string Icon(EntryKind kind) => kind switch
    {
        EntryKind.Login => "globe",
        EntryKind.Note => "sticky-note",
        EntryKind.ApiKey => "key-round",
        EntryKind.Database => "database",
        EntryKind.Server => "server",
        _ => "lock",
    };
}
