namespace Keypaste.Core;

/// <summary>
/// One earlier state of an entry, as KeePass history kept it.
/// </summary>
/// <remarks>
/// <para>
/// A revision is values to show and to restore, not a name to look up. Its
/// <see cref="VaultEntry.Title"/> is the one that revision carried, so
/// <see cref="VaultEntry.Path"/> on it can address nothing — KeePassXC can rename an entry, and the
/// revisions either side of that rename answer to different paths while remaining one entry.
/// <see cref="VaultEntry.GroupPath"/> is the entry's current group, because a history item in a
/// file that has been reopened has no group of its own.
/// </para>
/// <para>
/// <see cref="Fields"/> is a <see cref="VaultEntry"/> rather than five properties of its own so that
/// a revision and a current entry are read through one code path: whatever protection applies to a
/// current entry's password applies here, by construction rather than by remembering to.
/// </para>
/// <para>
/// It carries the secret in a managed string, as <see cref="VaultEntry"/> does and for the same
/// reason. A caller should not keep one beyond the operation that needed it — which for a screen
/// showing a list of revisions means clearing them when the vault locks.
/// </para>
/// </remarks>
/// <param name="Index">
/// This revision's position in <see cref="Vault.ReadHistory"/>'s answer, newest first, and the index
/// <see cref="Vault.RestoreRevision"/> takes. It is an ordinal within one reading and not an
/// identifier: the next change to the entry renumbers every revision, so it must not be stored.
/// </param>
/// <param name="ModifiedUtc">When this revision was last modified, in UTC.</param>
/// <param name="Fields">The values this revision held.</param>
public sealed record EntryRevision(int Index, DateTime ModifiedUtc, VaultEntry Fields);
