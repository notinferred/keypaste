using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Import;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Copying another KDBX file into the vault in use: what survives the copy, what never crosses,
/// where each group lands, and what stops an import before anything is written.
/// </summary>
/// <remarks>
/// What the copy keeps is read back from the saved target at the KeePassLib level, because a
/// <see cref="VaultEntry"/> has five fields and the claim is about everything else an entry holds.
/// </remarks>
public sealed class KdbxImportTests : IDisposable
{
    internal const string SourcePassword = "source-password";
    internal const string TargetPassword = "target-password";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-import-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A vault another KeePass application might have written; see <c>KeePassInterop.WriteForeignUnchecked</c>.</summary>
    internal static void WriteForeign(string path, string password, string kdf = "Argon2id", string cipher = "ChaCha20", string? keyfile = null)
    {
        var utf8 = Encoding.UTF8.GetBytes(password);
        KeePassInterop.WriteForeignUnchecked(path, utf8, keyfile, kdf, cipher);
    }

    // ---------------------------------------------------------------- opening the source

    [Fact]
    public void WrongPassword_IsTheUsualAuthFailure()
    {
        var source = Foreign();

        Assert.Throws<InvalidMasterPasswordException>(() => KdbxImport.Open(source, "not-it", null));
    }

    [Fact]
    public void KeyfileRequired_WithoutIt_Fails()
    {
        var keyfile = Path.Combine(_directory, "acme.keyx");
        File.WriteAllBytes(keyfile, RandomNumberGenerator.GetBytes(64));
        var source = Path.Combine(_directory, "acme.kdbx");
        WriteForeign(source, SourcePassword, keyfile: keyfile);

        Assert.Throws<InvalidMasterPasswordException>(() => KdbxImport.Open(source, SourcePassword, null));

        using var opened = KdbxImport.Open(source, SourcePassword, keyfile);
        Assert.Equal(["password", "key file"], opened.KeyFactors);
        Assert.Equal(keyfile, opened.Probe.SiblingKeyfile);
    }

    [Fact]
    public void TheSourceFile_IsNeverWritten()
    {
        var source = Foreign();
        var bytes = File.ReadAllBytes(source);
        var written = File.GetLastWriteTimeUtc(source);
        using var target = Target();

        using (var opened = KdbxImport.Open(source, SourcePassword, null))
        {
            opened.ApplyTo(target, opened.DefaultPlan(target, null));
            target.Save();
        }

        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(written, File.GetLastWriteTimeUtc(source));
        Assert.False(File.Exists(source + ".tmp"));
    }

    // ---------------------------------------------------------------- what a copy keeps

    [Fact]
    public void EveryEntryField_CustomString_Attachment_Tag_Icon_Expiry_AndHistory_Survive()
    {
        var target = ImportForeignAndSave(out _);

        var facts = Facts(target, TargetPassword, new EntryName("foreign/Banking", "Checking"));
        Assert.Equal("v2", facts.Strings["Password"]);
        Assert.Equal("holder", facts.Strings["UserName"]);
        Assert.Equal("4321", facts.Strings["PIN"]);
        Assert.Equal("statement bytes", Encoding.UTF8.GetString(facts.Attachments["statement.txt"]));
        Assert.Equal(["finance"], facts.Tags);
        Assert.NotNull(facts.CustomIcon);
        Assert.True(facts.CustomIconPresent, "the copied entry names a custom icon the target does not carry");
        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), facts.Expires);
        Assert.Equal(["v1"], facts.HistoryPasswords);

        using var reopened = Vault.Open(target, TargetPassword);
        Assert.NotNull(reopened.Find(new EntryName("foreign/Banking/Cards", "Visa")));
        Assert.NotNull(reopened.Find(new EntryName("foreign", "Loose")));
    }

    [Fact]
    public void EveryCopiedItem_HasANewUuid_IncludingHistory()
    {
        var target = ImportForeignAndSave(out var source);

        var original = Facts(source, SourcePassword, new EntryName("Banking", "Checking"));
        var copy = Facts(target, TargetPassword, new EntryName("foreign/Banking", "Checking"));

        Assert.NotEqual(original.Uuid, copy.Uuid);
        Assert.NotEqual(original.GroupUuid, copy.GroupUuid);
        Assert.Equal(original.HistoryUuids.Count, copy.HistoryUuids.Count);
        Assert.All(copy.HistoryUuids, uuid => Assert.Equal(copy.Uuid, uuid));
        Assert.All(original.HistoryUuids, uuid => Assert.Equal(original.Uuid, uuid));
    }

    [Fact]
    public void FieldReferences_PointAtTheCopies()
    {
        var source = Source("refs.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "Shared", GroupPath = "Team", Username = "svc", Password = "shared-pw" });
            vault.AddEntry(new VaultEntry { Title = "Dropped", GroupPath = "Other", Password = "dropped-pw" });
        });
        var shared = Facts(source, SourcePassword, new EntryName("Team", "Shared")).Uuid;
        var dropped = Facts(source, SourcePassword, new EntryName("Other", "Dropped")).Uuid;
        var first = $"{{REF:P@I:{shared}}}";
        var second = $"{{ref:u@i:{shared.ToLowerInvariant()}}}";
        var outside = $"{{REF:P@I:{dropped}}}";
        using (var vault = Vault.Open(source, SourcePassword))
        {
            vault.AddEntry(new VaultEntry { Title = "User", GroupPath = "Team", Password = first });
            vault.UpdateEntry(new VaultEntry { Title = "User", GroupPath = "Team", Password = first + second, Notes = outside });
            vault.Save();
        }

        var targetPath = Path.Combine(_directory, "target.kdbx");
        using (var target = Target(targetPath))
        using (var opened = KdbxImport.Open(source, SourcePassword, null))
        {
            var plan = opened.DefaultPlan(target, "moved");
            opened.ApplyTo(target, plan with { Rows = [.. plan.Rows.Where(row => row.SourceGroup == "Team")] });
            target.Save();
        }

        var copied = Facts(targetPath, TargetPassword, new EntryName("moved/Team", "Shared")).Uuid;
        var user = Facts(targetPath, TargetPassword, new EntryName("moved/Team", "User"));
        Assert.NotEqual(shared, copied);
        Assert.Equal($"{{REF:P@I:{copied}}}{{ref:u@i:{copied}}}", user.Strings["Password"]);
        Assert.Equal(outside, user.Strings["Notes"]);
        Assert.Equal([$"{{REF:P@I:{copied}}}"], user.HistoryPasswords);
    }

    [Fact]
    public void CreationTimes_AreKept()
    {
        var target = ImportForeignAndSave(out var source);

        var original = Facts(source, SourcePassword, new EntryName("Banking", "Checking"));
        var copy = Facts(target, TargetPassword, new EntryName("foreign/Banking", "Checking"));

        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), copy.Created);
        Assert.Equal(original.Created, copy.Created);
        Assert.Equal(original.GroupCreated, copy.GroupCreated);
    }

    [Fact]
    public void TheRecycleBinAndReservedGroups_AreNotCopied()
    {
        var source = Foreign();
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);

        Assert.Equal(4, opened.EntryCount);
        Assert.Equal(2, opened.SkippedEntries);
        Assert.Equal([ReservedGroups.Root, "Recycle Bin"], opened.Skipped.Select(skip => skip.SourceGroup).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(opened.DefaultPlan(target, null).Rows, row => row.SourceGroup is "Recycle Bin" || ReservedGroups.IsReserved(row.SourceGroup));

        var result = opened.ApplyTo(target, opened.DefaultPlan(target, null));

        Assert.Equal(4, result.Entries);
        var titles = target.Search(string.Empty).Select(match => match.Name.Title).ToList();
        Assert.DoesNotContain("Deleted", titles);
        Assert.DoesNotContain("planted", titles);
        Assert.DoesNotContain(target.ReadGroupPaths(), path => path.Contains(ReservedGroups.Root, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(target.ReadRecycled());
    }

    // ---------------------------------------------------------------- where it lands

    [Fact]
    public void AnExistingDestination_IsMergedInto()
    {
        var source = Foreign();
        using var target = Target();
        target.AddEntry(new VaultEntry { Title = "Savings", GroupPath = "moved/Banking", Password = "s" });
        target.AddEntry(new VaultEntry { Title = "Amex", GroupPath = "moved/Banking/Cards", Password = "a" });

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        opened.ApplyTo(target, opened.DefaultPlan(target, "moved"));

        var groups = target.ReadGroupPaths();
        Assert.Single(groups, path => path == "moved/Banking");
        Assert.Single(groups, path => path == "moved/Banking/Cards");
        Assert.NotNull(target.Find(new EntryName("moved/Banking", "Savings")));
        Assert.NotNull(target.Find(new EntryName("moved/Banking", "Checking")));
        Assert.NotNull(target.Find(new EntryName("moved/Banking/Cards", "Amex")));
        Assert.NotNull(target.Find(new EntryName("moved/Banking/Cards", "Visa")));
    }

    [Fact]
    public void DuplicateTitles_AreKeptAndCounted()
    {
        var source = Foreign();
        using var target = Target();
        target.AddEntry(new VaultEntry { Title = "Checking", GroupPath = "moved/Banking", Password = "mine" });

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, "moved");

        var problem = Assert.Single(opened.Check(target, plan));
        Assert.False(problem.Blocks);
        Assert.Contains("moved/Banking", problem.Message, StringComparison.Ordinal);

        var result = opened.ApplyTo(target, plan);

        Assert.Equal(1, result.DuplicateTitles);
        Assert.Equal(2, target.Search(string.Empty).Count(match => match.Name == new EntryName("moved/Banking", "Checking")));
    }

    [Fact]
    public void DuplicateTitlesInMergedSubgroups_AreCountedBeforeTheImport()
    {
        var source = Source("src.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "gmail", GroupPath = "Work/Email", Password = "theirs" }));
        using var target = Target();
        target.AddEntry(new VaultEntry { Title = "gmail", GroupPath = "x/Work/Email", Password = "mine" });

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, "x");

        var problem = Assert.Single(opened.Check(target, plan));
        Assert.False(problem.Blocks);
        Assert.Equal("1 title in x/Work and its subgroups names another entry too; both are kept", problem.Message);

        var result = opened.ApplyTo(target, plan);

        Assert.Equal(1, result.DuplicateTitles);
    }

    [Fact]
    public void APlainGroupNamedLikeTheRecycleBin_LandsUnderAnotherName()
    {
        var source = Source("bins.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "kept", GroupPath = KeePassInterop.RecycleBinName, Password = "k" });
            vault.AddEntry(new VaultEntry { Title = "gone", GroupPath = "Other", Password = "g" });
            vault.RemoveEntry(new EntryName("Other", "gone"));
        });
        using var target = Target();

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, "into");

        var row = Assert.Single(plan.Rows, row => row.SourceGroup == KeePassInterop.RecycleBinName);
        Assert.Equal("into/Recycle Bin (imported)", row.Destination);
        Assert.Equal("Recycle Bin is the recycle bin's name", row.Rerouted);
        Assert.DoesNotContain(opened.Check(target, plan), problem => problem.Blocks);

        opened.ApplyTo(target, plan);

        Assert.NotNull(target.Find(new EntryName("into/Recycle Bin (imported)", "kept")));
        Assert.DoesNotContain(target.Search(string.Empty), match => match.Name.Title == "gone");
    }

    [Fact]
    public void DefaultPlan_MapsEnvProjects_AndAvoidsCollisions()
    {
        var source = Source("acme.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "a" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", GroupPath = "env/infra", Password = "t" });
            vault.AddEntry(new VaultEntry { Title = "Bank", GroupPath = "Banking", Password = "b" });
        });
        using var target = Target();
        target.AddEntry(new VaultEntry { Title = "OTHER", GroupPath = "env/infra", Password = "o" });

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, null);

        Assert.Equal("acme", plan.Into);
        Assert.Equal(2, opened.ProjectCount);
        Assert.Equal("env/acme-api", Row(plan, "env/acme-api").Destination);
        Assert.Equal("acme/env/infra", Row(plan, "env/infra").Destination);
        Assert.Equal("acme/Banking", Row(plan, "Banking").Destination);
        Assert.Empty(opened.Check(target, plan));
    }

    [Fact]
    public void IntoDefaultsToTheFileStem_WithASuffixWhenTaken()
    {
        var source = Source("acme.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "Bank", GroupPath = "Banking", Password = "b" }));
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);

        Assert.Equal("acme", opened.DefaultPlan(target, null).Into);

        target.AddEntry(new VaultEntry { Title = "x", GroupPath = "acme", Password = "x" });
        Assert.Equal("acme (2)", opened.DefaultPlan(target, null).Into);

        target.AddEntry(new VaultEntry { Title = "x", GroupPath = "acme (2)", Password = "x" });
        Assert.Equal("acme (3)", opened.DefaultPlan(target, null).Into);
        Assert.Equal("acme (3)/Banking", Row(opened.DefaultPlan(target, null), "Banking").Destination);
    }

    [Fact]
    public void IntoStripsLeadingDots()
    {
        var source = Source("..hidden.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "Bank", GroupPath = "Banking", Password = "b" }));
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);

        Assert.Equal("hidden", opened.DefaultPlan(target, null).Into);
    }

    [Fact]
    public void EnvProject_WithProfiles_ExpandsToOneRowPerProfile()
    {
        var source = Source("acme.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "dev" });
            vault.AddEntry(new VaultEntry { Title = "DB_URL", GroupPath = "env/acme-api", Password = "dev-db" });
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api/staging", Password = "staging" });
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api/prod", Password = "prod" });
        });
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, null);

        Assert.Equal(["env/acme-api", "env/acme-api/prod", "env/acme-api/staging"], plan.Rows.Select(row => row.SourceGroup).Order(StringComparer.Ordinal));
        Assert.Equal(2, Row(plan, "env/acme-api").EntryCount);
        Assert.Equal(1, Row(plan, "env/acme-api/staging").EntryCount);
        Assert.Equal(1, opened.ProjectCount);

        var result = opened.ApplyTo(target, plan);

        Assert.Equal(4, result.Entries);
        Assert.Equal("staging", target.Find(new EntryName("env/acme-api/staging", "API_KEY"))!.Password);
        Assert.Equal("dev", target.Find(new EntryName("env/acme-api", "API_KEY"))!.Password);
        Assert.Single(target.ReadGroupPaths(), path => path == "env/acme-api/staging");
    }

    // ---------------------------------------------------------------- what blocks

    [Fact]
    public void EnvSubgroupNamedDevOrInvalid_Blocks()
    {
        var source = Source("acme.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "a" });
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api/dev", Password = "d" });
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api/Bad_Name", Password = "b" });
        });
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var byDefault = opened.DefaultPlan(target, null);
        var plan = new ImportPlan([.. byDefault.Rows.Select(row => row with { Destination = row.SourceGroup })], byDefault.Into);
        var problems = opened.Check(target, plan);

        Assert.Empty(opened.Check(target, byDefault));
        Assert.Equal("acme/env/acme-api/dev", Row(byDefault, "env/acme-api/dev").Destination);
        Assert.Equal("env/acme-api", Row(byDefault, "env/acme-api").Destination);
        Assert.Contains(problems, p => p.Blocks && p.Index == Row(plan, "env/acme-api/dev").Index && p.Message.Contains("default profile", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocks && p.Index == Row(plan, "env/acme-api/Bad_Name").Index && p.Message.Contains("not a profile name", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Index == Row(plan, "env/acme-api").Index);
    }

    [Fact]
    public void EnvGroupThatIsNotAValidSet_DefaultsUnderInto()
    {
        var source = Source("servers.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "ssh root", GroupPath = "env/Work Servers", Password = "r" });
            vault.AddEntry(new VaultEntry { Title = "db pass", GroupPath = "env/Work Servers/Staging", Password = "d" });
            vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "a" });
            vault.AddEntry(new VaultEntry { Title = "bad key", GroupPath = "env/acme-api/staging", Password = "s" });
        });
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);

        var plan = opened.DefaultPlan(target, "Imported");

        Assert.Equal("Imported/env/Work Servers", Row(plan, "env/Work Servers").Destination);
        Assert.Equal("Imported/env/Work Servers/Staging", Row(plan, "env/Work Servers/Staging").Destination);
        Assert.Equal("env/acme-api", Row(plan, "env/acme-api").Destination);
        Assert.Equal("Imported/env/acme-api/staging", Row(plan, "env/acme-api/staging").Destination);
        Assert.StartsWith("not a valid env set: ", Row(plan, "env/Work Servers").Rerouted, StringComparison.Ordinal);
        Assert.Null(Row(plan, "env/acme-api").Rerouted);
        Assert.Empty(opened.Check(target, plan));

        opened.ApplyTo(target, plan);
        Assert.NotNull(target.Find(new EntryName("Imported/env/Work Servers/Staging", "db pass")));
    }

    [Fact]
    public void ADestinationInTheReservedGroup_Blocks()
    {
        var dotted = Source(".keypaste.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "Bank", GroupPath = "Banking", Password = "b" }));
        using var target = Target();
        using var opened = KdbxImport.Open(dotted, SourcePassword, null);

        var byDefault = opened.DefaultPlan(target, null);
        Assert.Equal("keypaste", byDefault.Into);
        Assert.DoesNotContain(byDefault.Rows, row => ReservedGroups.IsReserved(row.Destination));
        Assert.Empty(opened.Check(target, byDefault));

        foreach (var into in new[] { ".keypaste", ".Keypaste/x" })
        {
            var problem = Assert.Single(opened.Check(target, opened.DefaultPlan(target, into)));
            Assert.True(problem.Blocks);
            Assert.Contains("keypaste's own group", problem.Message, StringComparison.Ordinal);
        }

        var edited = byDefault with { Rows = [.. byDefault.Rows.Select(row => row with { Destination = ReservedGroups.Tokens })] };
        Assert.Contains(opened.Check(target, edited), problem => problem.Blocks);
        Assert.Throws<VaultException>(() => opened.ApplyTo(target, edited));
        Assert.DoesNotContain(target.ReadGroupPaths(), ReservedGroups.IsReserved);
    }

    [Fact]
    public void EnvDestination_InvalidOrDuplicateKey_Blocks()
    {
        var source = Source("acme.kdbx", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "bad-key", GroupPath = "env/one", Password = "1" });
            vault.AddEntry(new VaultEntry { Title = "Token", GroupPath = "env/two", Password = "2" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", GroupPath = "env/two", Password = "2" });
            vault.AddEntry(new VaultEntry { Title = "KEY", GroupPath = "env/three", Password = "3" });
            vault.AddEntry(new VaultEntry { Title = "KEY", GroupPath = "env/three", Password = "3" });
            vault.AddEntry(new VaultEntry { Title = "FINE", GroupPath = "env/four", Password = "4" });
        });
        using var target = Target();
        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var byDefault = opened.DefaultPlan(target, null);
        var plan = new ImportPlan([.. byDefault.Rows.Select(row => row with { Destination = row.SourceGroup })], byDefault.Into);
        var problems = opened.Check(target, plan);

        Assert.DoesNotContain(opened.Check(target, byDefault), p => p.Blocks);
        Assert.Contains(problems, p => p.Blocks && p.Index == Row(plan, "env/one").Index && p.Message.Contains("not a valid environment variable name", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocks && p.Index == Row(plan, "env/two").Index && p.Message.Contains("differ only in case", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocks && p.Index == Row(plan, "env/three").Index && p.Message.Contains("twice", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Index == Row(plan, "env/four").Index);
    }

    [Fact]
    public void EnvDestination_KeyAlreadyThere_Blocks()
    {
        var source = Source("acme.kdbx", vault => vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "theirs" }));
        using var target = Target();
        target.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "mine" });

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, null);
        Assert.Equal("acme/env/acme-api", Row(plan, "env/acme-api").Destination);
        Assert.Empty(opened.Check(target, plan));

        var intoTheirs = plan with { Rows = [.. plan.Rows.Select(row => row with { Destination = "env/acme-api" })] };
        var problem = Assert.Single(opened.Check(target, intoTheirs));
        Assert.True(problem.Blocks);
        Assert.Contains("already in env/acme-api", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockedRow_WritesNothing()
    {
        var source = Foreign();
        var targetPath = Path.Combine(_directory, "target.kdbx");
        using var target = Target(targetPath);
        var bytes = File.ReadAllBytes(targetPath);
        var edits = 0;
        target.Edited += (_, _) => edits++;

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var plan = opened.DefaultPlan(target, null);
        var blocked = plan with { Rows = [.. plan.Rows.Select((row, i) => i == 0 ? row with { Destination = "env" } : row)] };

        Assert.Throws<VaultException>(() => opened.ApplyTo(target, blocked));

        Assert.Equal(0, edits);
        Assert.Empty(target.Search(string.Empty));
        Assert.Empty(target.ReadGroupPaths());
        Assert.Equal(SavedRead.Current, target.ReadSaved(out _));
        Assert.Equal(bytes, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void ApplyTo_RaisesEditedWithTheNewNames()
    {
        var source = Foreign();
        using var target = Target();
        List<VaultEdit> edits = [];
        target.Edited += (_, edit) => edits.Add(edit);

        using var opened = KdbxImport.Open(source, SourcePassword, null);
        var result = opened.ApplyTo(target, opened.DefaultPlan(target, "moved"));

        var edit = Assert.Single(edits);
        Assert.Same(result.Edit, edit);
        Assert.Equal(
            [new EntryName("env/acme-api", "API_KEY"), new EntryName("moved", "Loose"), new EntryName("moved/Banking", "Checking"), new EntryName("moved/Banking/Cards", "Visa")],
            edit.Entries.OrderBy(name => name.GroupPath, StringComparer.Ordinal));
    }

    [Fact]
    public void SameFileAsTarget_IsRefused()
    {
        var path = Path.Combine(_directory, "target.kdbx");
        using var target = Target(path);
        using var opened = KdbxImport.Open(path, TargetPassword, null);

        var problem = Assert.Single(opened.Check(target, opened.DefaultPlan(target, null)), p => p.Index == -1);
        Assert.True(problem.Blocks);
        Assert.Contains("the vault you are importing into", problem.Message, StringComparison.Ordinal);
        Assert.Throws<VaultException>(() => opened.ApplyTo(target, opened.DefaultPlan(target, null)));
    }

    [Fact]
    public void TheResult_SavesAndReopens()
    {
        var target = ImportForeignAndSave(out var source);

        using (var reopened = Vault.Open(target, TargetPassword))
        {
            Assert.Equal(4, reopened.ReadEntries().Count);
            Assert.Equal("v2", reopened.Find(new EntryName("foreign/Banking", "Checking"))!.Password);
            Assert.Equal("a", reopened.Find(new EntryName("env/acme-api", "API_KEY"))!.Password);
        }

        using var again = KdbxImport.Open(source, SourcePassword, null);
        Assert.Equal(4, again.EntryCount);
    }

    // ---------------------------------------------------------------- fixtures

    private string Foreign(string name = "foreign.kdbx")
    {
        var path = Path.Combine(_directory, name);
        WriteForeign(path, SourcePassword);

        using var vault = Vault.Open(path, SourcePassword);
        vault.AddEntry(new VaultEntry { Title = "API_KEY", GroupPath = "env/acme-api", Password = "a" });
        vault.Save();
        return path;
    }

    private string Source(string name, Action<Vault> fill)
    {
        var path = Path.Combine(_directory, name);
        using var vault = Vault.Create(path, SourcePassword);
        fill(vault);
        vault.Save();
        return path;
    }

    private Vault Target(string? path = null)
    {
        path ??= Path.Combine(_directory, $"target-{Guid.NewGuid():n}.kdbx");
        var vault = Vault.Create(path, TargetPassword);
        vault.Save();
        return vault;
    }

    private string ImportForeignAndSave(out string source)
    {
        source = Foreign();
        var targetPath = Path.Combine(_directory, "target.kdbx");

        using var target = Target(targetPath);
        using var opened = KdbxImport.Open(source, SourcePassword, null);
        opened.ApplyTo(target, opened.DefaultPlan(target, null));
        target.Save();
        return targetPath;
    }

    private static ImportRow Row(ImportPlan plan, string sourceGroup) =>
        Assert.Single(plan.Rows, row => row.SourceGroup == sourceGroup);

    private static KeePassInterop.EntryFacts Facts(string path, string password, EntryName name)
    {
        using var interop = KeePassInterop.Open(path, Encoding.UTF8.GetBytes(password));
        return interop.FactsUnchecked(name) ?? throw new InvalidOperationException($"{name} is not in {path}");
    }
}
