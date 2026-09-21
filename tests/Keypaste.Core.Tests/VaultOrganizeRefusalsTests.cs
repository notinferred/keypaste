using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Every way the four organize operations refuse, and the one thing they all have in common: the
/// vault is exactly as it was, on disk and in memory.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory half is not decoration. Every check runs before anything mutates, because the
/// alternative — mutate, check, undo — leaves the open vault wrong on any path that throws between
/// the two, and a later unrelated <see cref="Vault.Save"/> writes the wreckage out. So each case
/// here asserts the file's bytes are untouched <em>and</em> that saving the same vault afterwards
/// still produces the shape it started with.
/// </para>
/// <para>
/// The trap this guards is <c>EnsureGroup</c>, which creates every missing segment of a path. It is
/// the obvious way for a move to find its destination and it would leave a fresh empty group tree
/// behind every refused move.
/// </para>
/// </remarks>
public sealed class VaultOrganizeRefusalsTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private static readonly EntryName _token = new("env/billing", "TOKEN");

    private readonly string _directory;

    public VaultOrganizeRefusalsTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-organize-refusals-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    // ---------------------------------------------------------------- finding the entry

    [Fact]
    public void RenamingAndMoving_MatchNothing_WhenNoEntryAnswersToTheName()
    {
        WritesNothing(vault =>
        {
            var absent = new EntryName("env/billing", "ABSENT");

            Assert.Equal(OrganizeOutcome.NothingMatched, vault.RenameEntry(absent, "STILL_ABSENT", out var renamed));
            Assert.Null(renamed);

            Assert.Equal(OrganizeOutcome.NothingMatched, vault.MoveEntry(absent, "keys", out var moved));
            Assert.Null(moved);
        });
    }

    /// <summary>
    /// A recycled entry is not in the vault as far as every reader is concerned (D-0248), so it is
    /// not found rather than specially refused. Putting one back is <see cref="Vault.RestoreRecycled"/>;
    /// reorganizing the bin is not a feature.
    /// </summary>
    [Fact]
    public void ARecycledEntry_IsNotThereToRenameOrMove()
    {
        var path = Seeded();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(_token));
            vault.Save();
        }

        WritesNothing(path, vault =>
        {
            Assert.Equal(OrganizeOutcome.NothingMatched, vault.RenameEntry(_token, "API_TOKEN", out _));
            Assert.Equal(OrganizeOutcome.NothingMatched, vault.MoveEntry(_token, "keys", out _));
        });
    }

    [Fact]
    public void AnAmbiguousSource_IsRefusedRatherThanGuessedAt()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "one", GroupPath = "env/billing" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "two", GroupPath = "env/billing" });
            vault.Save();
        }

        WritesNothing(path, vault =>
        {
            Assert.Throws<VaultException>(() => vault.RenameEntry(_token, "API_TOKEN", out _));
            Assert.Throws<VaultException>(() => vault.MoveEntry(_token, "keys", out _));
        });
    }

    // ---------------------------------------------------------------- finding the destination

    [Fact]
    public void MovingSomewhereThatDoesNotExist_CreatesNothingOnTheWay()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(OrganizeOutcome.DestinationMissing, vault.MoveEntry(_token, "archive/2026/q1", out var moved));
            Assert.Null(moved);

            Assert.DoesNotContain("archive", vault.ReadGroupPaths());
            Assert.DoesNotContain("archive/2026", vault.ReadGroupPaths());
        });
    }

    /// <summary>A move is not a delete: the bin is not a destination a person can name.</summary>
    [Fact]
    public void TheRecycleBin_IsNotADestination()
    {
        var path = Seeded();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("keys", "SPARE")));
            vault.Save();
        }

        WritesNothing(path, vault =>
            Assert.Equal(OrganizeOutcome.DestinationMissing, vault.MoveEntry(_token, "Recycle Bin", out _)));
    }

    /// <summary>
    /// Two sibling groups of one name produce one path. keypaste refuses to create the pair, so
    /// the fixture is built through the unchecked seam for the reason D-0255 gives about a vault
    /// with its recycle bin switched off: the shape is KeePassXC's to make, and keypaste still has
    /// to have an answer for it.
    /// </summary>
    [Fact]
    public void AnAmbiguousDestinationPath_IsRefusedRatherThanGuessedAt()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "v", GroupPath = "env/billing" });
            vault.AddEntry(new VaultEntry { Title = "ONE", Password = "1", GroupPath = "twins" });
            vault.AddGroupUnchecked(string.Empty, "twins");
            vault.Save();
        }

        WritesNothing(path, vault =>
        {
            Assert.Throws<VaultException>(() => vault.MoveEntry(_token, "twins", out _));
            Assert.Throws<VaultException>(() => vault.RenameGroup("twins", "one", out _));
            Assert.Throws<VaultException>(() => vault.CreateGroup("twins", "inner", out _));
        });
    }

    // ---------------------------------------------------------------- nothing to do

    [Fact]
    public void RenamingToTheSameTitleAndMovingToTheSameGroup_SayNothingChanged()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(OrganizeOutcome.DestinationUnchanged, vault.RenameEntry(_token, "TOKEN", out var renamed));
            Assert.Null(renamed);

            Assert.Equal(OrganizeOutcome.DestinationUnchanged, vault.MoveEntry(_token, "env/billing", out var moved));
            Assert.Null(moved);
        });
    }

    // ---------------------------------------------------------------- names the vault cannot address

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\tb")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void ATitleTheVaultCannotAddress_IsRefused(string title)
    {
        WritesNothing(vault =>
            Assert.Equal(OrganizeOutcome.NameRefused, vault.RenameEntry(new EntryName("keys", "SPARE"), title, out _)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\tb")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void AGroupNameTheVaultCannotAddress_IsRefused(string name)
    {
        WritesNothing(vault =>
        {
            Assert.Equal(GroupOutcome.NameRefused, vault.RenameGroup("keys", name, out _));
            Assert.Equal(GroupOutcome.NameRefused, vault.CreateGroup(string.Empty, name, out _));
        });
    }

    // ---------------------------------------------------------------- collisions

    [Fact]
    public void ANameTheDestinationAlreadyAnswersTo_IsRefused()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(OrganizeOutcome.DestinationOccupied, vault.RenameEntry(_token, "KEPT", out _));
            Assert.Equal(
                OrganizeOutcome.DestinationOccupied,
                vault.MoveEntry(new EntryName("env/shipping", "TOKEN"), "env/billing", out _));
        });
    }

    /// <summary>
    /// The combined write refuses the same way the two halves do, and writes nothing when it does.
    /// </summary>
    /// <remarks>
    /// This is the claim the desktop rests on. Composing a rename and a move would refuse the
    /// second after the first had already mutated the open vault, and the harness below checks
    /// exactly that by saving the same vault afterwards: the bytes and the shape must both be what
    /// they were. One operation cannot half-apply.
    /// </remarks>
    [Fact]
    public void ARefusedRelocate_LeavesNeitherHalfBehind()
    {
        WritesNothing(vault =>
        {
            // Both halves vary, and the title is one the destination already answers to.
            Assert.Equal(
                OrganizeOutcome.DestinationOccupied,
                vault.Relocate(_token, new EntryName("keys", "SPARE"), out var result));

            Assert.Null(result);

            // Both halves vary, and the destination is not a group.
            Assert.Equal(
                OrganizeOutcome.DestinationMissing,
                vault.Relocate(_token, new EntryName("nowhere", "API_TOKEN"), out _));

            // Both halves vary, and the new title is one no vault could address.
            Assert.Equal(
                OrganizeOutcome.NameRefused,
                vault.Relocate(_token, new EntryName("keys", "a/b"), out _));

            // Both halves vary, and the destination is a project the new title could not export to.
            Assert.Equal(
                OrganizeOutcome.EnvNameRefused,
                vault.Relocate(new EntryName("keys", "SPARE"), new EntryName("env/shipping", "HAS-HYPHEN"), out _));
        });
    }

    [Fact]
    public void ASiblingGroupOfThatName_RefusesTheRenameAndTheCreate()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(GroupOutcome.DestinationOccupied, vault.RenameGroup("env/billing", "shipping", out _));
            Assert.Equal(GroupOutcome.DestinationOccupied, vault.CreateGroup("env", "billing", out _));
        });
    }

    /// <summary>
    /// The rule <c>Vault</c> applies when it resolves a typed path, moved to the write. An entry titled
    /// <c>nested/TOKEN</c> and an entry <c>TOKEN</c> in a group <c>nested</c> produce one string,
    /// and keypaste refuses to create the pair even though it can read a vault that holds one.
    /// </summary>
    [Fact]
    public void AMoveThatWouldMakeTwoEntriesAnswerToOnePath_IsRefused()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "nested/TOKEN", Password = "slashed", GroupPath = "keys" });
            vault.AddEntry(new VaultEntry { Title = "OTHER", Password = "other", GroupPath = "keys/nested" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "loose", GroupPath = "spare" });
            vault.Save();
        }

        WritesNothing(path, vault =>
            Assert.Equal(
                OrganizeOutcome.DestinationAmbiguous,
                vault.MoveEntry(new EntryName("spare", "TOKEN"), "keys/nested", out _)));
    }

    /// <summary>
    /// The subtree case, and the one a sibling-name check cannot catch. <c>top</c> holds an entry
    /// titled <c>deep/TOKEN</c> and a subgroup <c>x</c> holding <c>TOKEN</c>. Nothing is called
    /// <c>deep</c>, so renaming <c>x</c> to it passes every check about the group itself — and
    /// makes <c>top/deep/TOKEN</c> answer to two entries.
    /// </summary>
    [Fact]
    public void AGroupRenameThatWouldMakeTwoEntriesAnswerToOnePath_IsRefusedWhole()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "deep/TOKEN", Password = "slashed", GroupPath = "top" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "nested", GroupPath = "top/x" });
            vault.Save();
        }

        WritesNothing(path, vault =>
        {
            Assert.Equal(GroupOutcome.DestinationAmbiguous, vault.RenameGroup("top/x", "deep", out _));
            Assert.Contains("top/x", vault.ReadGroupPaths());
        });
    }

    /// <summary>
    /// A vault can already hold the pair keypaste refuses to create; trapping such a vault so that
    /// nothing else in it can be reorganized would be worse than the ambiguity. Only a collision a
    /// write would produce counts.
    /// </summary>
    [Fact]
    public void AnExistingAmbiguityElsewhere_DoesNotRefuseAnUnrelatedRename()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "nested/TOKEN", Password = "slashed", GroupPath = "env/dev" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "nested", GroupPath = "env/dev/nested" });
            vault.AddEntry(new VaultEntry { Title = "SPARE", Password = "spare", GroupPath = "keys" });
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Equal(
            OrganizeOutcome.Renamed,
            reopened.RenameEntry(new EntryName("keys", "SPARE"), "BACKUP", out _));
    }

    // ---------------------------------------------------------------- the env namespace

    [Theory]
    [InlineData("lower-case")]
    [InlineData("9LEADING_DIGIT")]
    [InlineData("HAS-HYPHEN")]
    public void AnEnvVariableNameNothingCouldExport_IsRefused(string title)
    {
        WritesNothing(vault => Assert.Equal(OrganizeOutcome.EnvNameRefused, vault.RenameEntry(_token, title, out _)));
    }

    /// <summary>
    /// The env rules apply to the env namespace and nowhere else. A vault is not all environment
    /// variables, and a password called <c>lower-case</c> outside <c>env/</c> is an ordinary name.
    /// </summary>
    [Theory]
    [InlineData("lower-case")]
    [InlineData("9LEADING_DIGIT")]
    [InlineData("HAS-HYPHEN")]
    public void TheSameNameOutsideTheEnvNamespace_IsOrdinary(string title)
    {
        using var vault = Vault.Open(Seeded(), MasterPassword);

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(new EntryName("keys", "SPARE"), title, out _));
    }

    /// <summary>
    /// An entry directly in <c>env</c> is a write to nowhere: <see cref="EnvStore.Read"/> reads
    /// <c>env/&lt;project&gt;</c>, so nothing would ever find it again.
    /// </summary>
    [Fact]
    public void TheEnvRootItself_IsNotAPlaceAVariableCanLive()
    {
        WritesNothing(vault =>
            Assert.Equal(OrganizeOutcome.EnvNameRefused, vault.MoveEntry(new EntryName("keys", "SPARE"), "env", out _)));
    }

    [Fact]
    public void MovingAnEntryIntoAnEnvProjectUnderAnUnexportableName_IsRefused()
    {
        var path = Seeded();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "lower-case", Password = "x", GroupPath = "keys" });
            vault.Save();
        }

        WritesNothing(path, vault =>
            Assert.Equal(
                OrganizeOutcome.EnvNameRefused,
                vault.MoveEntry(new EntryName("keys", "lower-case"), "env/billing", out _)));
    }

    /// <summary>
    /// Two variables differing only in case are two on Linux and one on Windows. The rule already
    /// refuses the pair when a set is exported; a rename that creates it must be refused where it
    /// happens, not discovered later by whoever runs the project.
    /// </summary>
    [Fact]
    public void TwoKeysDifferingOnlyInCase_AreRefusedAtTheWrite()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(OrganizeOutcome.EnvNameCollides, vault.RenameEntry(_token, "Kept", out _));
            Assert.Equal(
                OrganizeOutcome.EnvNameCollides,
                vault.MoveEntry(new EntryName("env/shipping", "Kept"), "env/billing", out _));
        });
    }

    /// <summary>
    /// Changing only the case of one variable leaves the project with one variable, so the rule
    /// must not see the entry it is about to replace. Refusing this would refuse the repair
    /// somebody opened the app to make.
    /// </summary>
    [Fact]
    public void ChangingOnlyTheCaseOfAKey_IsAllowed_BecauseTheEntryIsNotItsOwnCollision()
    {
        using var vault = Vault.Open(Seeded(), MasterPassword);

        Assert.Equal(OrganizeOutcome.Renamed, vault.RenameEntry(new EntryName("env/billing", "KEPT"), "Kept", out _));
    }

    [Fact]
    public void RenamingAnEnvProjectToANameNothingCouldResolve_IsRefused()
    {
        WritesNothing(vault =>
            Assert.Equal(GroupOutcome.NameRefused, vault.RenameGroup("env/billing", "with/slash", out _)));
    }

    // ---------------------------------------------------------------- reserved names

    /// <summary>
    /// <c>env</c> at the root is not an ordinary folder: every child becomes a project, every
    /// grandchild a variable, and the whole subtree falls under <see cref="EntryExposure.Default"/>
    /// with nobody having written a glob. Renaming the env root away is the same act in reverse and
    /// would silently switch off every project.
    /// </summary>
    [Fact]
    public void TheEnvRootGroup_CanBeNeitherMadeNorRenamedNorReplaced()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(GroupOutcome.NameReserved, vault.CreateGroup(string.Empty, "env", out _));
            Assert.Equal(GroupOutcome.NameReserved, vault.RenameGroup("keys", "env", out _));
            Assert.Equal(GroupOutcome.NameReserved, vault.RenameGroup("env", "environments", out _));
        });
    }

    /// <summary>
    /// The bin is excluded from every traversal, so a sibling scan would happily make a second
    /// group of its name that KeePassXC then draws as two trash cans. The check asks the vault for
    /// its bin rather than looking at what it can see.
    /// </summary>
    [Fact]
    public void TheRecycleBinsName_IsNotAvailableForAnOrdinaryGroup()
    {
        var path = Seeded();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("keys", "SPARE")));
            vault.Save();
        }

        WritesNothing(path, vault =>
        {
            Assert.Equal(GroupOutcome.NameReserved, vault.CreateGroup(string.Empty, "Recycle Bin", out _));
            Assert.Equal(GroupOutcome.NameReserved, vault.RenameGroup("keys", "Recycle Bin", out _));
        });
    }

    [Fact]
    public void TheRecycleBin_IsNotAGroupThatCanBeRenamed()
    {
        var path = Seeded();

        using (var vault = Vault.Open(path, MasterPassword))
        {
            Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("keys", "SPARE")));
            vault.Save();
        }

        WritesNothing(path, vault =>
            Assert.Equal(GroupOutcome.NothingMatched, vault.RenameGroup("Recycle Bin", "Trash", out _)));
    }

    // ---------------------------------------------------------------- creating a group

    [Fact]
    public void CreatingUnderAParentThatDoesNotExist_MakesNoPartOfThePath()
    {
        WritesNothing(vault =>
        {
            Assert.Equal(GroupOutcome.ParentMissing, vault.CreateGroup("archive/2026", "q1", out var created));
            Assert.Equal(string.Empty, created, StringComparer.Ordinal);

            Assert.DoesNotContain("archive", vault.ReadGroupPaths());
        });
    }

    [Fact]
    public void RenamingAGroupThatIsNotThere_MatchesNothing()
    {
        WritesNothing(vault =>
            Assert.Equal(GroupOutcome.NothingMatched, vault.RenameGroup("absent", "present", out _)));
    }

    [Fact]
    public void RenamingAGroupToItsOwnName_SaysNothingChanged()
    {
        WritesNothing(vault =>
            Assert.Equal(GroupOutcome.DestinationUnchanged, vault.RenameGroup("env/billing", "billing", out _)));
    }

    // ---------------------------------------------------------------- helpers

    private void WritesNothing(Action<Vault> refused)
    {
        WritesNothing(Seeded(), refused);
    }

    /// <summary>
    /// Runs a refused operation and checks it left nothing behind: not the file's bytes, not the
    /// open vault's own answers, and not what a save of that vault would then put on disk.
    /// </summary>
    private static void WritesNothing(string path, Action<Vault> refused)
    {
        var bytes = File.ReadAllBytes(path);
        var before = Shape(path);

        using (var vault = Vault.Open(path, MasterPassword))
        {
            refused(vault);

            Assert.Equal(bytes, File.ReadAllBytes(path));
            vault.Save();
        }

        Assert.Equal(before, Shape(path));
    }

    private static List<string> Shape(string path)
    {
        using var vault = Vault.Open(path, MasterPassword);

        List<string> rows =
        [
            .. vault.ReadGroupPaths().Select(group => "group " + group),
            .. vault.ReadEntries().Select(entry => $"entry {entry.GroupPath} {entry.Title} {entry.Password}"),
            .. vault.ReadRecycled().Select(row => $"bin {row.OriginalGroupPath} {row.Title}"),
        ];

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private string Seeded()
    {
        var path = NewVaultPath();

        using var vault = Vault.Create(path, MasterPassword);

        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "v3", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "KEPT", Password = "kept", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "other", GroupPath = "env/shipping" });
        vault.AddEntry(new VaultEntry { Title = "Kept", Password = "cased", GroupPath = "env/shipping" });
        vault.AddEntry(new VaultEntry { Title = "SPARE", Password = "spare", GroupPath = "keys" });
        vault.Save();

        return path;
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
