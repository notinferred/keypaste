using Keypaste.Core.AutoType;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// keypaste reads an item's and its groups' Auto-Type settings from a KDBX file as KeePassXC 2.8 reads them, with its
/// inheritance and defaults, chooses the sequence for a window's title as KeePassXC matches its associations, fills
/// each field as D-0465 fills a value, never offers a recycled item or one of its own records, and keeps every setting
/// through its own edits and saves (P.4a1).
/// </summary>
public sealed class VaultAutoTypeTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _default = "{USERNAME}{TAB}{PASSWORD}{ENTER}";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-autotype-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void KeePassXCs_effective_sequences_hold_on_a_saved_vault()
    {
        // KeePassXC 2.8's TestAutoType::testAutoTypeEffectiveSequences, one entry for each of its cases.
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "entry1" });
            vault.AddEntry(new VaultEntry { Title = "entry2" });
            vault.AddEntry(new VaultEntry { Title = "entry3", GroupPath = "group1/group2" });
            vault.AddEntry(new VaultEntry { Title = "entry4", GroupPath = "group1/group3" });
            vault.AddEntry(new VaultEntry { Title = "entry5", GroupPath = "group1/group2" });
            vault.SetGroupAutoTypeUnchecked("group1", null, "{TEST_GROUP1}");
            vault.SetGroupAutoTypeUnchecked("group1/group3", false, "{TEST_GROUP3}");
            vault.SetAutoTypeUnchecked(new EntryName(string.Empty, "entry2"), true, false, "{TEST_ENTRY2}", []);
            vault.SetAutoTypeUnchecked(new EntryName("group1/group3", "entry4"), true, false, "{TEST_DISABLED}", []);
            vault.SetAutoTypeUnchecked(new EntryName("group1/group2", "entry5"), false, false, "{TEST_DISABLED}", []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal((_default, AutoTypeSource.Default, (int?)null), Chosen(reopened, string.Empty, "entry1"));
        Assert.Equal(("{TEST_ENTRY2}", AutoTypeSource.Item, (int?)null), Chosen(reopened, string.Empty, "entry2"));
        Assert.Equal(("{TEST_GROUP1}", AutoTypeSource.Group, (int?)null), Chosen(reopened, "group1/group2", "entry3"));
        Assert.Equal("Auto-Type is off for a group this item is in.", Off(reopened, "group1/group3", "entry4"));
        Assert.Equal("Auto-Type is off for this item.", Off(reopened, "group1/group2", "entry5"));
    }

    [Fact]
    public void The_default_sequence_drops_an_empty_username_or_password_as_KeePassXC_drops_it()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "both", Username = "alice", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = "username", Username = "alice" });
            vault.AddEntry(new VaultEntry { Title = "password", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = "neither" });
            vault.AddEntry(new VaultEntry { Title = "grouped", GroupPath = "spelled", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = "own", Password = "pw" });
            vault.SetGroupAutoTypeUnchecked("spelled", null, _default);
            vault.SetAutoTypeUnchecked(new EntryName(string.Empty, "own"), true, false, _default, []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal((_default, AutoTypeSource.Default, (int?)null), Chosen(reopened, string.Empty, "both"));
        Assert.Equal(("{USERNAME}{ENTER}", AutoTypeSource.Default, (int?)null), Chosen(reopened, string.Empty, "username"));
        Assert.Equal(("{PASSWORD}{ENTER}", AutoTypeSource.Default, (int?)null), Chosen(reopened, string.Empty, "password"));
        Assert.Equal((_default, AutoTypeSource.Default, (int?)null), Chosen(reopened, string.Empty, "neither"));
        Assert.Equal(("{PASSWORD}{ENTER}", AutoTypeSource.Group, (int?)null), Chosen(reopened, "spelled", "grouped"));
        Assert.Equal((_default, AutoTypeSource.Item, (int?)null), Chosen(reopened, string.Empty, "own"));
    }

    [Fact]
    public void A_group_that_says_off_stops_its_items_as_KeePassXC_decides()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "nearest", GroupPath = "a/b", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = "walked", GroupPath = "c/d", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = "stopped", GroupPath = "g/h/i", Password = "pw" });
            vault.SetGroupAutoTypeUnchecked("a", false, string.Empty);
            vault.SetGroupAutoTypeUnchecked("a/b", null, "{PASSWORD}");
            vault.SetGroupAutoTypeUnchecked("c", false, string.Empty);
            vault.SetGroupAutoTypeUnchecked("c/d", true, string.Empty);
            vault.SetGroupAutoTypeUnchecked("g", false, string.Empty);
            vault.SetGroupAutoTypeUnchecked("g/h", true, "{PASSWORD}");
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal("Auto-Type is off for a group this item is in.", Off(reopened, "a/b", "nearest"));
        Assert.Equal("Auto-Type is off for a group this item is in.", Off(reopened, "c/d", "walked"));
        Assert.Equal(("{PASSWORD}", AutoTypeSource.Group, (int?)null), Chosen(reopened, "g/h/i", "stopped"));
    }

    [Fact]
    public void An_association_chooses_the_sequence_for_a_title_as_KeePassXC_matches_it()
    {
        // KeePassXC 2.8's TestAutoType entries 3, 4 and 6, with the custom fields its patterns name.
        var path = NewVaultPath();
        var regex = new EntryName("site", "regex");
        var custom = new EntryName("site", "custom_attr");
        var empty = new EntryName("site", "empty window test");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = regex.Title, GroupPath = regex.GroupPath, Password = "regex" });
            vault.AddEntry(new VaultEntry { Title = custom.Title, GroupPath = custom.GroupPath, Password = "custom_attr" });
            vault.AddEntry(new VaultEntry { Title = empty.Title, GroupPath = empty.GroupPath, Username = "user", Password = "example6" });
            vault.SetFields(custom, [new FieldWrite("CUSTOM_FIRST", "AttrValueFirst", false), new FieldWrite("CUSTOM_SECOND", "AttrValueSecond", false), new FieldWrite("CustomAttrThird", "AttrValueThird", false)]);
            vault.SetAutoTypeUnchecked(regex, true, false, string.Empty, [("//REGEX1//", "regex1"), ("//^REGEX2$//", "regex2"), (@"//^REGEX3-([rd]\d){2}$//", "regex3")]);
            vault.SetAutoTypeUnchecked(custom, true, false, string.Empty,
            [
                ("//^{S:CUSTOM_FIRST}$//", "custom_attr_first"),
                ("//{S:CUSTOM_FIRST}And{S:CUSTOM_SECOND}//", "custom_attr_first_and_second"),
                ("//{S:CustomAttrThird}//", "custom_attr_third"),
            ]);
            vault.SetAutoTypeUnchecked(empty, true, false, string.Empty, [(string.Empty, "{S:Empty Window}"), ("non-matching window", "should not match"), ("*notepad*", "{USERNAME}")]);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(("regex1", AutoTypeSource.Association, 1), Chosen(reopened, regex, "lorem REGEX1 ipsum"));
        Assert.Equal(("regex1", AutoTypeSource.Association, 1), Chosen(reopened, regex, "lorem regex1 ipsum"));
        Assert.Equal(("regex2", AutoTypeSource.Association, 2), Chosen(reopened, regex, "REGEX2"));
        Assert.Equal(("regex3", AutoTypeSource.Association, 3), Chosen(reopened, regex, "REGEX3-R2D2"));
        Assert.Equal(("{PASSWORD}{ENTER}", AutoTypeSource.Default, (int?)null), Chosen(reopened, regex, "nomatch"));
        Assert.Equal(("custom_attr_first", AutoTypeSource.Association, 1), Chosen(reopened, custom, "AttrValueFirst"));
        Assert.Equal(("custom_attr_first_and_second", AutoTypeSource.Association, 2), Chosen(reopened, custom, "lorem AttrValueFirstAndAttrValueSecond ipsum"));
        Assert.Equal(("{USERNAME}", AutoTypeSource.Association, 3), Chosen(reopened, empty, "Windows Notepad"));
        Assert.Equal((_default, AutoTypeSource.Default, (int?)null), Chosen(reopened, empty, "Some Other Window"));

        using var unfillable = reopened.PlanAutoType(custom, "lorem AttrValueThird ipsum", TimeProvider.System)!;

        Assert.Equal(AutoTypeSource.Default, unfillable.Source);
        Assert.Equal(
            ["association 3's window pattern holds the KeePass placeholder {S:CustomAttrThird}, which names a field keypaste never releases"],
            unfillable.UnmatchedPatterns);
    }

    [Fact]
    public void A_recycled_item_and_one_of_keypastes_own_records_are_never_offered_and_an_expired_item_types_marked()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);
        var checking = new EntryName("Banking", "Checking");
        var after = new ManualClock(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero));

        using var vault = Vault.Open(path, _master);
        vault.SetAutoTypeUnchecked(checking, true, false, "{USERNAME}{TAB}{TOTP}{ENTER}", []);

        Assert.Null(vault.PlanAutoType(new EntryName(ReservedGroups.Tokens, "planted"), string.Empty, after));
        Assert.Null(vault.PlanAutoType(new EntryName("Recycle Bin", "Deleted"), string.Empty, after));

        using (var loose = vault.PlanAutoType(new EntryName(string.Empty, "Loose"), string.Empty, after))
        {
            Assert.True(loose!.CanType);
            vault.RemoveEntry(new EntryName(string.Empty, "Loose"));
        }

        Assert.Null(vault.PlanAutoType(new EntryName(string.Empty, "Loose"), string.Empty, after));

        using var expired = vault.PlanAutoType(checking, string.Empty, after)!;
        using var totp = vault.ReadTotp(checking, out _)!;

        Assert.True(expired.Expired);
        Assert.True(expired.CanType);
        Assert.Equal(totp.Code(after).Value, Assert.IsType<AutoTypeCode>(expired.Steps[2]).Code(after));
    }

    [Fact]
    public void Each_field_types_what_D_0465_fills_in_and_a_field_it_refuses_refuses_the_sequence()
    {
        var path = NewVaultPath();
        var login = new EntryName("work", "login");

        using var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = login.Title, GroupPath = login.GroupPath, Username = "alice", Password = "{USERNAME}-{S:API_KEY}", Notes = "{REF:P@I:0123}" });
        vault.SetFields(login, [new FieldWrite("API_KEY", "sk-live", true)]);
        vault.SetFieldsUnchecked(login, [new FieldWrite("a{PASSWORD}b", "braced", false)]);

        vault.SetAutoTypeUnchecked(login, true, false, "{PASSWORD}{ENTER}", []);
        using (var filled = vault.PlanAutoType(login, string.Empty, TimeProvider.System)!)
        {
            Assert.Equal("alice-sk-live", Assert.IsType<AutoTypeText>(filled.Steps[0]).Text);
            Assert.Equal(AutoTypeKey.Enter, Assert.IsType<AutoTypePress>(filled.Steps[1]).Key);
        }

        vault.SetAutoTypeUnchecked(login, true, false, "{USERNAME}{TAB}{NOTES}", []);
        using (var refused = vault.PlanAutoType(login, string.Empty, TimeProvider.System)!)
        {
            Assert.Equal(
                "keypaste can't type this item: its notes hold the KeePass placeholder {REF:…}, which keypaste does not fill in. Edit the item so keypaste can fill it in.",
                refused.Refusal);
            Assert.Empty(refused.Steps);
        }

        vault.SetAutoTypeUnchecked(login, true, false, "{S:a{PASSWORD}b}", []);
        using var braced = vault.PlanAutoType(login, string.Empty, TimeProvider.System)!;

        Assert.Equal("keypaste can't type this item: its Auto-Type sequence holds {S:a{PASSWORD}b}, which names a field keypaste never types.", braced.Refusal);
    }

    [Fact]
    public void KeePass_2s_two_channel_obfuscation_is_read_and_refuses_the_item()
    {
        var path = NewVaultPath();
        var name = new EntryName(string.Empty, "obfuscated");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = name.Title, Username = "alice", Password = "pw" });
            vault.SetAutoTypeUnchecked(name, true, true, string.Empty, []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var plan = reopened.PlanAutoType(name, string.Empty, TimeProvider.System)!;

        Assert.Equal("keypaste can't type this item: its Auto-Type asks for KeePass's two-channel obfuscation, which keypaste does not use.", plan.Refusal);
    }

    [Fact]
    public void Keypastes_edits_and_saves_keep_every_Auto_Type_setting()
    {
        var path = NewVaultPath();
        var name = new EntryName("work", "mail");
        var off = new EntryName("work/quiet", "silent");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = name.Title, GroupPath = name.GroupPath, Username = "alice", Password = "pw" });
            vault.AddEntry(new VaultEntry { Title = off.Title, GroupPath = off.GroupPath, Password = "pw" });
            vault.SetGroupAutoTypeUnchecked("work", null, "{USERNAME}{ENTER}");
            vault.SetGroupAutoTypeUnchecked("work/quiet", false, string.Empty);
            vault.SetAutoTypeUnchecked(name, true, false, "{PASSWORD}", [("*Mail*", "{USERNAME}{TAB}{PASSWORD}")]);
            vault.Save();
        }

        using (var vault = Vault.Open(path, _master))
        {
            vault.UpdateEntry(vault.Find(name)! with { Notes = "edited", Password = "pw-2" });
            vault.SetFields(name, [new FieldWrite("PIN", "1234")]);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(("{USERNAME}{TAB}{PASSWORD}", AutoTypeSource.Association, 1), Chosen(reopened, name, "Inbox - Mail"));
        Assert.Equal(("{PASSWORD}", AutoTypeSource.Item, (int?)null), Chosen(reopened, name, "Other"));
        Assert.Equal("Auto-Type is off for a group this item is in.", Off(reopened, off.GroupPath, off.Title));
    }

    private static (string?, AutoTypeSource?, int?) Chosen(Vault vault, string group, string title) =>
        Chosen(vault, new EntryName(group, title), string.Empty);

    private static (string?, AutoTypeSource?, int?) Chosen(Vault vault, EntryName name, string window)
    {
        using var plan = vault.PlanAutoType(name, window, TimeProvider.System) ?? throw new InvalidOperationException($"no plan for {name}");
        return (plan.Sequence, plan.Source, plan.Association);
    }

    private static string? Off(Vault vault, string group, string title)
    {
        using var plan = vault.PlanAutoType(new EntryName(group, title), string.Empty, TimeProvider.System)!;

        Assert.Null(plan.Sequence);
        Assert.Empty(plan.Steps);
        return plan.Refusal;
    }

    private string NewVaultPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
