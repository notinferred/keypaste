using Keypaste.Core.Import;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>What keypaste copies in from a vault or an export KeePassXC made is what KeePassXC then reads in keypaste's vault.</summary>
/// <remarks>How an import plans, refuses and reports is <c>KdbxImportTests</c>', <c>ExportImportTests</c>' and the CLI's <c>ImportVerbTests</c>'.</remarks>
public sealed class ImportTests
{
    private static string Uuid(string name) => Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(name.PadRight(16)[..16]));

    [Fact]
    public void KeePassXC_reads_a_copied_entry_with_its_attribute_attachment_and_history_and_nothing_from_the_recycle_bin()
    {
        using var file = new KeePassXcVault();
        var source = Path.Combine(file.Directory_, "a.kdbx");
        var seed = Path.Combine(file.Directory_, "seed.xml");
        File.WriteAllText(seed, $"""
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <KeePassFile>
              <Meta><Generator>keypaste-tests</Generator><DatabaseName>a</DatabaseName></Meta>
              <Root><Group><UUID>{Uuid("root")}</UUID><Name>Root</Name>
                <Group><UUID>{Uuid("banking")}</UUID><Name>Banking</Name>
                  <Entry><UUID>{Uuid("checking")}</UUID>
                    <String><Key>Title</Key><Value>checking</Value></String>
                    <String><Key>Password</Key><Value ProtectInMemory="True">import-pw-2</Value></String>
                    <String><Key>account-number</Key><Value ProtectInMemory="True">import-attribute-value</Value></String>
                    <History><Entry><UUID>{Uuid("checking")}</UUID>
                      <String><Key>Title</Key><Value>checking</Value></String>
                      <String><Key>Password</Key><Value ProtectInMemory="True">import-pw-1</Value></String>
                    </Entry></History>
                  </Entry>
                  <Entry><UUID>{Uuid("doomed")}</UUID>
                    <String><Key>Title</Key><Value>doomed</Value></String>
                    <String><Key>Password</Key><Value ProtectInMemory="True">doomed-pw</Value></String>
                  </Entry>
                </Group>
                <Group><UUID>{Uuid("env")}</UUID><Name>env</Name>
                  <Group><UUID>{Uuid("acme")}</UUID><Name>acme</Name>
                    <Entry><UUID>{Uuid("oldkey")}</UUID>
                      <String><Key>Title</Key><Value>OLD_KEY</Value></String>
                      <String><Key>Password</Key><Value ProtectInMemory="True">import-old-key</Value></String>
                    </Entry>
                  </Group>
                </Group>
              </Group></Root>
            </KeePassFile>
            """);
        var statement = Path.Combine(file.Directory_, "statement.bin");
        File.WriteAllBytes(statement, System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096));

        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", seed, source]).Exit);
        KeePassXcCli.Run("attachment-import", source, "Banking/checking", "statement.bin", statement);
        KeePassXcCli.Run("rm", source, "Banking/doomed");
        var sourceBytes = File.ReadAllBytes(source);

        using (var vault = file.Create())
        using (var opened = KdbxImport.Open(source, KeePassXcCli.Password, null))
        {
            opened.ApplyTo(vault, opened.DefaultPlan(vault, "moved"));
            vault.Save();
        }

        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.Contains("moved/Banking/checking", tree);
        // An env group KeePassXC made is a plain group once copied, which no project reads.
        Assert.Contains("moved/env/acme/OLD_KEY", tree);
        Assert.DoesNotContain(tree, line => line.Contains("doomed", StringComparison.Ordinal) || line.Contains("Recycle Bin", StringComparison.Ordinal));
        Assert.Equal(["import-pw-2", "import-attribute-value"], KeePassXcCli.Show(file.Path_, "moved/Banking/checking", "Password", "account-number"));

        var copied = Path.Combine(file.Directory_, "statement.out");
        KeePassXcCli.Run("attachment-export", file.Path_, "moved/Banking/checking", "statement.bin", copied);
        Assert.Equal(File.ReadAllBytes(statement), File.ReadAllBytes(copied));

        var checking = KeePassXcCli.Export(file.Path_).Descendants("Entry").First(e => KeePassXcVault.StringValue(e, "Title") == "checking");
        Assert.Contains(checking.Element("History")!.Descendants("Value"), value => value.Value == "import-pw-1");
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
    }

    [Fact]
    public void KeePassXC_reads_back_each_row_of_its_own_CSV_export_but_the_icon_and_what_it_had_recycled()
    {
        using var file = new KeePassXcVault();
        var source = Path.Combine(file.Directory_, "csv.kdbx");
        var seed = Path.Combine(file.Directory_, "csv-seed.xml");
        File.WriteAllText(seed, $"""
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <KeePassFile>
              <Meta><Generator>keypaste-tests</Generator><DatabaseName>csv</DatabaseName></Meta>
              <Root><Group><UUID>{Uuid("csvroot")}</UUID><Name>Root</Name>
                <Entry><UUID>{Uuid("csvtop")}</UUID>
                  <Times><CreationTime>2021-02-03T04:05:06Z</CreationTime><LastModificationTime>2022-03-04T05:06:07Z</LastModificationTime></Times>
                  <String><Key>Title</Key><Value>csv-top</Value></String>
                  <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-top</Value></String>
                </Entry>
                <Entry><UUID>{Uuid("csvdoomed")}</UUID>
                  <String><Key>Title</Key><Value>csv-doomed</Value></String>
                  <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-doomed</Value></String>
                </Entry>
                <Group><UUID>{Uuid("csvwork")}</UUID><Name>Work</Name>
                  <Group><UUID>{Uuid("csvdev")}</UUID><Name>Dev</Name>
                    <Entry><UUID>{Uuid("csvsite")}</UUID><IconID>12</IconID>
                      <Times><CreationTime>2023-04-05T06:07:08Z</CreationTime><LastModificationTime>2024-05-06T07:08:09Z</LastModificationTime></Times>
                      <String><Key>Title</Key><Value>csv-site</Value></String>
                      <String><Key>UserName</Key><Value>csv-user</Value></String>
                      <String><Key>Password</Key><Value ProtectInMemory="True">csv-pw-site</Value></String>
                      <String><Key>URL</Key><Value>https://csv.import.example</Value></String>
                      <String><Key>Notes</Key><Value>csv-notes</Value></String>
                      <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/csv-site:csv-user?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&amp;period=30&amp;digits=8</Value></String>
                    </Entry>
                  </Group>
                </Group>
              </Group></Root>
            </KeePassFile>
            """);
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", seed, source]).Exit);
        KeePassXcCli.Run("rm", source, "csv-doomed");
        var csv = Path.Combine(file.Directory_, "keepassxc.csv");
        var exported = KeePassXcCli.Run("export", source, "-f", "csv");
        File.WriteAllText(csv, exported);

        using (var vault = file.Create())
        using (var opened = ExportImport.Open(csv, ExportImport.Readers)!)
        {
            var plan = opened.Plan(vault, "kpxc-csv");
            Assert.Contains(plan.Losses, loss => loss.ToString()!.Contains("csv-doomed", StringComparison.Ordinal));
            opened.Commit(vault, plan, saving => saving.Save());
        }

        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.Contains("kpxc-csv/csv-top", tree);
        Assert.Contains("kpxc-csv/Work/Dev/csv-site", tree);
        Assert.DoesNotContain(tree, line => line.StartsWith("kpxc-csv/Root", StringComparison.Ordinal) || line.Contains("csv-doomed", StringComparison.Ordinal));

        var readBack = KeePassXcCli.Run("export", file.Path_, "-f", "csv");
        foreach (var title in new[] { "csv-top", "csv-site" })
        {
            Assert.Equal(WithoutGroupOrIcon(Row(exported, title)), WithoutGroupOrIcon(Row(readBack, title)));
        }

        Assert.EndsWith("\"0\",\"2024-05-06T07:08:09Z\",\"2023-04-05T06:07:08Z\"", Row(readBack, "csv-site"), StringComparison.Ordinal);
    }

    [Fact]
    public void KeePassXC_computes_the_codes_keypaste_gives_and_reads_the_addresses_and_passkey_of_a_Bitwarden_export()
    {
        using var file = new KeePassXcVault();
        var export = Path.Combine(file.Directory_, "bitwarden.json");
        File.WriteAllText(export, """
            {
              "encrypted": false,
              "folders": [ { "id": "folder-work", "name": "Work" } ],
              "items": [
                { "id": "item-sha1", "folderId": "folder-work", "type": 1, "name": "Sha1",
                  "login": {
                    "uris": [ { "match": null, "uri": "https://one.import.example" }, { "match": null, "uri": "https://two.import.example" }, { "match": null, "uri": "https://three.import.example" } ],
                    "username": "import-bw-user", "password": "import-bw-pw", "totp": "gezd gnbv gy3t qojq gezd gnbv gy3t qojq",
                    "fido2Credentials": [ {
                      "credentialId": "a3e15f8b-27c1-42ae-90cf-a615ad87854f", "keyType": "public-key", "keyAlgorithm": "ECDSA", "keyCurve": "P-256",
                      "keyValue": "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVSp6ieusvjkLkzaGc7fjGBmwpiuLPxR_d-ZjqMI9L2DKh-takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh",
                      "rpId": "webauthn.io", "userHandle": "aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw", "userName": "KPXC_BITWARDEN",
                      "counter": "0", "rpName": "webauthn.io", "userDisplayName": "KPXC_BITWARDEN", "discoverable": "true" } ] } },
                { "id": "item-sha256", "folderId": "folder-work", "type": 1, "name": "Sha256",
                  "login": { "totp": "otpauth://totp/Sha256?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA&algorithm=SHA256&digits=8" } },
                { "id": "item-steam", "folderId": "folder-work", "type": 1, "name": "Steam", "login": { "totp": "steam://JBSWY3DPEHPK3PXP" } }
              ]
            }
            """);

        using (var vault = file.Create())
        using (var opened = ExportImport.Open(export, ExportImport.Readers)!)
        {
            opened.Commit(vault, opened.Plan(vault, "bitwarden"), saving => saving.Save());
        }

        using (var vault = file.Open())
        {
            var codes = new Dictionary<string, string>();
            foreach (var title in new[] { "Sha1", "Sha256", "Steam" })
            {
                codes[title] = KeePassXcTotp.AssertSameCode(vault, new EntryName("bitwarden/Work", title), file.Path_);
            }

            Assert.NotEqual(codes["Sha1"], codes["Steam"]);
        }

        const string pem = "-----BEGIN PRIVATE KEY-----MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgmr4GQQjerojFuf0ZouOuUllMvAwxZSZAfB6gwDYcLiehRANCAAT0WR5zVSp6ieusvjkLkzaGc7fjGBmwpiuLPxR/d+ZjqMI9L2DKh+takp6wGt2x0n4jzr1KA352NZg0vjZX9CHh-----END PRIVATE KEY-----";
        Assert.Equal(
            ["https://one.import.example", "https://two.import.example", "https://three.import.example", "import-bw-pw",
             "o-FfiyfBQq6Qz6YVrYeFTw", pem, "webauthn.io", "KPXC_BITWARDEN", "aTFtdmFnOHYtS2dxVEJ0by1rSFpLWGg0enlTVC1iUVJReDZ5czJXa3c2aw"],
            KeePassXcCli.Show(
                file.Path_, "bitwarden/Work/Sha1", "URL", "KP2A_URL_1", "KP2A_URL_2", "Password", "KPEX_PASSKEY_CREDENTIAL_ID",
                "KPEX_PASSKEY_PRIVATE_KEY_PEM", "KPEX_PASSKEY_RELYING_PARTY", "KPEX_PASSKEY_USERNAME", "KPEX_PASSKEY_USER_HANDLE"));
    }

    private static string Row(string csv, string title) =>
        csv.Split('\n').Single(line => line.Contains($",\"{title}\",", StringComparison.Ordinal));

    // The group column moves under the import's group, and an icon is not imported.
    private static string WithoutGroupOrIcon(string row)
    {
        var fields = row.Split("\",\"");
        fields[0] = "";
        fields[7] = "";
        return string.Join("\",\"", fields);
    }
}
