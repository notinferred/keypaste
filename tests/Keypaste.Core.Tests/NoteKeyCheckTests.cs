using System.Reflection;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// C.2: keys left in notes are found in a saved vault by entry and key, never by value.
/// </summary>
public sealed class NoteKeyCheckTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    internal static readonly string GitHubToken = "ghp_" + string.Concat(Enumerable.Repeat("kp2c", 9));

    internal static readonly string FixtureNotes = string.Join(
        "\n",
        "STRIPE_SECRET_KEY=sk_test_1",
        "export OPENAI_API_KEY=sk-proj-2",
        GitHubToken,
        "-----BEGIN PRIVATE KEY-----",
        "API_KEY=inside-the-block",
        "AKIAABCDEFGHIJKLMNOP",
        "-----END PRIVATE KEY-----",
        "Recovery codes are in the safe.");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-notekey-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_fixture_gives_two_keys_and_a_token_and_a_sentence_gives_nothing()
    {
        var path = Saved(("services", "Stripe", FixtureNotes), ("services", "Plain", "Recovery codes are in the safe."));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);

        Assert.Equal(
            [("STRIPE_SECRET_KEY", NoteKeyKind.Assignment, 0), ("OPENAI_API_KEY", NoteKeyKind.Assignment, 1), ("GITHUB_TOKEN", NoteKeyKind.Token, 2)],
            findings.Select(finding => (finding.Field, finding.Kind, finding.Line)));
        Assert.All(findings, finding => Assert.Equal(new EntryName("services", "Stripe"), finding.Entry));
        Assert.Equal("GitHub token", findings[2].TokenKind);
        Assert.All(findings, finding => Assert.False(finding.IsRepeated));
        Assert.Equal(vault.EntryUuid(new EntryName("services", "Stripe")), findings[0].EntryUuid);
    }

    [Fact]
    public void Each_line_rule_decides_what_is_found()
    {
        (string Title, string Notes, string[] Fields)[] cases =
        [
            ("plain", "KEY=v", ["KEY"]),
            ("export-comment", "  export\tKEY = v  # the key", ["KEY"]),
            ("double-quoted", "KEY=\"quoted value\"", ["KEY"]),
            ("single-quoted", "KEY='single'", ["KEY"]),
            ("backtick", "KEY=`tick`", ["KEY"]),
            ("unbalanced", "KEY=\"unbalanced", []),
            ("after-quote", "KEY=\"a\" trailing", []),
            ("empty", "KEY=", []),
            ("empty-quotes", "KEY=\"\"", []),
            ("prose", "TODO=call Bob", []),
            ("quote-inside", "KEY=a\"b", []),
            ("lowercase", "key=lower", []),
            ("standard", "PASSWORD=hunter2", []),
            ("keepassxc", "KPXC_SETTING=1", []),
            ("too-long", new string('A', 129) + "=v", []),
            ("glued-export", "exportKEY=v", []),
            ("token-in-sentence", "Use " + GitHubToken + " for CI", []),
            ("short-token", "ghp_short", []),
            ("aws", "AKIAABCDEFGHIJKLMNOP", ["AWS_ACCESS_KEY_ID"]),
            ("aws-lowercase", "AKIAabcdefghijklmnop", []),
            ("anthropic", "sk-ant-api03-abcdefghijklmnop", ["ANTHROPIC_API_KEY"]),
            ("slack", "xoxb-1234567890-abcdefghij", ["SLACK_TOKEN"]),
            ("key-holding-token", "GITHUB=" + GitHubToken, ["GITHUB"]),
            ("unterminated-pem", "-----BEGIN KEY-----\nKEY=v", []),
        ];

        var path = Saved([.. cases.Select(c => ("rules", c.Title, c.Notes))]);

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var found = check.Scan(vault).ToLookup(finding => finding.Entry.Title, finding => finding.Field);

        Assert.All(cases, c => Assert.True(c.Fields.SequenceEqual(found[c.Title]), $"{c.Title}: found [{string.Join(", ", found[c.Title])}]"));
    }

    [Fact]
    public void A_field_set_on_two_lines_is_found_twice_and_marked_repeated()
    {
        var path = Saved(("rules", "twice", "KEY=a\nKEY=b\nOTHER=c"));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);

        Assert.Equal([("KEY", true), ("KEY", true), ("OTHER", false)], findings.Select(finding => (finding.Field, finding.IsRepeated)));
    }

    [Fact]
    public void Entries_in_the_recycle_bin_or_keypastes_own_groups_are_not_read()
    {
        var path = Path.Combine(_directory, "bin.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = ".keypaste/tokens", Title = "planted", Notes = "KEY=planted" });
            vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Deleted", Notes = "KEY=deleted" });
            vault.RemoveEntry(new EntryName("services", "Deleted"), out _);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();

        Assert.Empty(check.Scan(reopened));
    }

    [Fact]
    public void A_finding_holds_no_value_in_any_public_member_or_its_text()
    {
        var path = Saved(("services", "Stripe", FixtureNotes));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);
        string[] values = ["sk_test_1", "sk-proj-2", GitHubToken];

        foreach (var finding in findings)
        {
            var shown = typeof(NoteKeyFinding)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.GetValue(finding)?.ToString() ?? string.Empty)
                .Append(finding.ToString());

            Assert.All(shown, text => Assert.DoesNotContain(values, value => text.Contains(value, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Search_still_never_reads_notes()
    {
        var path = Saved(("services", "Stripe", FixtureNotes));

        using var vault = Vault.Open(path, _master);

        Assert.Empty(vault.Search("sk_test_1"));
        Assert.Empty(vault.Search("STRIPE_SECRET_KEY"));
    }

    private string Saved(params (string Group, string Title, string Notes)[] entries)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using var vault = Vault.Create(path, _master);

        foreach (var (group, title, notes) in entries)
        {
            vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = "p", Notes = notes });
        }

        vault.Save();
        return path;
    }
}
