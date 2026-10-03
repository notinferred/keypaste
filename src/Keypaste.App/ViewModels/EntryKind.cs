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
}

/// <summary>Reads an entry's kind from which of its fields are filled in, never from what they hold.</summary>
internal static class EntryKinds
{
    internal static EntryKind Of(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Username.Length > 0 || entry.Url.Length > 0)
        {
            return EntryKind.Login;
        }

        return entry.Password.Length == 0 && entry.Notes.Length > 0 ? EntryKind.Note : EntryKind.Password;
    }

    internal static string Label(EntryKind kind) => kind switch
    {
        EntryKind.Login => "Login",
        EntryKind.Note => "Secure note",
        _ => "Password",
    };

    /// <summary>The Lucide icon the row draws.</summary>
    internal static string Icon(EntryKind kind) => kind switch
    {
        EntryKind.Login => "globe",
        EntryKind.Note => "sticky-note",
        _ => "lock",
    };
}
