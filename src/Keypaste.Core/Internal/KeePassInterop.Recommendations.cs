extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Cryptography;
using KeePass::KeePassLib.Security;
using KeePass::KeePassLib.Utility;
using Keypaste.Core.Approval;
using Keypaste.Core.Recommendations;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The entry custom data KeePassXC reads, besides KDBX 4.1's <c>QualityCheck</c>, as leaving an entry out of its reports.</summary>
    private const string _excludedFromReports = "KnownBad";

    /// <summary>The longest password, in characters, the estimator rates: its repetition pass grows with the cube of the length and runs under the vault's lock, and KeePassXC stops at the same length.</summary>
    private const int _longestRated = 256;

    /// <summary>The common-password list in KeePassLib's estimator, which every estimate in this process shares, loaded once before the first rating.</summary>
    private static readonly Lazy<bool> _commonPasswords = new(LoadCommonPasswords, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every live entry's lost project memberships, by the traversal <see cref="Collect"/> uses.</summary>
    internal IReadOnlyList<LostProjectTag> ReadLostProjectTags()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<LostProjectTag> lost = [];
        CollectLostProjectTags(_database.RootGroup, string.Empty, lost, Bin());
        return lost;
    }

    /// <summary>Every live entry's password rating and expiry outside keypaste's own groups, by the traversal <see cref="Collect"/> uses.</summary>
    /// <remarks>
    /// No password in <see cref="ReservedGroups"/> is decrypted. The digests are HMACs under a key drawn for this read and
    /// zeroed after it, so they compare passwords within it and confirm nothing outside it.
    /// </remarks>
    internal IReadOnlyList<EntryPasswordHealth> ReadPasswordHealth()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _ = _commonPasswords.Value;
        byte[] key = RandomNumberGenerator.GetBytes(32);

        try
        {
            List<EntryPasswordHealth> found = [];
            CollectPasswordHealth(_database.RootGroup, string.Empty, found, Bin(), key);
            return found;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Every live entry's expiry, URL, kind and needs-update tag outside keypaste's own groups, by the traversal <see cref="Collect"/> uses; no secret is read.</summary>
    internal IReadOnlyList<EntryReminderFacts> ReadReminderFacts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<EntryReminderFacts> found = [];
        CollectReminderFacts(_database.RootGroup, string.Empty, found, Bin());
        return found;
    }

    private static void CollectPasswordHealth(PwGroup group, string groupPath, List<EntryPasswordHealth> found, PwGroup? bin, byte[] key)
    {
        foreach (PwEntry entry in group.Entries)
        {
            var (bits, digest) = Rate(entry.Strings.GetSafe(PwDefs.PasswordField), key);

            found.Add(new EntryPasswordHealth(
                new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)) { Uuid = entry.Uuid.ToHexString() },
                entry.Uuid.ToHexString(),
                bits,
                digest,
                entry.Expires ? new DateTimeOffset(TimeUtil.ToUtc(entry.ExpiryTime, true)) : null,
                !entry.QualityCheck || entry.CustomData.Get(_excludedFromReports) == "true"));
        }

        foreach (PwGroup child in group.Groups)
        {
            var childPath = ChildPath(groupPath, child.Name);

            if (!ReferenceEquals(child, bin) && !ReservedGroups.IsReserved(childPath))
            {
                CollectPasswordHealth(child, childPath, found, bin, key);
            }
        }
    }

    private static void CollectReminderFacts(PwGroup group, string groupPath, List<EntryReminderFacts> found, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            var needsUpdate = HasTag(entry, ItemChangeRules.NeedsUpdateTag);

            found.Add(new EntryReminderFacts(
                new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)) { Uuid = entry.Uuid.ToHexString() },
                entry.Uuid.ToHexString(),
                ReadField(entry, PwDefs.UrlField),
                KindOf(entry),
                entry.Expires ? new DateTimeOffset(TimeUtil.ToUtc(entry.ExpiryTime, true)) : null,
                !entry.QualityCheck || entry.CustomData.Get(_excludedFromReports) == "true",
                needsUpdate,
                needsUpdate ? TaggedSince(entry, ItemChangeRules.NeedsUpdateTag) : null));
        }

        foreach (PwGroup child in group.Groups)
        {
            var childPath = ChildPath(groupPath, child.Name);

            if (!ReferenceEquals(child, bin) && !ReservedGroups.IsReserved(childPath))
            {
                CollectReminderFacts(child, childPath, found, bin);
            }
        }
    }

    /// <summary>KeePass's estimate of a password and its HMAC under <paramref name="key"/>, or nulls when it is empty or holds a placeholder; one longer than <see cref="_longestRated"/> has a digest and no estimate.</summary>
    /// <remarks>The UTF-8 copy is zeroed before this returns, and the estimator zeroes its own.</remarks>
    private static (uint? Bits, string? Digest) Rate(ProtectedString password, byte[] key)
    {
        if (password.IsEmpty)
        {
            return (null, null);
        }

        byte[] utf8 = password.ReadUtf8();

        try
        {
            if (utf8.AsSpan().Contains((byte)'{') && KeePassPlaceholders.HoldsPlaceholder(Encoding.UTF8.GetString(utf8)))
            {
                return (null, null);
            }

            uint? bits = Encoding.UTF8.GetCharCount(utf8) <= _longestRated ? QualityEstimation.EstimatePasswordBits(utf8) : null;

            return (bits, Convert.ToHexString(HMACSHA256.HashData(key, utf8)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static bool LoadCommonPasswords()
    {
        PopularPasswords.Add(CommonPasswords.Read(), bGZipped: false);
        return true;
    }

    private static void CollectLostProjectTags(PwGroup group, string groupPath, List<LostProjectTag> lost, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            if (entry.History.UCount > 0)
            {
                LostProjectTags(entry, new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)) { Uuid = entry.Uuid.ToHexString() }, lost);
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectLostProjectTags(child, ChildPath(groupPath, child.Name), lost, bin);
            }
        }
    }

    /// <summary>Each membership a version held and the current one lacks, when the version after the last one holding it changed more than its tags.</summary>
    private static void LostProjectTags(PwEntry entry, EntryName name, List<LostProjectTag> lost)
    {
        PwEntry[] versions = [.. Enumerable.Reverse(HistoryNewestFirst(entry)).Select(position => entry.History.GetAt(position)), entry];
        var current = Memberships(TagsOf(entry));
        Dictionary<(string Project, string Environment), (int Held, string Tag, bool Protects)> dropped = [];

        for (var version = 0; version < versions.Length - 1; version++)
        {
            foreach (var tag in TagsOf(versions[version]))
            {
                if (ProjectTag.Read(tag) is { Kind: ProjectTagKind.Member, Project: { } project, Environment: { } environment } read
                    && !current.Contains((project, environment)))
                {
                    dropped[(project, environment)] = (version, tag, read.Protects);
                }
            }
        }

        foreach (var ((project, environment), (held, tag, protects)) in dropped)
        {
            var after = versions[held + 1];

            if (ChangedMoreThanTags(versions[held], after))
            {
                lost.Add(new LostProjectTag(name, entry.Uuid.ToHexString(), tag, project, environment, protects, after.LastModificationTime.ToUniversalTime()));
            }
        }
    }
}
