namespace Keypaste.Core;

/// <summary>What a save could not carry over from the file it replaced, and the copy of that file kept before it, whatever the backup floor says.</summary>
/// <param name="Kept">The copy of the file from before the save.</param>
/// <param name="OtherProgramsData">Whether the file held data another program or a newer KeePass format wrote that keypaste can't keep, which the save left out.</param>
/// <param name="OldFormat">Whether the file was in a KeePass format older than KDBX 4, which the save replaced with the current one.</param>
public sealed record VaultLeftBehind(VaultBackup Kept, bool OtherProgramsData, bool OldFormat)
{
    /// <summary>What a person is told about the save and the copy.</summary>
    public string Text
    {
        get
        {
            var stays = $"at '{Kept.Path}', which stays among the vault's backups for up to about four weeks.";

            return (OtherProgramsData, OldFormat) switch
            {
                (true, true) =>
                    "keypaste saved this vault in the current KeePass format, which KeePass before 2.35 can't open, and left out data from another program " +
                    $"that it can't keep. It kept a copy of the file from before the save, in the old format and with that data, {stays}",
                (false, true) =>
                    $"keypaste saved this vault in the current KeePass format, which KeePass before 2.35 can't open. It kept a copy of the file in the old format {stays}",
                _ =>
                    "This vault held data from another program that keypaste can't keep, so the save left it out. " +
                    $"keypaste kept a copy of the file from before the save, with that data, {stays}",
            };
        }
    }
}
