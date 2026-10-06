using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Keypaste.Core.Tests;

/// <summary>
/// An edit by another KeePass program, through KeePassLib directly: a revision of the entry as it was,
/// then the change, written at the time given, as a phone app or KeePassXC saves one.
/// </summary>
internal static class ForeignEdit
{
    /// <summary>Edits the one entry titled <paramref name="title"/> and saves the file.</summary>
    internal static void Apply(string path, string masterPassword, string title, DateTime at, Action<PwEntry> edit)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(masterPassword), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            var entry = database.RootGroup.GetEntries(true).Single(candidate => candidate.Strings.ReadSafe(PwDefs.TitleField) == title);
            entry.CreateBackup(database);
            edit(entry);
            entry.LastModificationTime = at;
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }
}
