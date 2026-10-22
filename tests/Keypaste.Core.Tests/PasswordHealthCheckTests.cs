using System.Reflection;
using KeePassLib;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V.9: weak, reused and expired passwords are found in a saved vault by item, never by value, among
/// live items outside keypaste's own groups only.
/// </summary>
public sealed class PasswordHealthCheckTests : IDisposable
{
    private const string _master = "password-health-master-pw";
    private const string _weak = "hunter2";
    private const string _shared = "k8Fq!2vZ#pL9xR4m@Tw7";
    private const string _strong = "Vq7#tR2p!Lx9@mZ4wK8$bN3e";
    private const string _alsoStrong = "Jd4%nW8s^Hc2*Ty6&Qb9";

    private static readonly DateTimeOffset _now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _elsewhere = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly EntryName _mail = new("services", "Mail");
    private static readonly EntryName _forum = new("services", "Forum");
    private static readonly EntryName _shop = new("services", "Shop");
    private static readonly EntryName _wiki = new("services", "Wiki");
    private static readonly EntryName _cloud = new("services", "Cloud");
    private static readonly EntryName _bank = new("services", "Bank");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-password-health-tests-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Weak_reused_and_expired_items_are_found_by_item_and_nothing_else_is()
    {
        Seed(created =>
        {
            Add(created, _mail, _weak);
            Add(created, _forum, _shared);
            Add(created, _shop, _shared);
            Add(created, _wiki, _shared);
            Add(created, _cloud, _strong);
            created.SetExpiryUnchecked(_cloud, _now.AddDays(-1));
            Add(created, _bank, _alsoStrong);
            created.SetExpiryUnchecked(_bank, _now.AddDays(30));
            Add(created, new EntryName("services", "Blank"), string.Empty);
        });

        using var vault = Vault.Open(VaultPath, _master);
        var findings = PasswordHealthCheck.Scan(vault, _now);

        Assert.Equal(
            [(_mail, PasswordProblem.Weak), (_forum, PasswordProblem.Reused), (_shop, PasswordProblem.Reused), (_wiki, PasswordProblem.Reused), (_cloud, PasswordProblem.Expired)],
            findings.Select(finding => (finding.Entry, finding.Problem)));
        Assert.Equal([_shop, _wiki], findings[1].SharedWith);
        Assert.Equal([_forum, _wiki], findings[2].SharedWith);
        Assert.Equal([_forum, _shop], findings[3].SharedWith);
        Assert.Empty(findings[0].SharedWith);
        Assert.Equal(_now.AddDays(-1), findings[4].ExpiredUtc);
        Assert.Equal(["weak", "reused", "reused", "reused", "expired@2029-12-31T09:00:00.0000000Z"], findings.Select(finding => finding.Key));
        Assert.All(findings, finding => Assert.Equal(vault.EntryUuid(finding.Entry), finding.EntryUuid));
    }

    [Fact]
    public void A_password_too_long_to_rate_is_not_weak_and_its_reuse_is_still_found()
    {
        var repeated = new string('a', 8 * 1024);

        Seed(created =>
        {
            Add(created, _forum, repeated);
            Add(created, _shop, repeated);
            Add(created, _mail, new string('b', 257));
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Equal(
            [(_forum, PasswordProblem.Reused), (_shop, PasswordProblem.Reused)],
            PasswordHealthCheck.Scan(vault, _now).Select(finding => (finding.Entry, finding.Problem)));
    }

    /// <summary>
    /// A password on the common-password list is weak however long it is, while one as long, of the same
    /// kinds of character and on no list, is not (D-0499).
    /// </summary>
    /// <remarks>
    /// The first two rate at about 80 and 117 bits by their characters alone, so without the list neither
    /// would be listed; the third is the control that length and character kinds are not what decided it.
    /// </remarks>
    [Fact]
    public void A_password_on_the_common_list_is_weak_however_long_it_is()
    {
        Seed(created =>
        {
            Add(created, _mail, "films+pic+galeries");
            Add(created, _forum, "washingtoncaliforniamanchester");
            Add(created, _shop, "qzvkx+wpt+jhrmybnd");
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Equal(
            [(_mail, PasswordProblem.Weak), (_forum, PasswordProblem.Weak)],
            PasswordHealthCheck.Scan(vault, _now).Select(finding => (finding.Entry, finding.Problem)));
    }

    [Fact]
    public void An_expiry_at_the_moment_of_the_check_has_passed_and_one_a_second_later_has_not()
    {
        Seed(created =>
        {
            Add(created, _cloud, _strong);
            created.SetExpiryUnchecked(_cloud, _now);
            Add(created, _bank, _alsoStrong);
            created.SetExpiryUnchecked(_bank, _now.AddSeconds(1));
        });

        using var vault = Vault.Open(VaultPath, _master);

        var expired = Assert.Single(PasswordHealthCheck.Scan(vault, _now));
        Assert.Equal((_cloud, PasswordProblem.Expired), (expired.Entry, expired.Problem));
    }

    [Fact]
    public void Items_in_the_recycle_bin_or_keypastes_own_groups_are_neither_reported_nor_compared()
    {
        Seed(created =>
        {
            Add(created, _forum, _shared);
            Add(created, new EntryName(".keypaste/tokens", "planted"), _shared);
            Add(created, new EntryName(".keypaste", "weak"), _weak);
            Add(created, new EntryName("services", "Deleted"), _shared);
            Add(created, new EntryName("services", "Old"), _weak);
            created.RemoveEntry(new EntryName("services", "Deleted"), out _);
            created.RemoveEntry(new EntryName("services", "Old"), out _);
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Empty(PasswordHealthCheck.Scan(vault, _now));
    }

    [Fact]
    public void A_password_holding_a_placeholder_is_neither_rated_nor_compared_and_other_braces_are_rated()
    {
        const string Reference = "{REF:P@I:0123456789ABCDEF0123456789ABCDEF}";

        Seed(created =>
        {
            Add(created, _forum, Reference);
            Add(created, _shop, Reference);
            Add(created, _mail, "{username}");
            Add(created, _wiki, "{ab}");
            Add(created, _bank, @"\{ab\}");
        });

        using var vault = Vault.Open(VaultPath, _master);

        var findings = PasswordHealthCheck.Scan(vault, _now);
        Assert.Equal([_wiki, _bank], findings.Select(finding => finding.Entry));
        Assert.All(findings, finding => Assert.Equal(PasswordProblem.Weak, finding.Problem));
    }

    [Fact]
    public void Items_left_out_of_quality_checks_are_not_reported_but_still_share_a_password()
    {
        Seed(created =>
        {
            Add(created, _mail, _weak);
            Add(created, _wiki, _weak + "!");
            Add(created, _forum, _shared);
            Add(created, _shop, _shared);
            Add(created, _cloud, _strong);
            created.SetExpiryUnchecked(_cloud, _now.AddDays(-1));
        });

        EditElsewhere(_mail.Title, entry => entry.QualityCheck = false);
        EditElsewhere(_forum.Title, entry => entry.QualityCheck = false);
        EditElsewhere(_cloud.Title, entry => entry.QualityCheck = false);
        EditElsewhere(_wiki.Title, entry => entry.CustomData.Set("KnownBad", "true"));

        using var vault = Vault.Open(VaultPath, _master);

        var reused = Assert.Single(PasswordHealthCheck.Scan(vault, _now));
        Assert.Equal((_shop, PasswordProblem.Reused), (reused.Entry, reused.Problem));
        Assert.Equal([_forum], reused.SharedWith);
    }

    [Fact]
    public void Keypastes_own_passwords_and_passphrases_at_their_defaults_are_never_weak()
    {
        Seed(created =>
        {
            foreach (var name in new[] { "a", "b", "c", "d", "e" })
            {
                Add(created, new EntryName("password", name), Generated(buffer => PasswordGenerator.Append(PasswordRecipe.Default, buffer)));
                Add(created, new EntryName("passphrase", name), Generated(buffer => PasswordGenerator.Append(PassphraseRecipe.Default, buffer)));
            }
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Empty(PasswordHealthCheck.Scan(vault, _now));
    }

    [Fact]
    public void A_digest_compares_passwords_within_one_read_and_matches_nothing_from_another()
    {
        Seed(created =>
        {
            Add(created, _forum, _shared);
            Add(created, _shop, _shared);
        });

        using var vault = Vault.Open(VaultPath, _master);
        var first = vault.ReadPasswordHealth();
        var second = vault.ReadPasswordHealth();

        Assert.NotNull(first[0].Digest);
        Assert.Equal(first[0].Digest, first[1].Digest);
        Assert.NotEqual(first[0].Digest, second[0].Digest);
        Assert.Equal("Forum", first[0].ToString());
    }

    [Fact]
    public void A_finding_holds_no_value_in_any_public_member_or_its_text()
    {
        Seed(created =>
        {
            Add(created, _mail, _weak);
            Add(created, _forum, _shared);
            Add(created, _shop, _shared);
            Add(created, _cloud, _strong);
            created.SetExpiryUnchecked(_cloud, _now.AddDays(-1));
        });

        using var vault = Vault.Open(VaultPath, _master);
        var findings = PasswordHealthCheck.Scan(vault, _now);
        string[] values = [_weak, _shared, _strong];

        Assert.Equal(4, findings.Count);

        foreach (var finding in findings)
        {
            var shown = typeof(PasswordFinding)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.GetValue(finding)?.ToString() ?? string.Empty)
                .Concat(finding.SharedWith.Select(name => name.ToString()))
                .Append(finding.ToString());

            Assert.All(shown, text => Assert.DoesNotContain(values, value => text.Contains(value, StringComparison.Ordinal)));
        }
    }

    private void Seed(Action<Vault> fill)
    {
        using var vault = Vault.Create(VaultPath, _master);
        fill(vault);
        vault.Save();
    }

    private void EditElsewhere(string title, Action<PwEntry> edit) =>
        ForeignEdit.Apply(VaultPath, _master, title, _elsewhere, edit);

    private static void Add(Vault vault, EntryName name, string password) =>
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = password });

    private static string Generated(Action<SecretBuffer> append)
    {
        using var buffer = new SecretBuffer();
        append(buffer);
        return buffer.Value.ToString();
    }
}
