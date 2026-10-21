using System.Globalization;

namespace Keypaste.Core.Recommendations;

/// <summary>What is wrong with an item's password, or with the item, that Recommendations reports (V.9).</summary>
public enum PasswordProblem
{
    /// <summary>KeePass's estimate rates the password below <see cref="PasswordHealthCheck.StrongBits"/>.</summary>
    Weak,

    /// <summary>Another item holds the same password.</summary>
    Reused,

    /// <summary>The item's expiry date is at or before the time of the check.</summary>
    Expired,
}

/// <summary>One item whose password is weak or reused, or that has expired, named by item and never by value (V.9).</summary>
/// <param name="Entry">The item.</param>
/// <param name="EntryUuid">The item's KDBX identifier as hex, which survives a rename.</param>
/// <param name="Problem">What is wrong.</param>
/// <param name="SharedWith">The other items holding the same password, for <see cref="PasswordProblem.Reused"/>; otherwise empty.</param>
/// <param name="ExpiredUtc">When the item expired, for <see cref="PasswordProblem.Expired"/>; otherwise null.</param>
public sealed record PasswordFinding(
    EntryName Entry,
    string EntryUuid,
    PasswordProblem Problem,
    IReadOnlyList<EntryName> SharedWith,
    DateTimeOffset? ExpiredUtc)
{
    /// <summary>What a dismissal remembers: the problem, and for an expiry when it was, so a new expiry that passes is listed again.</summary>
    public string Key => Problem switch
    {
        PasswordProblem.Weak => "weak",
        PasswordProblem.Reused => "reused",
        _ => string.Create(CultureInfo.InvariantCulture, $"expired@{ExpiredUtc?.UtcDateTime:O}"),
    };
}

/// <summary>Finds weak, reused and expired passwords in the process holding the vault, with no network request (V.9).</summary>
/// <remarks>
/// <para>
/// It reads the password of every live item outside keypaste's own groups. KeePass's own estimator
/// rates each, and passwords are compared by an HMAC under a key drawn for that one read and zeroed
/// after it, so no value and no digest that could confirm a guess outlives the check. Custom fields
/// are not read. An empty password, or one holding a placeholder such as <c>{REF:…}</c>, whose text
/// is not the password it fills in to, is neither rated nor compared.
/// </para>
/// <para>
/// An item KeePass or KeePassXC leaves out of quality checks and reports is not reported, though its
/// password still counts as reused by another item holding it.
/// </para>
/// </remarks>
public static class PasswordHealthCheck
{
    /// <summary>The fewest bits KeePass's estimate gives a password that is not weak.</summary>
    public const uint StrongBits = 64;

    /// <summary>Checks every live item outside keypaste's own groups.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <param name="now">The time an expiry is compared with.</param>
    /// <returns>The findings, by item in tree order and then by problem.</returns>
    public static IReadOnlyList<PasswordFinding> Scan(Vault vault, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vault);

        var items = vault.ReadPasswordHealth();
        var holders = items.Where(item => item.Digest is not null).ToLookup(item => item.Digest!, StringComparer.Ordinal);
        List<PasswordFinding> findings = [];

        foreach (var item in items.Where(item => !item.Excluded))
        {
            if (item.Bits < StrongBits)
            {
                findings.Add(new PasswordFinding(item.Entry, item.Uuid, PasswordProblem.Weak, [], null));
            }

            if (item.Digest is { } digest
                && holders[digest].Where(other => !ReferenceEquals(other, item)).Select(other => other.Entry).ToList() is { Count: > 0 } others)
            {
                findings.Add(new PasswordFinding(item.Entry, item.Uuid, PasswordProblem.Reused, others, null));
            }

            if (item.Expires is { } expires && expires <= now)
            {
                findings.Add(new PasswordFinding(item.Entry, item.Uuid, PasswordProblem.Expired, [], expires));
            }
        }

        return findings;
    }
}

/// <summary>One live item's password rating and expiry, as the check reads them.</summary>
/// <param name="Entry">The item.</param>
/// <param name="Uuid">Its KDBX identifier as hex.</param>
/// <param name="Bits">KeePass's estimate of the password, or null when it is empty or holds a placeholder.</param>
/// <param name="Digest">An HMAC of the password that only equal passwords in the same read share, or null as for <paramref name="Bits"/>.</param>
/// <param name="Expires">When the item expires, or null when it does not.</param>
/// <param name="Excluded">Whether the item is left out of quality checks and reports.</param>
internal sealed record EntryPasswordHealth(EntryName Entry, string Uuid, uint? Bits, string? Digest, DateTimeOffset? Expires, bool Excluded)
{
    public override string ToString() => Entry.Title;
}
