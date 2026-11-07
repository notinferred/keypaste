using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace Keypaste.Core.Tests;

/// <summary>
/// An edit by another KeePass program, through KeePassLib directly: a revision of the entry as it was,
/// then the change, written at the time given, as a phone app or KeePassXC saves one.
/// </summary>
internal static class ForeignEdit
{
    /// <summary>Edits the one entry titled <paramref name="title"/> and saves the file.</summary>
    internal static void Apply(string path, string masterPassword, string title, DateTime at, Action<PwEntry> edit) =>
        InFile(path, masterPassword, database =>
        {
            var entry = Single(database, title);
            entry.CreateBackup(database);
            edit(entry);
            entry.LastModificationTime = at;
        });

    /// <summary>Saves a copy of an entry beside it under its UUID, which KeePass's format forbids and a file can still hold.</summary>
    internal static void CloneKeepingItsUuid(string path, string masterPassword, string title, string twinTitle, string twinPassword) =>
        InFile(path, masterPassword, database =>
        {
            var original = Single(database, title);
            var twin = original.CloneDeep();
            twin.Strings.Set(PwDefs.TitleField, new ProtectedString(false, twinTitle));
            twin.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, twinPassword));
            original.ParentGroup.AddEntry(twin, true);
        });

    private static PwEntry Single(PwDatabase database, string title) =>
        database.RootGroup.GetEntries(true).Single(candidate => candidate.Strings.ReadSafe(PwDefs.TitleField) == title);

    private static void InFile(string path, string masterPassword, Action<PwDatabase> work)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(masterPassword), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            work(database);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }
}
