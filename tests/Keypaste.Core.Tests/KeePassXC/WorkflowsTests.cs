using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>
/// Every supported workflow on a vault KeePassXC made leaves KeePassXC everything keypaste does not model: meta, group and entry
/// CustomData, custom strings, its own otp, tags, Auto-Type settings, KeePassXC-Browser's pairing, attachments, and the cipher and KDF
/// KeePassXC chose. FEATURES states KeePassXC compatibility from this class.
/// </summary>
/// <remarks>
/// keypaste's writes go through the core calls the CLI and the app's screens make. Refusals, and the sequences Auto-Type types, are
/// tested where those calls are; here every write is followed by KeePassXC reading the whole vault back.
/// </remarks>
public sealed class WorkflowsTests
{
    private static readonly string[] _markers =
    [
        "kp94-meta-value", "kp94-group-value", "kp94-entry-value", "kp94-plain-value", "kp94-secret-value", "kp94-window", "kp94-tag",
        "KP7BOTPJBSWY3DPE", "kp4a1-group", "kp4a1-item", "kp4a1-regex", "kp4a1-placeholder", "kp4a1-title",
    ];

    public static TheoryData<string> Kinds => ["password", "password and keyfile", "keyfile"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_write_keypaste_makes_leaves_KeePassXC_what_keypaste_does_not_model(string kind)
    {
        using var file = new KeePassXcVault();
        var keyfile = kind == "password" ? null : Keyfile(file, "made.keyx");
        var key = new KeePassXcKey(kind == "keyfile" ? null : KeePassXcCli.Password, keyfile);
        var fixture = Import(file, key);
        var original = File.ReadAllBytes(file.Path_);
        var database = new EntryName("servers", "database");

        using (var vault = Open(file, key))
        {
            var found = vault.Find(database)!;
            Assert.True(vault.UpdateEntry(database, found with { Notes = "kp94 notes", Url = "https://kp94.example" }));
            vault.Save();
        }

        fixture.AssertIntact("servers/database");
        Assert.Equal(original, File.ReadAllBytes(VaultBackups.List(file.Path_)[^1].Path));
        Assert.Equal(["kp94 notes", "https://kp94.example"], fixture.Show("servers/database", "Notes", "URL"));

        using (var vault = Open(file, key))
        {
            var oldest = vault.ReadHistory(database)!.Count - 1;
            Assert.True(vault.RestoreRevision(database, oldest));
            vault.Save();
        }

        // The revision KeePassXC wrote predates the fields it added since, so restoring it whole drops them.
        Assert.Equal(["pw-1"], fixture.Show("servers/database", "Password"));
        Assert.Null(KeePassXcVault.StringValue(KeePassXcVault.Entry(fixture.Current(), "database"), "kp94-secret"));

        using (var vault = Open(file, key))
        {
            Assert.True(vault.RestoreRevision(database, 0));
            vault.Save();
        }

        Assert.Equal(["pw-2", "kp94-secret-value"], fixture.Show("servers/database", "Password", "kp94-secret"));
        fixture.AssertIntact("servers/database");

        using (var vault = Open(file, key))
        {
            Assert.True(vault.SetFields(database, [new FieldWrite("kp7b-added", "kp7b-added-value"), new FieldWrite("kp7b-change", "kp7b-changed")]));
            Assert.True(vault.SetFields(database, [new FieldWrite("kp7b-change", null, Protect: true)]));
            Assert.True(vault.RemoveField(database, "kp7b-remove"));
            Assert.True(vault.AddTag(database, "env:kp94:prod"));
            vault.Save();
        }

        Assert.Equal(["pw-2", "kp7b-added-value", "kp7b-changed"], fixture.Show("servers/database", "Password", "kp7b-added", "kp7b-change"));
        var entry = KeePassXcVault.Entry(fixture.Current(), "database");
        Assert.True(KeePassXcVault.IsProtected(entry, "kp7b-added"));
        Assert.True(KeePassXcVault.IsProtected(entry, "kp7b-change"));
        Assert.Null(KeePassXcVault.StringValue(entry, "kp7b-remove"));
        Assert.Contains("env:kp94:prod", fixture.Show("servers/database", "Tags")[0].Split(','));
        fixture.AssertIntact("servers/database");

        using (var vault = Open(file, key))
        {
            Assert.True(vault.RemoveTags(database, ["env:kp94:prod"]));
            Assert.True(vault.SetTotp(new EntryName("servers", "cache"), "JBSWY3DPEHPK3PXP"));
            vault.Save();
            KeePassXcTotp.AssertSameCode(vault, new EntryName("servers", "cache"), file.Path_, key);
        }

        Assert.DoesNotContain("env:kp94:prod", fixture.Show("servers/database", "Tags")[0].Split(','));
        Assert.True(KeePassXcVault.IsProtected(KeePassXcVault.Entry(fixture.Current(), "cache"), "otp"));
        fixture.AssertIntact("servers/database");

        using (var vault = Open(file, key))
        {
            Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("servers", "hosts", out _));
            Assert.Equal(OrganizeOutcome.RenamedAndMoved, vault.Relocate(new EntryName("hosts", "database"), new EntryName("archive", "database-old"), out _));
            vault.Save();
        }

        Assert.Equal(["pw-2"], fixture.Show("archive/database-old", "Password"));
        fixture.AssertIntact("archive/database-old");

        using (var vault = Open(file, key))
        {
            vault.RemoveEntry(new EntryName("archive", "database-old"), out var recycled);
            vault.Save();
            Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(recycled));
            vault.Save();
        }

        fixture.AssertIntact("archive/database-old");

        var keptKeePassXcBytes = VaultBackups.List(file.Path_)[^1];
        VaultBackups.Restore(VaultBackups.Inspect(file.Path_, keptKeePassXcBytes, key.Password ?? "", key.Keyfile));
        fixture.AssertIntact("servers/database");
        using (var vault = Open(file, key))
        {
            vault.ExportTo(Path.Combine(file.Directory_, "export.kdbx"));
        }

        Assert.Equal(File.ReadAllBytes(file.Path_), File.ReadAllBytes(Path.Combine(file.Directory_, "export.kdbx")));

        var next = Keyfile(file, "next.keyx");
        using (var vault = Open(file, key))
        {
            var change = new VaultAccessChange(SetPassword: false, AccessKeyfileChange.Attach, next);
            Assert.Equal(VaultAccessOutcome.Changed, vault.ChangeAccess(change, [], []).Outcome);
        }

        var changed = key with { Keyfile = next };
        fixture.Key = changed;
        fixture.AssertIntact("servers/database");
        Assert.NotEqual(0, KeePassXcCli.Try(key.Password ?? "", ["ls", "-q", .. key.Arguments, file.Path_]).Exit);
    }

    [Fact]
    public void KeePassXC_reads_each_template_keypaste_makes_whole_with_no_revision()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.CreateGroup("", "servers", out _);
            foreach (var kind in Enum.GetValues<ItemKind>())
            {
                vault.CreateEntry(new VaultEntry { GroupPath = "servers", Title = $"n4-{kind}", Kind = kind, Password = $"{kind}-pw" }, [], []);
            }

            vault.Save();
        }

        var export = KeePassXcCli.Export(file.Path_);
        foreach (var kind in Enum.GetValues<ItemKind>())
        {
            var entry = export.Descendants("Entry").Single(e => KeePassXcVault.StringValue(e, "Title") == $"n4-{kind}");
            Assert.Equal($"{kind}-pw", KeePassXcVault.StringValue(entry, "Password"));
            Assert.Null(entry.Element("History")?.Element("Entry"));
        }
    }

    [Fact]
    public void A_KDBX_31_vault_keypaste_saves_becomes_KDBX_4_with_KeePassXCs_cipher_and_the_31_file_as_its_backup()
    {
        using var file = new KeePassXcVault();
        var seed = Path.Combine(file.Directory_, "plain.xml");
        File.WriteAllText(seed, """
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <KeePassFile><Meta><Generator>keypaste-tests</Generator></Meta><Root><Group><UUID>AAAAAAAAAAAAAAAAAAAAAQ==</UUID><Name>Root</Name>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAAg==</UUID><String><Key>Title</Key><Value>old</Value></String>
            <String><Key>Password</Key><Value ProtectInMemory="True">old-pw</Value></String></Entry>
            </Group></Root></KeePassFile>
            """);
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", seed, file.Path_]).Exit);
        Assert.Equal(3, File.ReadAllBytes(file.Path_)[10]);
        var cipher = Cipher(KeePassXcCli.Run("db-info", file.Path_));
        var kdbx31 = File.ReadAllBytes(file.Path_);

        using (var vault = file.Open())
        {
            Assert.True(vault.SetFields(new EntryName("", "old"), [new FieldWrite("added", "added-value")]));
            vault.Save();
        }

        Assert.Equal(4, File.ReadAllBytes(file.Path_)[10]);
        Assert.Equal(cipher, Cipher(KeePassXcCli.Run("db-info", file.Path_)));
        Assert.Equal(["old-pw", "added-value"], KeePassXcCli.Show(file.Path_, "old", "Password", "added"));
        Assert.Equal(kdbx31, File.ReadAllBytes(VaultBackups.List(file.Path_)[^1].Path));
    }

    [Fact]
    public void KeePassXC_reads_each_of_two_items_it_gave_one_title_as_itself_after_keypaste_edits_both()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.Save();
        }

        KeePassXcCli.Run("add", file.Path_, "-u", "first-user", "twin");
        KeePassXcCli.Run("mkdir", file.Path_, "other");
        KeePassXcCli.Run("add", file.Path_, "-u", "second-user", "other/twin");
        KeePassXcCli.Run("mv", file.Path_, "other/twin", "/");

        using (var vault = file.Open())
        {
            foreach (var row in vault.ReadEntries().Where(e => e.Title == "twin"))
            {
                var name = new EntryName(row.GroupPath, row.Title) { Uuid = row.Uuid };
                Assert.True(vault.UpdateEntry(name, row with { Notes = $"notes of {row.Username}" }));
            }

            vault.Save();
        }

        var twins = KeePassXcCli.Export(file.Path_).Descendants("Entry")
            .Where(e => e.Parent!.Name == "Group" && KeePassXcVault.StringValue(e, "Title") == "twin")
            .ToDictionary(e => KeePassXcVault.StringValue(e, "UserName")!, e => KeePassXcVault.StringValue(e, "Notes"));
        Assert.Equal("notes of first-user", twins["first-user"]);
        Assert.Equal("notes of second-user", twins["second-user"]);
    }

    [Fact]
    public void A_Twofish_vault_stays_Twofish_through_keypastes_saves_and_each_reads_what_the_other_wrote()
    {
        using var file = new KeePassXcVault();
        WriteTwofish(file.Path_);
        Assert.Contains("Twofish", Cipher(KeePassXcCli.Run("db-info", file.Path_)), StringComparison.Ordinal);

        using (var vault = file.Open())
        {
            Assert.True(vault.SetFields(new EntryName("env/tf", "deploy"), [new FieldWrite("TOKEN", "tf-2")]));
            vault.Save();
        }

        Assert.Contains("Twofish", Cipher(KeePassXcCli.Run("db-info", file.Path_)), StringComparison.Ordinal);
        Assert.Equal(4, File.ReadAllBytes(file.Path_)[10]);
        Assert.Equal(["tf-2"], KeePassXcCli.Show(file.Path_, "env/tf/deploy", "TOKEN"));

        KeePassXcCli.Run("add", file.Path_, "-g", "-L", "24", "-l", "-U", "-n", "env/tf/from-kpxc");
        var written = KeePassXcCli.Show(file.Path_, "env/tf/from-kpxc", "Password")[0];
        Assert.NotEmpty(written);
        using (var vault = file.Open())
        {
            Assert.Equal(written, vault.Find(new EntryName("env/tf", "from-kpxc"))!.Password);
            var found = vault.Find(new EntryName("env/tf", "from-kpxc"))!;
            Assert.True(vault.UpdateEntry(new EntryName("env/tf", "from-kpxc"), found with { Password = "tf-app" }));
            vault.Save();
        }

        Assert.Contains("Twofish", Cipher(KeePassXcCli.Run("db-info", file.Path_)), StringComparison.Ordinal);
        Assert.Equal(["tf-app"], KeePassXcCli.Show(file.Path_, "env/tf/from-kpxc", "Password"));
    }

    [Fact]
    public void A_vault_keypaste_creates_opens_in_KeePassXC_and_one_given_a_keyfile_opens_only_with_both()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/kp94", Title = "deploy", Password = "created" });
            vault.Save();
        }

        Assert.Equal(["created"], KeePassXcCli.Show(file.Path_, "env/kp94/deploy", "Password"));

        var keyfile = Keyfile(file, "next.keyx");
        using (var vault = file.Open())
        {
            Assert.Equal(VaultAccessOutcome.Changed, vault.ChangeAccess(new VaultAccessChange(false, AccessKeyfileChange.Attach, keyfile), [], []).Outcome);
        }

        KeePassXcCli.Run(new KeePassXcKey(KeePassXcCli.Password, keyfile), "db-info", file.Path_);
        Assert.NotEqual(0, KeePassXcCli.Try(KeePassXcCli.Password, ["db-info", "-q", file.Path_]).Exit);
    }

    // The vault KeePass with its Twofish plugin writes, which keepassxc-cli cannot make: Twofish under AES-KDF.
    private static void WriteTwofish(string path)
    {
        var key = new KeePassLib.Keys.CompositeKey();
        key.AddUserKey(new KeePassLib.Keys.KcpPassword(KeePassXcCli.Password));
        var database = new KeePassLib.PwDatabase();
        try
        {
            database.New(KeePassLib.Serialization.IOConnectionInfo.FromPath(path), key);
            database.DataCipherUuid = KeePassLib.Cryptography.Cipher.TwofishCipherEngine.TwofishUuid;
            database.KdfParameters = new KeePassLib.Cryptography.KeyDerivation.AesKdf().GetDefaultParameters();
            database.KdfParameters.SetUInt64(KeePassLib.Cryptography.KeyDerivation.AesKdf.ParamRounds, 1000);
            var env = new KeePassLib.PwGroup(true, true, "env", KeePassLib.PwIcon.Folder);
            database.RootGroup.AddGroup(env, true);
            var project = new KeePassLib.PwGroup(true, true, "tf", KeePassLib.PwIcon.Folder);
            env.AddGroup(project, true);
            var deploy = new KeePassLib.PwEntry(true, true);
            deploy.Strings.Set(KeePassLib.PwDefs.TitleField, new KeePassLib.Security.ProtectedString(false, "deploy"));
            deploy.Strings.Set("TOKEN", new KeePassLib.Security.ProtectedString(true, "tf-1"));
            deploy.AddTag("env:tf");
            project.AddEntry(deploy, true);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }

    private static Vault Open(KeePassXcVault file, KeePassXcKey key) => Vault.Open(file.Path_, key.Password ?? "", key.Keyfile);

    private static string Cipher(string info) =>
        string.Join('\n', info.Split('\n').Where(line => line.StartsWith("Cipher:", StringComparison.Ordinal) || line.StartsWith("KDF:", StringComparison.Ordinal)));

    /// <summary>An XML keyfile KeePassXC writes itself.</summary>
    private static string Keyfile(KeePassXcVault file, string name)
    {
        var keyfile = Path.Combine(file.Directory_, name);
        var throwaway = Path.Combine(file.Directory_, $"{name}.kdbx");
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["db-create", "-q", "-p", "--set-key-file", keyfile, throwaway]).Exit);
        return keyfile;
    }

    private static Fixture Import(KeePassXcVault file, KeePassXcKey key)
    {
        var seed = Path.Combine(file.Directory_, "seed.xml");
        File.WriteAllText(seed, Seed());
        var create = key.Password is null ? new List<string>() : ["-p"];
        if (key.Keyfile is not null)
        {
            create.AddRange(["--set-key-file", key.Keyfile]);
        }

        Assert.Equal(0, KeePassXcCli.Try($"{key.Password}\n{key.Password}", ["import", "-q", .. create, seed, file.Path_]).Exit);

        var fixture = new Fixture(file, key);
        File.WriteAllBytes(Path.Combine(file.Directory_, "blob.bin"), System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096));
        File.WriteAllText(Path.Combine(file.Directory_, "note.txt"), "kp94 text attachment\r\nsecond line\n");
        foreach (var name in Fixture.Attachments)
        {
            KeePassXcCli.Run(key, "attachment-import", file.Path_, "servers/database", name, Path.Combine(file.Directory_, name));
        }

        fixture.TakeBaseline();
        return fixture;
    }

    private static string Uuid(string name) => Convert.ToBase64String(Encoding.ASCII.GetBytes(name.PadRight(16)[..16]));

    private static string Seed() => $"""
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <KeePassFile>
          <Meta>
            <Generator>keypaste-tests</Generator><DatabaseName>kp94</DatabaseName>
            <CustomData>
              <Item><Key>kp94.meta</Key><Value>kp94-meta-value</Value></Item>
              <Item><Key>KPXC_BROWSER_kp81d-keepassxc</Key><Value>{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))}</Value></Item>
              <Item><Key>_CREATED_kp81d-keepassxc</Key><Value>10/24/26 10:00 AM</Value></Item>
            </CustomData>
          </Meta>
          <Root><Group><UUID>{Uuid("root")}</UUID><Name>Root</Name>
            <Group><UUID>{Uuid("servers")}</UUID><Name>servers</Name>
              <DefaultAutoTypeSequence>{"{USERNAME}{TAB}{PASSWORD}{C:kp4a1-group}{ENTER}"}</DefaultAutoTypeSequence>
              <CustomData><Item><Key>kp94.group</Key><Value>kp94-group-value</Value></Item></CustomData>
              <Entry><UUID>{Uuid("database")}</UUID><Tags>kp94-tag</Tags>
                <String><Key>Title</Key><Value>database</Value></String>
                <String><Key>UserName</Key><Value>admin</Value></String>
                <String><Key>Password</Key><Value ProtectInMemory="True">pw-2</Value></String>
                <String><Key>kp94-plain</Key><Value>kp94-plain-value</Value></String>
                <String><Key>kp94-secret</Key><Value ProtectInMemory="True">kp94-secret-value</Value></String>
                <String><Key>kp7b-change</Key><Value>kp7b-change-value</Value></String>
                <String><Key>kp7b-remove</Key><Value ProtectInMemory="True">kp7b-remove-value</Value></String>
                <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/kp94?secret=KP7BOTPJBSWY3DPE&amp;issuer=kp94</Value></String>
                <AutoType><Enabled>True</Enabled><DataTransferObfuscation>0</DataTransferObfuscation>
                  <Association><Window>kp94-window</Window><KeystrokeSequence>{"{PASSWORD}{ENTER}"}</KeystrokeSequence></Association>
                  <Association><Window>//^kp4a1-regex [0-9]+//</Window><KeystrokeSequence>{"{USERNAME}{ENTER}"}</KeystrokeSequence></Association>
                  <Association><Window>*{"{TITLE}"} kp4a1-placeholder*</Window><KeystrokeSequence>{"{C:kp4a1-title}{PASSWORD}{ENTER}"}</KeystrokeSequence></Association>
                </AutoType>
                <CustomData>
                  <Item><Key>kp94.entry</Key><Value>kp94-entry-value</Value></Item>
                  <Item><Key>KeePassXC-Browser Settings</Key><Value>{"{"}"Allow":["kp81d.example"],"Deny":[],"Realm":""{"}"}</Value></Item>
                </CustomData>
                <History><Entry><UUID>{Uuid("database")}</UUID>
                  <String><Key>Title</Key><Value>database</Value></String>
                  <String><Key>Password</Key><Value ProtectInMemory="True">pw-1</Value></String>
                </Entry></History>
              </Entry>
              <Entry><UUID>{Uuid("cache")}</UUID>
                <String><Key>Title</Key><Value>cache</Value></String>
                <String><Key>Password</Key><Value ProtectInMemory="True">c-1</Value></String>
                <AutoType><Enabled>True</Enabled><DataTransferObfuscation>0</DataTransferObfuscation><DefaultSequence>{"{C:kp4a1-item}{PASSWORD}{ENTER}"}</DefaultSequence></AutoType>
                <CustomData><Item><Key>BrowserHideEntry</Key><Value>true</Value></Item></CustomData>
              </Entry>
            </Group>
            <Group><UUID>{Uuid("archive")}</UUID><Name>archive</Name></Group>
            <Group><UUID>{Uuid("review")}</UUID><Name>review</Name><EnableAutoType>false</EnableAutoType>
              <Entry><UUID>{Uuid("plain")}</UUID>
                <String><Key>Title</Key><Value>plain</Value></String>
                <String><Key>Password</Key><Value ProtectInMemory="True">p-1</Value></String>
              </Entry>
            </Group>
          </Group></Root>
        </KeePassFile>
        """;

    /// <summary>The imported vault and what KeePassXC read of it before keypaste wrote anything.</summary>
    private sealed class Fixture(KeePassXcVault file, KeePassXcKey key)
    {
        internal static readonly string[] Attachments = ["blob.bin", "note.txt"];

        private string _browser = "";
        private string _container = "";

        internal KeePassXcKey Key { get; set; } = key;

        internal void TakeBaseline()
        {
            _browser = Browser(Current());
            _container = Cipher(KeePassXcCli.Run(Key, "db-info", file.Path_));
        }

        internal string[] Show(string entry, params string[] attributes) =>
            KeePassXcCli.Run(Key, "show", file.Path_, [entry, .. attributes.SelectMany(a => new[] { "-a", a })]).TrimEnd('\n').Split('\n');

        internal XDocument Current()
        {
            var export = XDocument.Parse(KeePassXcCli.Run(Key, "export", file.Path_, "-f", "xml"));
            export.Descendants("History").Remove();
            return export;
        }

        /// <summary>KeePassXC still finds every marker outside history, the pairing as imported, Auto-Type off for review, both attachments byte for byte, its own cipher and KDF, and KDBX 4.</summary>
        internal void AssertIntact(string attachedTo)
        {
            var current = Current();
            var text = current.ToString();
            foreach (var marker in _markers)
            {
                Assert.True(text.Contains(marker, StringComparison.Ordinal), $"KeePassXC no longer finds {marker} outside history");
            }

            Assert.Equal(_browser, Browser(current));
            var review = current.Descendants("Group").Single(group => (string?)group.Element("Name") == "review");
            Assert.Equal("false", ((string?)review.Element("EnableAutoType"))?.ToLowerInvariant());

            foreach (var name in Attachments)
            {
                var exported = Path.Combine(file.Directory_, $"{name}.out");
                File.Delete(exported);
                KeePassXcCli.Run(Key, "attachment-export", file.Path_, attachedTo, name, exported);
                Assert.Equal(File.ReadAllBytes(Path.Combine(file.Directory_, name)), File.ReadAllBytes(exported));
            }

            Assert.Equal(_container, Cipher(KeePassXcCli.Run(Key, "db-info", file.Path_)));
            Assert.Equal(4, File.ReadAllBytes(file.Path_)[10]);
        }

        // KeePassXC-Browser's items, sorted and unstamped: a save may reorder them or stamp them, and neither changes one.
        private static string Browser(XDocument export) =>
            string.Join('\n', export.Descendants("Item")
                .Select(item => $"{(string?)item.Element("Key")}={(string?)item.Element("Value")}")
                .Where(item => item.StartsWith("KPXC_BROWSER_", StringComparison.Ordinal) || item.StartsWith("_CREATED_", StringComparison.Ordinal)
                    || item.StartsWith("KeePassXC-Browser Settings", StringComparison.Ordinal) || item.StartsWith("BrowserHideEntry", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }
}
