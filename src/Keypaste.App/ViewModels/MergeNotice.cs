using System.Text;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>What the shell says about a merge of another program's save: items and groups by name, never a value (D-0639).</summary>
internal static class MergeNotice
{
    /// <summary>How many items a sentence names before it counts the rest, as D-0492's notice does.</summary>
    private const int _named = 3;

    /// <summary>The notice after a merge that carried this copy's changes into another program's save and saved the result.</summary>
    /// <param name="vault">The vault's file name.</param>
    /// <param name="merged">The merge.</param>
    internal static string Saved(string vault, MergeResult merged)
    {
        ArgumentNullException.ThrowIfNull(merged);

        var said = new StringBuilder($"Another program saved {vault}, and keypaste merged it with your changes and saved.");
        said.Append(Carried(merged.Carried));

        if (merged.Report is not { } report)
        {
            return said.ToString();
        }

        var attention = Attention(report).ToList();

        foreach (var sentence in attention.Take(_named))
        {
            said.Append(' ').Append(sentence);
        }

        var more = attention.Count - _named;

        if (more > 0)
        {
            said.Append(more == 1 ? " One more was merged the same way." : $" {more} more were merged the same way.");
        }

        if (report.Agents.Count > 0)
        {
            said.Append($" The other program's settings for {Listed([.. report.Agents.Select(Shown)])} now apply.");
        }

        return said.ToString();
    }

    /// <summary>Whether the notice names an item or group both copies changed, or one kept against a deletion, so it stays until dismissed.</summary>
    internal static bool NamesAConflict(MergeResult merged) =>
        merged?.Report is { } report && report.Conflicts.Count + report.Kept.Count + report.RenamedGroups.Count > 0;

    /// <summary>Why keypaste did not merge another program's save, as the banner's clause.</summary>
    internal static string Refused(MergeResult refused)
    {
        ArgumentNullException.ThrowIfNull(refused);

        return refused.Outcome switch
        {
            VaultMergeSaveOutcome.AnotherVault => "the file now holds another vault",
            VaultMergeSaveOutcome.BelowKdbx4 =>
                "the file uses an older KeePass format, which keypaste does not merge; choose the newer format in KeePassXC's database settings",
            VaultMergeSaveOutcome.DuplicateUuids => "two items in one copy share an identity, which KeePass does not allow",
            VaultMergeSaveOutcome.HardwareKeyUnanswered => "your hardware key did not answer; keypaste asks it again when you save or the file changes",
            VaultMergeSaveOutcome.Unreadable => "the file could not be read, as while a program is still writing it; keypaste tries again when it changes",
            VaultMergeSaveOutcome.ChangedAgain => "the file kept changing while keypaste merged it; keypaste tries again when it changes",
            VaultMergeSaveOutcome.KeyRefused => "the password or key you unlocked with no longer opens the file",
            _ => $"the merged vault could not be saved: {refused.Reason.TrimEnd('.')}",
        };
    }

    /// <summary>The items the open copy had changed and the merge saved, as D-0492 names what a reload would discard.</summary>
    private static string Carried(IReadOnlyList<EntryName> carried)
    {
        var items = carried.Where(name => !ReservedGroups.IsReserved(name.GroupPath)).ToList();
        List<string> named = [.. items.Take(_named).Select(ApprovalPrompt.Shown)];

        if (items.Count > _named)
        {
            named.Add(items.Count == _named + 1 ? "1 more item" : $"{items.Count - _named} more items");
        }

        if (carried.Count > items.Count)
        {
            named.Add("keypaste's own records");
        }

        return named.Count switch
        {
            0 => string.Empty,
            1 when items.Count == 1 => $" Your change to {named[0]} is saved.",
            _ => $" Your changes to {Listed(named)} are saved.",
        };
    }

    /// <summary>A sentence for each item both copies changed, each kept against a deletion, and each group renamed in both.</summary>
    private static IEnumerable<string> Attention(VaultMergeReport report)
    {
        foreach (var conflict in report.Conflicts)
        {
            yield return conflict.OpenCopyCurrent
                ? $"You and the other program both changed {ApprovalPrompt.Shown(conflict.Item)}. Your version is current; the other program's is in its history."
                : $"You and the other program both changed {ApprovalPrompt.Shown(conflict.Item)}. The other program's version is current; yours is in its history.";
        }

        foreach (var kept in report.Kept)
        {
            var item = ApprovalPrompt.Shown(kept.Item);

            yield return (kept.DeletedInFile, kept.Recycled) switch
            {
                (true, true) => $"The other program deleted {item} after you changed it here. It is in Trash with your change.",
                (true, false) => $"The other program deleted {item} after you changed it here. keypaste kept it with your change.",
                (false, true) => $"You deleted {item} after the other program changed it. It is in Trash with that change.",
                (false, false) => $"You deleted {item} after the other program changed it. keypaste kept it with that change.",
            };
        }

        foreach (var group in report.RenamedGroups)
        {
            yield return $"You and the other program both renamed the group {EntryNameSanitizer.SanitizePath(group.GroupPath).Text}; the name {Shown(group.LostName)} was not kept.";
        }
    }

    private static string Shown(string name) => DisplayTextSanitizer.Sanitize(name).Text;

    private static string Listed(List<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
