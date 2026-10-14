using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// Agents' settings kept in the vault: one hidden entry per agent under a UUID its name derives, which a
/// merge of two copies that each created it settles by time, authorizing only as the file holds it,
/// another entry under the name applying as the least, and a change rewriting only what it names, so
/// nothing keypaste does not understand is lost.
/// </summary>
public sealed class AgentAccessStoreTests : IDisposable
{
    private const string _agent = "claude-code";

    private static readonly EntryName _entry = new(ReservedGroups.Agents, _agent);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-agents-").FullName;
    private Vault _vault;

    public AgentAccessStoreTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        _vault.Save();
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private AgentAccessStore Store => new(_vault);

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ARecord_IsOneHiddenEntryTitledWithTheAgentsName()
    {
        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Automatic));
        _vault.Save();

        Assert.Empty(_vault.ReadEntries());
        Assert.Empty(_vault.Search(_agent));
        Assert.DoesNotContain(ReservedGroups.Agents, _vault.ReadGroupPaths());
        Assert.Equal(ExposureReach.None, See("**").Reach(_entry, []));

        var entry = Assert.Single(_vault.ReadEntries(includeReserved: true));
        Assert.Equal((ReservedGroups.Agents, _agent, string.Empty), (entry.GroupPath, entry.Title, entry.Password));
        Assert.Equal(AcafRecord.Write(Automatic), _vault.ReadField(_entry, AgentAccessStore.Field));
        Assert.False(Assert.Single(_vault.Fields(_entry)!).IsProtected);
        InFile(database =>
        {
            Assert.Equal(Uuid(_agent), Agents(database).Entries.Single().Uuid);
            Assert.Equal(GroupUuid(ReservedGroups.Agents), Agents(database).Uuid);
            Assert.Equal(GroupUuid(ReservedGroups.Root), Agents(database).ParentGroup.Uuid);
        });
    }

    [Fact]
    public void ASavedRecord_IsReadAsItsFileHoldsIt_AndSurvivesReopening()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();

        var records = Store.Read(_agent);

        Assert.Equal(SavedRead.Current, records.State);
        Assert.Equal(Automatic, Assert.Single(records.Own).Access);
        Assert.Empty(records.Any);

        _vault.Dispose();
        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);

        Assert.Equal(Automatic, Assert.Single(Store.Read(_agent).Own).Access);
        Assert.Equal(AgentMode.Automatic, EffectiveAccess.Create(_agent, Store.Read(_agent), AccessCaps.Empty).Item(["env:billing"], DateTimeOffset.UtcNow).Mode);
    }

    [Fact]
    public void AnUnsavedRecord_AppliesOnlyBeneathTheDefaults()
    {
        Store.Write(_agent, Automatic with { Values = false });

        var records = Store.Read(_agent);
        var access = EffectiveAccess.Create(_agent, records, AccessCaps.Empty);

        Assert.Equal(SavedRead.Unsaved, records.State);
        Assert.Equal(AgentMode.AllowForAWhile, access.Item(["env:billing"], DateTimeOffset.UtcNow).Mode);
        Assert.False(access.Values);
    }

    [Fact]
    public void ARecordAnotherProgramSaved_AppliesOnlyBeneathTheDefaultsUntilReloaded()
    {
        Store.Write(_agent, AgentAccess.Defaults);
        _vault.Save();

        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            new AgentAccessStore(other).Write(_agent, Automatic);
            other.Save();
        }

        var records = Store.Read(_agent);

        Assert.Equal(SavedRead.ChangedOnDisk, records.State);
        Assert.Equal(AgentMode.AllowForAWhile, EffectiveAccess.Create(_agent, records, AccessCaps.Empty).Item([], DateTimeOffset.UtcNow).Mode);

        var reloaded = _vault.Reload();
        _vault.Dispose();
        _vault = reloaded;

        Assert.Equal(AgentMode.Automatic, EffectiveAccess.Create(_agent, Store.Read(_agent), AccessCaps.Empty).Item([], DateTimeOffset.UtcNow).Mode);
    }

    [Fact]
    public void WritingAgain_EditsTheOneEntry_KeepingTheOldRecordInItsHistory()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();
        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Single(_vault.ReadEntries(includeReserved: true));
        Assert.Single(_vault.ReadHistory(_entry)!);
        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);

        string? kept = null;
        InFile(database => kept = Agents(database).Entries.Single().History.GetAt(0).Strings.Get(AgentAccessStore.Field)?.ReadString());

        Assert.Equal(AcafRecord.Write(Automatic), kept);
    }

    [Fact]
    public void WritingTheSameSettings_ChangesNothing()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();

        Assert.Equal(AgentAccessWrite.Unchanged, Store.Write(_agent, Automatic));
        Assert.Equal(SavedRead.Current, Store.Read(_agent).State);
        Assert.Empty(_vault.ReadHistory(_entry)!);
    }

    [Fact]
    public void AReset_WritesARecordOfTheDefaults_RatherThanDeleting()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();

        Assert.Equal(AgentAccessWrite.Written, Store.Reset(_agent));
        _vault.Save();

        var record = Assert.Single(Store.Read(_agent).Own);
        Assert.True(record.Understood);
        Assert.Equal(AgentAccess.Defaults, record.Access);
        Assert.Single(_vault.ReadHistory(_entry)!);
    }

    [Fact]
    public void TwoCopiesThatEachCreateAnAgentsRecord_HoldOneEntry_WhichKeePassMergesToTheNewerEitherWay()
    {
        var other = Path.Combine(_directory, "other.kdbx");
        File.Copy(VaultPath, other);

        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            var entry = Agents(database).Entries.Single();
            entry.LastModificationTime = entry.CreationTime = entry.LocationChanged = DateTime.UtcNow.AddMinutes(-1);
            database.Save(null);
        });

        using (var copy = Vault.Open(other, EnvStoreTests.MasterPassword))
        {
            Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(copy).Write(_agent, Asks));
            copy.Save();
        }

        var intoFirst = Path.Combine(_directory, "into-first.kdbx");
        var intoOther = Path.Combine(_directory, "into-other.kdbx");
        File.Copy(VaultPath, intoFirst);
        File.Copy(other, intoOther);
        Merge(intoFirst, other);
        Merge(intoOther, VaultPath);

        foreach (var merged in new[] { intoFirst, intoOther })
        {
            InFile(merged, database =>
            {
                var entry = Assert.Single(Agents(database).Entries);
                Assert.Equal(GroupUuid(ReservedGroups.Agents), entry.ParentGroup.Uuid);
                Assert.Equal(Uuid(_agent), entry.Uuid);
                Assert.Equal(AcafRecord.Write(Asks), entry.Strings.Get(AgentAccessStore.Field)?.ReadString());
                Assert.Contains(AcafRecord.Write(Automatic), entry.History.Select(revision => revision.Strings.Get(AgentAccessStore.Field)?.ReadString()));
            });

            using var vault = Vault.Open(merged, EnvStoreTests.MasterPassword);
            Assert.Equal(Asks, Assert.Single(new AgentAccessStore(vault).Read(_agent).Own).Access);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAgentsEntryMovedAway_IsMovedBack_WhenTheAgentIsWritten(bool recycled)
    {
        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            var agents = Agents(database);
            var entry = agents.Entries.Single();
            agents.Entries.Remove(entry);

            if (recycled)
            {
                PwGroup bin = new(true, true, "Recycle Bin", PwIcon.TrashBin);
                database.RootGroup.AddGroup(bin, true);
                database.RecycleBinUuid = bin.Uuid;
                database.RecycleBinEnabled = true;
                entry.PreviousParentGroup = agents.Uuid;
                bin.AddEntry(entry, true, true);
            }
            else
            {
                database.RootGroup.AddEntry(entry, true, true);
            }

            database.Save(null);
        });

        Assert.Empty(Store.Read(_agent).Own);
        Assert.Equal(recycled ? 0 : 1, _vault.ReadEntries().Count);

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        Assert.Empty(_vault.ReadEntries());
        InFile(database =>
        {
            var entry = database.RootGroup.FindEntry(Uuid(_agent), true);
            Assert.Same(Agents(database), entry.ParentGroup);
            Assert.True(entry.PreviousParentGroup.IsZero);
            Assert.Equal(AcafRecord.Write(Automatic), entry.History.GetAt(entry.History.UCount - 1).Strings.Get(AgentAccessStore.Field)?.ReadString());
            Assert.Single(database.RootGroup.GetEntries(true));
        });
    }

    [Fact]
    public void ATombstoneForTheEntry_IsDropped_WhenKeypasteCreatesItAgain_AndAMergeWithTheCopyHoldingItKeepsTheEntry()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            var agents = Agents(database);
            agents.Entries.Remove(agents.Entries.Single());
            database.DeletedObjects.Add(new PwDeletedObject(Uuid(_agent), DateTime.UtcNow.AddMinutes(-1)));
            database.Save(null);
        });

        var deleted = Path.Combine(_directory, "deleted.kdbx");
        File.Copy(VaultPath, deleted);
        Assert.Empty(Store.Read(_agent).Own);
        Assert.Equal(1, _vault.TombstoneCount);

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(0, _vault.TombstoneCount);
        _vault.Dispose();
        Merge(VaultPath, deleted);
        Merge(deleted, VaultPath);
        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);

        foreach (var merged in new[] { VaultPath, deleted })
        {
            InFile(merged, database =>
            {
                Assert.Equal(Uuid(_agent), Assert.Single(Agents(database).Entries).Uuid);
                Assert.DoesNotContain(database.DeletedObjects, tombstone => tombstone.Uuid.Equals(Uuid(_agent)));
            });
        }

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
    }

    [Fact]
    public void AnotherEntryUnderTheName_AppliesAsTheLeast_AndAWriteGivesItTheSameRecord()
    {
        var other = Automatic with { Run = true, Projects = [Row("billing", AgentMode.Ask)] };
        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            var agents = Agents(database);
            var own = agents.Entries.Single();
            PwEntry entry = new(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, _agent));
            entry.Strings.Set(AgentAccessStore.Field, new ProtectedString(false, AcafRecord.Write(other)));
            agents.AddEntry(entry, true);

            // Its own entry last in the file, so reading it first is the store's doing.
            agents.Entries.Remove(own);
            agents.AddEntry(own, true);
            database.Save(null);
        });

        var records = Store.Read(_agent);
        var access = EffectiveAccess.Create(_agent, records, AccessCaps.Empty);

        Assert.Equal(new[] { Automatic, other }, records.Own.Select(record => record.Access));
        Assert.Equal(AgentMode.Ask, access.Item(["env:billing"], DateTimeOffset.UtcNow).Mode);
        Assert.Equal(AgentMode.Automatic, access.Item(["env:other"], DateTimeOffset.UtcNow).Mode);
        Assert.False(access.Run);
        Assert.Equal(new[] { Automatic, other }, Store.List().Select(record => record.Record.Access));

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Edit(mode: "ask")));
        _vault.Save();

        Assert.Equal(new[] { Asks, Asks }, Store.Read(_agent).Own.Select(record => record.Access));
        Assert.Equal(AgentMode.Ask, EffectiveAccess.Create(_agent, Store.Read(_agent), AccessCaps.Empty).Item(["env:other"], DateTimeOffset.UtcNow).Mode);
        Assert.Equal(AgentAccessWrite.Unchanged, Store.Write(_agent, Edit(mode: "ask")));
        InFile(database =>
        {
            var hand = Agents(database).Entries.Single(entry => !entry.Uuid.Equals(Uuid(_agent)));
            Assert.Equal(AcafRecord.Write(other), Assert.Single(hand.History).Strings.Get(AgentAccessStore.Field)?.ReadString());
        });
    }

    [Fact]
    public void TheEntryRetitledByHand_KeepsTheOtherNamesRecord_WhenItsOwnAgentIsWritten()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            Agents(database).Entries.Single().Strings.Set(PwDefs.TitleField, new ProtectedString(false, "codex"));
            database.Save(null);
        });

        Assert.Empty(Store.Read(_agent).Own);
        Assert.Equal(Automatic, Assert.Single(Store.Read("codex").Own).Access);

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        Assert.Equal(Automatic, Assert.Single(Store.Read("codex").Own).Access);
        InFile(database =>
        {
            var entries = Agents(database).Entries;
            Assert.Equal(_agent, database.RootGroup.FindEntry(Uuid(_agent), true).Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal("codex", database.RootGroup.FindEntry(Uuid("codex"), true).Strings.ReadSafe(PwDefs.TitleField));
            Assert.Equal(2u, entries.UCount);
        });
    }

    /// <summary>
    /// A change to a record a person edited in KeePassXC, or another writer wrote, rewrites only what it
    /// names, keeping a limit, a row and a member keypaste does not understand, and the record still
    /// applies beneath the defaults until they are fixed.
    /// </summary>
    [Fact]
    public void AChange_RewritesOnlyWhatItNames_KeepingWhatKeypasteDoesNotUnderstand()
    {
        const string edited = """{"acaf":1,"mode":"automatic","limit":{"perHour":5,"perDay":50},"projects":[{"project":"billing","mode":"automatic","colour":"red"}],"later":{"x":1}}""";
        Assert.True(_vault.WriteRecord(_entry, AcafIds.Agent, AgentAccessStore.Field, edited));
        _vault.Save();

        Assert.False(Assert.Single(Store.Read(_agent).Own).Understood);
        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Edit(values: "no")));
        _vault.Save();

        Assert.Equal(
            """{"acaf":1,"values":false,"mode":"automatic","limit":{"perHour":5,"perDay":50},"projects":[{"project":"billing","mode":"automatic","colour":"red"}],"later":{"x":1}}""",
            _vault.ReadField(_entry, AgentAccessStore.Field));

        var access = EffectiveAccess.Create(_agent, Store.Read(_agent), AccessCaps.Empty);
        Assert.False(access.Values);
        Assert.Equal(AgentMode.Ask, access.Item(["env:billing"], DateTimeOffset.UtcNow).Mode);
        InFile(database => Assert.Equal(edited, Agents(database).Entries.Single().History.GetAt(0).Strings.Get(AgentAccessStore.Field)?.ReadString()));
    }

    [Fact]
    public void AChangeToAnAgentWithNoRecord_StartsFromTheRecordForEveryOtherAgent()
    {
        var everyOther = Asks with { Values = false, See = See("tag:env:billing") };
        Store.Write(AgentNames.Any, everyOther);

        Assert.Equal(AgentAccessWrite.Written, Store.Write("codex", Edit(run: "on")));
        _vault.Save();

        var record = Assert.Single(Store.Read("codex").Own);
        Assert.True(record.Understood);
        Assert.Equal(everyOther with { Run = true }, record.Access);
    }

    [Fact]
    public void AChangeToARow_InProjectsKeypasteCannotRead_IsRefused_AndChangesNothing()
    {
        const string unreadable = """{"acaf":1,"mode":"automatic","projects":{"project":"billing"}}""";
        Assert.True(_vault.WriteRecord(_entry, AcafIds.Agent, AgentAccessStore.Field, unreadable));
        _vault.Save();

        Assert.Equal(AgentAccessWrite.ProjectsUnreadable, Store.Write(_agent, Edit(project: "billing", mode: "ask")));
        Assert.Equal(AgentAccessWrite.ProjectsUnreadable, Store.Preview(_agent, Edit(project: "billing", mode: "ask"), out var record));
        Assert.Null(record);
        Assert.Equal(unreadable, _vault.ReadField(_entry, AgentAccessStore.Field));
        Assert.Equal(SavedRead.Current, Store.Read(_agent).State);
    }

    /// <summary>A preview says what a change would leave without writing it: whether the parts keypaste does not understand stay, or the change fixes them.</summary>
    [Fact]
    public void APreview_SaysWhatAChangeWouldLeave_AndWritesNothing()
    {
        const string unreadable = """{"acaf":1,"mode":"automatic","limit":{"perHour":0}}""";
        Assert.True(_vault.WriteRecord(_entry, AcafIds.Agent, AgentAccessStore.Field, unreadable));
        _vault.Save();

        Assert.Equal(AgentAccessWrite.Written, Store.Preview(_agent, Edit(run: "on"), out var kept));
        Assert.Equal(AgentAccessWrite.Written, Store.Preview(_agent, Edit(perHour: "5"), out var fixedLimit));

        Assert.False(kept!.Understood);
        Assert.Equal(AgentMode.Ask, kept.Access.Mode);
        Assert.True(fixedLimit!.Understood);
        Assert.Equal(Automatic with { Limit = new AccessLimit { PerHour = 5 } }, fixedLimit.Access);
        Assert.Equal(unreadable, _vault.ReadField(_entry, AgentAccessStore.Field));
        Assert.Equal(SavedRead.Current, Store.Read(_agent).State);
    }

    [Fact]
    public void ARecordOfAnotherVersion_IsNeverRewritten_NorReset()
    {
        const string later = """{"acaf":2,"mode":"ask"}""";
        _vault.WriteRecord(_entry, AcafIds.Agent, AgentAccessStore.Field, later);
        _vault.Save();

        Assert.Equal(AgentAccessWrite.UnknownVersion, Store.Write(_agent, Asks));
        Assert.Equal(AgentAccessWrite.UnknownVersion, Store.Reset(_agent));
        Assert.Equal(later, _vault.ReadField(_entry, AgentAccessStore.Field));
        Assert.Equal(SavedRead.Current, Store.Read(_agent).State);
    }

    [Fact]
    public void TheAnyRecord_MayOnlyTakeAwayFromTheDefaults()
    {
        Assert.Equal(AgentAccessWrite.WiderThanDefaults, Store.Write(AgentNames.Any, Automatic));
        Assert.Equal(AgentAccessWrite.WiderThanDefaults, Store.Write(AgentNames.Any, AgentAccess.Defaults with { Run = true }));
        Assert.Equal(AgentAccessWrite.WiderThanDefaults, Store.Write(AgentNames.Any, AgentAccess.Defaults with { See = See("**") }));
        Assert.Equal(AgentAccessWrite.WiderThanDefaults, Store.Write(AgentNames.Any, AgentAccess.Defaults with { Projects = [Row("billing", AgentMode.Automatic)] }));
        Assert.Empty(_vault.ReadEntries(includeReserved: true));

        Assert.Equal(AgentAccessWrite.Written, Store.Write(AgentNames.Any, Asks with { Values = false, See = See("tag:env:billing") }));
        _vault.Save();

        Assert.Equal(Asks with { Values = false, See = See("tag:env:billing") }, Assert.Single(Store.Read(null).Any).Access);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("say \"hi\"")]
    [InlineData("\u001b[2J")]
    public void ANameSettingsCannotBeKeptUnder_IsRefused(string name)
    {
        Assert.Equal(AgentAccessWrite.InvalidName, Store.Write(name, Asks));
        Assert.Empty(_vault.ReadEntries(includeReserved: true));
    }

    [Fact]
    public void ABridgeWithNoName_ReadsNoOwnRecord_EvenOneTitledLikeIt()
    {
        Store.Write(AgentNames.Any, Asks);
        _vault.Save();

        foreach (var label in new string?[] { null, string.Empty, AgentNames.Any, "a/b" })
        {
            var records = Store.Read(label);

            Assert.Empty(records.Own);
            Assert.Single(records.Any);
        }
    }

    [Fact]
    public void AnEntryWithNoRecord_ReadsAsNotUnderstood()
    {
        _vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Agents, Title = _agent, Notes = """{"acaf":1,"mode":"automatic"}""" });
        _vault.Save();

        var record = Assert.Single(Store.Read(_agent).Own);

        Assert.False(record.Understood);
        Assert.Equal(AgentMode.AllowForAWhile, EffectiveAccess.Create(_agent, Store.Read(_agent), AccessCaps.Empty).Item([], DateTimeOffset.UtcNow).Mode);
    }

    /// <summary>
    /// A change whose save fails is taken back out of the open vault, the entry and the groups it made
    /// included, so no later save, for anything, writes what the reply said was not made, and the vault
    /// again reads as its file holds it.
    /// </summary>
    [Fact]
    public void AChangeWhoseSaveFails_IsTakenBack_SoNoLaterSaveWritesIt()
    {
        var failing = new VaultAgentSettings(
            () => _vault,
            edit => edit(_vault) ? new AgentSave(AgentSaveOutcome.Failed, "the disk is full") : AgentSave.Unchanged,
            new AccessCapsSource(Path.Combine(_directory, "clients.toml")));

        Assert.Equal(AgentSaveOutcome.Failed, failing.Write(_agent, Edit(mode: "automatic")).Outcome);
        Assert.Empty(_vault.ReadEntries(includeReserved: true));
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);

        Store.Write("codex", Asks);
        _vault.Save();

        Assert.Equal(AgentSaveOutcome.Failed, failing.Write("codex", Edit(mode: "automatic")).Outcome);
        Assert.Equal(Asks, Assert.Single(Store.Read("codex").Own).Access);
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);

        Assert.Equal(AgentAccessWrite.Written, Store.Write("cursor", Asks));
        _vault.Save();

        InFile(database =>
        {
            Assert.Null(database.RootGroup.FindEntry(Uuid(_agent), true));
            var codex = database.RootGroup.FindEntry(Uuid("codex"), true);
            Assert.Equal(AcafRecord.Write(Asks), codex.Strings.ReadSafe(AgentAccessStore.Field));
            Assert.Empty(codex.History);
            Assert.Single(database.RootGroup.Groups, group => group.Name == ReservedGroups.Root);
        });
    }

    /// <summary>
    /// Only the group <c>agents</c> inside <c>.keypaste</c> holds agents' settings: a root group whose own
    /// name is <c>.keypaste/agents</c> joins to the same path, and what it holds is neither read nor written.
    /// </summary>
    [Fact]
    public void AGroupNamedLikeThePath_HoldsNoAgentsSettings()
    {
        InFile(database =>
        {
            PwGroup impostor = new(true, true, ReservedGroups.Agents, PwIcon.Folder);
            database.RootGroup.AddGroup(impostor, true);
            PwEntry entry = new(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, _agent));
            entry.Strings.Set(AgentAccessStore.Field, new ProtectedString(false, AcafRecord.Write(Automatic)));
            impostor.AddEntry(entry, true);
            database.Save(null);
        });

        Assert.Empty(Store.Read(_agent).Own);
        Assert.Empty(Store.List());

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        InFile(database =>
        {
            var impostor = database.RootGroup.Groups.Single(group => group.Name == ReservedGroups.Agents);
            Assert.Equal(AcafRecord.Write(Automatic), impostor.Entries.Single().Strings.ReadSafe(AgentAccessStore.Field));
            Assert.Equal(Uuid(_agent), Agents(database).Entries.Single().Uuid);
        });
    }

    /// <summary>
    /// <c>agents</c> moved into another group keeps its derived UUID there, so a write makes the group
    /// again under a random one, and no two objects in the file share a UUID.
    /// </summary>
    [Fact]
    public void TheAgentsGroupMovedAway_IsMadeAgainUnderARandomUuid()
    {
        Store.Write(_agent, Automatic);
        _vault.Save();
        InFile(database =>
        {
            var agents = Agents(database);
            agents.ParentGroup.Groups.Remove(agents);
            PwGroup archive = new(true, true, "archive", PwIcon.Folder);
            database.RootGroup.AddGroup(archive, true);
            archive.AddGroup(agents, true);
            database.Save(null);
        });

        Assert.Empty(Store.Read(_agent).Own);
        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        InFile(database =>
        {
            Assert.NotEqual(GroupUuid(ReservedGroups.Agents), Agents(database).Uuid);
            Assert.Equal(Uuid(_agent), Agents(database).Entries.Single().Uuid);
            Assert.Equal(GroupUuid(ReservedGroups.Agents), database.RootGroup.Groups.Single(group => group.Name == "archive").Groups.Single().Uuid);
            AssertEachUuidOnce(database);
        });
    }

    /// <summary>
    /// An entry retitled by hand to another agent's name, while that agent's own entry holds its derived
    /// UUID, is first copied for that name under a random UUID, and no two objects share one.
    /// </summary>
    [Fact]
    public void AnEntryRetitledToANameWhoseEntryExists_IsCopiedUnderARandomUuid()
    {
        Store.Write(_agent, Automatic);
        Store.Write("codex", Asks);
        _vault.Save();
        InFile(database =>
        {
            Agents(database).Entries.Single(entry => entry.Uuid.Equals(Uuid(_agent))).Strings.Set(PwDefs.TitleField, new ProtectedString(false, "codex"));
            database.Save(null);
        });

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        Assert.Equal(new[] { Asks, Automatic }, Store.Read("codex").Own.Select(record => record.Access));
        InFile(database =>
        {
            var codex = Agents(database).Entries.Where(entry => entry.Strings.ReadSafe(PwDefs.TitleField) == "codex").ToList();
            Assert.Equal(2, codex.Count);
            Assert.Contains(codex, entry => entry.Uuid.Equals(Uuid("codex")));
            AssertEachUuidOnce(database);
        });
    }

    /// <summary>
    /// An agent's entry made where a group already holds its derived UUID takes a random one, and a later
    /// write finds it again by its title rather than making another.
    /// </summary>
    [Fact]
    public void AnAgentsEntry_WhoseUuidAGroupHolds_IsMadeUnderARandomOne_AndFoundAgain()
    {
        InFile(database =>
        {
            database.RootGroup.AddGroup(new PwGroup(true, true, "holder", PwIcon.Folder) { Uuid = Uuid(_agent) }, true);
            database.Save(null);
        });

        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Automatic));
        Assert.Equal(AgentAccessWrite.Written, Store.Write(_agent, Asks));
        _vault.Save();

        Assert.Equal(Asks, Assert.Single(Store.Read(_agent).Own).Access);
        InFile(database =>
        {
            Assert.NotEqual(Uuid(_agent), Agents(database).Entries.Single().Uuid);
            AssertEachUuidOnce(database);
        });
    }

    [Fact]
    public void ARecordIsWrittenOnlyIntoKeypastesOwnGroups() =>
        Assert.Throws<ArgumentException>(() => _vault.WriteRecord(new EntryName("agents", _agent), AcafIds.Agent, AgentAccessStore.Field, "{}"));

    private static AccessEdit Edit(string? project = null, string? mode = null, string? values = null, string? run = null, string? perHour = null)
    {
        Assert.True(AccessEdit.TryCreate(project, mode, null, values, run, perHour, null, false, out var edit, out var error), error);
        return edit;
    }

    private static PwGroup Agents(PwDatabase database) =>
        database.RootGroup.Groups.Single(group => group.Name == ReservedGroups.Root).Groups.Single(group => group.Name == "agents");

    private static PwUuid Uuid(string name) => new(AcafIds.Agent(name).ToByteArray(bigEndian: true));

    /// <summary>Asserts that no two objects in the file, the root, groups and entries alike, share a UUID.</summary>
    private static void AssertEachUuidOnce(PwDatabase database)
    {
        List<PwUuid> ids =
        [
            database.RootGroup.Uuid,
            .. database.RootGroup.GetGroups(true).Select(group => group.Uuid),
            .. database.RootGroup.GetEntries(true).Select(entry => entry.Uuid),
        ];

        Assert.Equal(ids.Count, ids.Select(id => id.ToHexString()).Distinct(StringComparer.Ordinal).Count());
    }

    private static PwUuid GroupUuid(string groupPath) => new(AcafIds.Group(groupPath).ToByteArray(bigEndian: true));

    /// <summary>Closes the vault, works on its file as another KeePass program would, and opens it again.</summary>
    private void InFile(Action<PwDatabase> work)
    {
        _vault.Dispose();
        InFile(VaultPath, work);
        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
    }

    /// <summary>Works on a closed vault's file as another KeePass program would.</summary>
    private static void InFile(string path, Action<PwDatabase> work)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(EnvStoreTests.MasterPassword), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            work(database);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>Merges one closed vault's file into another's as KeePass's synchronize does, and saves it.</summary>
    private static void Merge(string target, string source) =>
        InFile(target, into => InFile(source, from =>
        {
            into.MergeIn(from, PwMergeMethod.Synchronize);
            into.Save(null);
        }));
}
