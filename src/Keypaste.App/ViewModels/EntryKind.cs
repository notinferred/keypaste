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

    /// <summary>A key of an env project, which a <c>kp://</c> env reference names.</summary>
    Variable = 3,
}

/// <summary>Reads an entry's kind from which of its fields are filled in, never from what they hold.</summary>
internal static class EntryKinds
{
    internal static EntryKind Of(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (EnvPlace.Of(entry.GroupPath, entry.Title) is not null)
        {
            return EntryKind.Variable;
        }

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
        EntryKind.Variable => "Env variable",
        _ => "Password",
    };

    /// <summary>The Lucide icon the row draws.</summary>
    internal static string Icon(EntryKind kind) => kind switch
    {
        EntryKind.Login => "globe",
        EntryKind.Note => "sticky-note",
        EntryKind.Variable => "key-round",
        _ => "lock",
    };
}

/// <summary>An env project and profile, read from a group path the way a <c>kp://</c> reference reads it.</summary>
/// <param name="Project">The project.</param>
/// <param name="Profile">The profile; <see cref="EnvProfileNames.Default"/> for the project group itself.</param>
internal sealed record EnvPlace(string Project, string Profile)
{
    /// <summary>Where the entry <paramref name="title"/> in <paramref name="groupPath"/> is a variable, or null.</summary>
    internal static EnvPlace? Of(string groupPath, string title) =>
        KpReferences.ForEntry(new EntryName(groupPath, title)) is { } text
        && KpReferences.TryParse(text, out var reference, out _)
        && reference is EnvReference env
            ? new EnvPlace(env.Project, env.Profile)
            : null;

    /// <summary>The project and profile a group holds variables for, or null for any other group.</summary>
    internal static EnvPlace? OfGroup(string groupPath) => Of(groupPath, "KEY");
}
