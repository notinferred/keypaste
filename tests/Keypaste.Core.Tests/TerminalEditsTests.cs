using Keypaste.Core.Editing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The edits the CLI's saving verbs make and a vault's holder makes for them (D-0464): each is checked
/// and applied by the same code, refused in the CLI's words, previewed by name only, and never saved here.
/// </summary>
public sealed class TerminalEditsTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _sentinel = "EDIT-SENTINEL-6f2a";

    private static readonly EntryName _stripe = new("api", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-edit-tests-").FullName;
    private readonly string _path;
    private readonly Vault _vault;
    private readonly EditScene _scene;

    public TerminalEditsTests()
    {
        _path = Path.Combine(_directory, "edits.kdbx");
        _vault = Vault.Create(_path, _master);
        _vault.AddEntry(new VaultEntry { Title = _stripe.Title, GroupPath = _stripe.GroupPath, Password = "login-password" });
        _vault.Save();
        _scene = new EditScene(Path.Combine(_directory, "clients.toml"), DateTimeOffset.UnixEpoch);
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private EditCheck Check(TerminalEdit edit) => TerminalEdits.Check(_vault, edit, _scene);

    private EditResult Apply(TerminalEdit edit, EditPreview? shown = null) => TerminalEdits.Apply(_vault, edit, _scene, shown);

    private void AssertUnsavedAndFileUntouched(byte[] before)
    {
        Assert.Equal(VaultSaveStatus.Unsaved, _vault.SaveState().Status);
        Assert.Equal(before, File.ReadAllBytes(_path));
    }

    private static void AssertNoValue(EditPreview preview)
    {
        Assert.DoesNotContain(preview.Changes, sentence => sentence.Contains(_sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(_sentinel, preview.Item, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_previews_by_name_creates_the_item_and_saves_nothing()
    {
        var before = File.ReadAllBytes(_path);
        var edit = new AddItem(new EntryName("work", "github"), "octo", string.Empty, string.Empty, NewSecret.Of(_sentinel));

        var preview = Check(edit).Preview!;
        AssertNoValue(preview);
        Assert.Equal(["Creates work/github with the password the terminal sent, and the username the terminal sent."], preview.Changes);

        var done = Apply(edit).Done!;

        Assert.Equal(EditOutcome.Created, done.Outcome);
        Assert.Equal("work/github", done.Item);
        Assert.Equal(_sentinel, _vault.Find(new EntryName("work", "github"))!.Password);
        AssertUnsavedAndFileUntouched(before);
    }

    [Fact]
    public void Add_refuses_an_item_that_exists_in_the_CLIs_words()
    {
        var refusal = Check(new AddItem(_stripe, string.Empty, string.Empty, string.Empty, null)).Refusal!;

        Assert.Equal(new EditRefusal(EditRefusalKind.Invalid, "'api/Stripe' already exists"), refusal);
    }

    [Fact]
    public void Set_replaces_the_password_and_keeps_the_old_one_in_history()
    {
        var edit = new SetPassword(_stripe, NewSecret.Of(_sentinel));

        var preview = Check(edit).Preview!;
        AssertNoValue(preview);
        Assert.False(preview.Creates);

        Assert.Equal(EditOutcome.Updated, Apply(edit).Done!.Outcome);
        Assert.Equal(_sentinel, _vault.Find(_stripe)!.Password);
        Assert.Contains(_vault.ReadHistory(_stripe)!, revision => revision.Fields.Password == "login-password");
    }

    [Fact]
    public void Set_creates_an_item_that_is_not_there()
    {
        var name = new EntryName("work", "new");

        Assert.Equal(EditOutcome.Created, Apply(new SetPassword(name, NewSecret.Of("v"))).Done!.Outcome);
        Assert.Equal("v", _vault.Find(name)!.Password);
    }

    [Fact]
    public void Set_fields_refuses_a_missing_item_and_plain_on_an_existing_field()
    {
        var missing = Check(new SetFields(new EntryName("api", "Absent"), [new FieldWrite("KEY", null)])).Refusal!;
        Assert.Equal(EditRefusalKind.NotFound, missing.Kind);
        Assert.Equal("no item 'api/Absent'; --field writes to an item that already exists", missing.Reason);

        _vault.SetFields(_stripe, [new FieldWrite("Region", "eu")]);
        var plain = Check(new SetFields(_stripe, [new FieldWrite("Region", null, Protect: false)])).Refusal!;

        Assert.Equal(EditRefusalKind.Invalid, plain.Kind);
        Assert.Equal("'Region' already exists and keeps its protection; --plain applies only to a new field", plain.Reason);
        Assert.Equal(["Nothing was written."], plain.Then);
    }

    [Fact]
    public void Set_fields_writes_every_field_in_one_revision()
    {
        var revisions = _vault.ReadHistory(_stripe)!.Count;
        var edit = new SetFields(_stripe, [new FieldWrite("KEY", _sentinel), new FieldWrite("OTHER", "o")]);

        AssertNoValue(Check(edit with { Fields = [new FieldWrite("KEY", null), new FieldWrite("OTHER", null)] }).Preview!);
        var done = Apply(edit).Done!;

        Assert.Equal(["KEY", "OTHER"], done.Names);
        Assert.Equal(_sentinel, _vault.ReadField(_stripe, "KEY"));
        Assert.Equal(revisions + 1, _vault.ReadHistory(_stripe)!.Count);
    }

    [Fact]
    public void Remove_says_whether_the_vault_keeps_a_recycle_bin()
    {
        var preview = Check(new RemoveItem("api/Stripe")).Preview!;
        Assert.True(preview.Recycles);
        Assert.Equal(EditOutcome.Recycled, Apply(new RemoveItem("api/Stripe")).Done!.Outcome);

        _vault.AddEntry(new VaultEntry { Title = "gone", Password = "pw" });
        _vault.SetRecyclesDeletedEntries(false);

        Assert.Equal(["Removes gone for good: this vault has no recycle bin."], Check(new RemoveItem("gone")).Preview!.Changes);
        Assert.Equal(EditOutcome.Deleted, Apply(new RemoveItem("gone")).Done!.Outcome);
    }

    [Fact]
    public void Remove_refuses_a_group_and_a_missing_item()
    {
        Assert.Equal("'api' is a group; only items can be removed", Check(new RemoveItem("api")).Refusal!.Reason);
        Assert.Equal(new EditRefusal(EditRefusalKind.NotFound, "no item 'nope'"), Check(new RemoveItem("nope")).Refusal);
    }

    [Fact]
    public void Rotate_generates_where_it_is_applied_and_refuses_a_missing_item()
    {
        Assert.Equal(EditOutcome.Rotated, Apply(new RotatePassword(_stripe, SecretRecipe.Default)).Done!.Outcome);
        Assert.NotEqual("login-password", _vault.Find(_stripe)!.Password);

        Assert.Equal(
            new EditRefusal(EditRefusalKind.NotFound, "no item named api/Absent"),
            Check(new RotatePassword(new EntryName("api", "Absent"), SecretRecipe.Default)).Refusal);
    }

    [Fact]
    public void Removing_a_field_refuses_one_the_item_lacks()
    {
        Assert.Equal(
            new EditRefusal(EditRefusalKind.NotFound, "'api/Stripe' has no field 'KEY'"),
            Check(new RemoveField("api/Stripe", "KEY")).Refusal);

        _vault.SetFields(_stripe, [new FieldWrite("KEY", "k")]);

        Assert.Equal(EditOutcome.FieldRemoved, Apply(new RemoveField("api/Stripe", "KEY")).Done!.Outcome);
        Assert.Empty(_vault.Fields(_stripe)!);
    }

    [Fact]
    public void Env_set_creates_the_home_item_and_a_value_already_held_writes_nothing()
    {
        var edit = new EnvSetKey("billing", "dev", "API_KEY", null, NewSecret.Of(_sentinel));

        var preview = Check(edit).Preview!;
        AssertNoValue(preview);
        Assert.Contains(preview.Changes, sentence => sentence.StartsWith("Creates env/billing/.env and tags it env:billing", StringComparison.Ordinal));

        var done = Apply(edit).Done!;
        Assert.Equal(EditOutcome.KeySet, done.Outcome);
        Assert.True(done.CreatesHome);
        Assert.Equal(EnvWriteChange.New, done.Keys[0].Change);

        var again = Check(edit).Unchanged!;
        Assert.Equal(EnvWriteChange.Unchanged, again.Keys[0].Change);
    }

    [Fact]
    public void Env_remove_refuses_a_project_or_key_that_is_not_there()
    {
        Assert.Equal("no env set for 'billing'", Check(new EnvRemoveKey("billing", "dev", "API_KEY", null)).Refusal!.Reason);

        Apply(new EnvSetKey("billing", "dev", "API_KEY", null, NewSecret.Of("v")));

        Assert.Equal("'billing' has no variable 'OTHER'", Check(new EnvRemoveKey("billing", "dev", "OTHER", null)).Refusal!.Reason);
        Assert.Equal(EditOutcome.KeyRemoved, Apply(new EnvRemoveKey("billing", "dev", "API_KEY", null)).Done!.Outcome);
    }

    [Fact]
    public void Env_pull_imports_all_and_the_same_file_again_is_unchanged()
    {
        var text = $"API_KEY={_sentinel}\nREGION=eu\n";
        var edit = new EnvPullFile("billing", "dev", null, text);

        var preview = Check(edit).Preview!;
        AssertNoValue(preview);
        Assert.StartsWith("Imports 2 variables into billing/dev: 2 new, 0 updated, 0 unchanged.", preview.Changes[0], StringComparison.Ordinal);

        Assert.Equal(EditOutcome.Imported, Apply(edit).Done!.Outcome);
        Assert.Equal(EditOutcome.Unchanged, Check(edit).Unchanged!.Outcome);
    }

    [Fact]
    public void Env_pull_refuses_a_file_that_does_not_parse()
    {
        var refusal = Check(new EnvPullFile("billing", "dev", null, "not a dotenv line\n")).Refusal!;

        Assert.Equal(EditRefusalKind.Invalid, refusal.Kind);
        Assert.EndsWith("nothing was imported", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_names_the_placeholders_each_value_plants_right_after_what_it_creates()
    {
        var edit = new AddItem(new EntryName("work", "github"), "octo", string.Empty, "{url}", NewSecret.Of(_sentinel + "{USERNAME}{S:Title}"));

        var preview = Check(edit).Preview!;

        AssertNoValue(preview);
        Assert.Equal(
            [
                "Creates work/github with the password the terminal sent, and the username and notes the terminal sent.",
                "Its new password fills in {USERNAME} and {S:Title} from this item when it leaves.",
                "Its new notes fill in {URL} from this item when they leave.",
            ],
            preview.Changes);
    }

    [Theory]
    [InlineData("{REF:P@I:0}", "{REF:…}, which keypaste does not fill in")]
    [InlineData("{Totp}", "{TOTP}, which keypaste does not fill in")]
    [InlineData("{password}", "{PASSWORD}, which leads back to itself")]
    [InlineData("{S:Region}", "{S:Region}, which names a field keypaste never releases")]
    [InlineData("{S:" + _sentinel + "}", "{S:…}, which names no field of its item")]
    public void Set_names_the_placeholder_that_refuses_its_new_password_wherever_it_leaves(string planted, string named)
    {
        _vault.SetFields(_stripe, [new FieldWrite("Region", "eu")]);

        var preview = Check(new SetPassword(_stripe, NewSecret.Of(_sentinel + planted))).Preview!;

        AssertNoValue(preview);
        Assert.Equal(
            [
                "Replaces the password of api/Stripe with the password the terminal sent; the old one stays in its history.",
                $"Its new password holds {named}, so it is refused wherever it would leave.",
            ],
            preview.Changes);
    }

    [Fact]
    public void Set_fields_name_a_field_only_when_the_item_has_it_or_the_edit_writes_it()
    {
        _vault.SetFields(_stripe, [new FieldWrite("DEPLOY_KEY", _sentinel)]);
        var edit = new SetFields(
            _stripe,
            [
                new FieldWrite("API_KEY", "{S:DEPLOY_KEY}"),
                new FieldWrite("DATABASE_URL", "postgres://u:{S:DB_PASSWORD}@db"),
                new FieldWrite("DB_PASSWORD", "db"),
                new FieldWrite("OTHER", "{S:" + _sentinel + "}"),
            ]);

        var preview = Check(edit).Preview!;

        AssertNoValue(preview);
        Assert.Equal(
            [
                "Sets API_KEY, DATABASE_URL, DB_PASSWORD and OTHER on api/Stripe to values the terminal sent; the item's earlier state stays in its history.",
                "The new value of API_KEY fills in {S:DEPLOY_KEY} from this item when it leaves.",
                "The new value of DATABASE_URL fills in {S:DB_PASSWORD} from this item when it leaves.",
                "The new value of OTHER holds {S:…}, which names no field of its item, so it is refused wherever it would leave.",
            ],
            preview.Changes);

        Assert.NotNull(Apply(edit, preview).Done);
        Assert.Equal("{S:DEPLOY_KEY}", _vault.ReadField(_stripe, "API_KEY"));
    }

    [Fact]
    public void Env_set_names_what_its_value_plants_from_the_item_it_is_written_to()
    {
        Apply(new TagItem("billing", "dev", "api/Stripe", Tagging: true));
        var edit = new EnvSetKey("billing", "dev", "DATABASE_URL", "api/Stripe", NewSecret.Of("postgres://u:{PASSWORD}@db-" + _sentinel));

        var preview = Check(edit).Preview!;

        AssertNoValue(preview);
        Assert.DoesNotContain(preview.Changes, sentence => sentence.Contains("login-password", StringComparison.Ordinal));
        Assert.Equal(
            [
                "Adds DATABASE_URL to billing/dev on api/Stripe with the value the terminal sent.",
                "The new value of DATABASE_URL fills in {PASSWORD} from this item when it leaves.",
            ],
            preview.Changes);
    }

    [Fact]
    public void Env_pull_names_what_each_variable_plants_on_its_item()
    {
        var text = $"API_URL=https://{{URL}}/{_sentinel}\nDATABASE_URL=postgres://u:{{PASSWORD}}@db\nLINK={{REF:P@I:0}}\nREGION=eu\n";

        var preview = Check(new EnvPullFile("billing", "dev", null, text)).Preview!;

        AssertNoValue(preview);
        Assert.Equal(
            [
                "Imports 4 variables into billing/dev: 4 new, 0 updated, 0 unchanged.",
                "On env/billing/.env, API_URL fills in {URL} from that item when it leaves.",
                "On env/billing/.env, DATABASE_URL fills in {PASSWORD} from that item when it leaves.",
                "On env/billing/.env, LINK holds {REF:…}, which keypaste does not fill in, so it is refused wherever it would leave.",
                "New on env/billing/.env: API_URL, DATABASE_URL, LINK and REGION.",
                "Creates env/billing/.env and tags it into billing/dev.",
            ],
            preview.Changes);
    }

    [Fact]
    public void Tagging_twice_is_unchanged_and_untagging_removes_the_tag()
    {
        var tag = new TagItem("billing", "dev", "api/Stripe", Tagging: true);

        Assert.Equal(EditOutcome.Tagged, Apply(tag).Done!.Outcome);
        Assert.Equal(EditOutcome.Unchanged, Check(tag).Unchanged!.Outcome);
        Assert.Equal(EditOutcome.Untagged, Apply(tag with { Tagging = false }).Done!.Outcome);
        Assert.Empty(_vault.Tags(_stripe)!);
    }

    [Fact]
    public void A_change_other_than_the_one_shown_is_refused_and_nothing_is_written()
    {
        var shown = Check(new RemoveItem("api/Stripe")).Preview!;
        _vault.SetRecyclesDeletedEntries(false);

        var refusal = Apply(new RemoveItem("api/Stripe"), shown).Refusal!;

        Assert.Equal(EditRefusalKind.Conflict, refusal.Kind);
        Assert.NotNull(_vault.Find(_stripe));
    }

    [Fact]
    public void Generate_makes_a_value_from_a_recipe_and_keeps_a_given_one()
    {
        var given = (SetPassword)TerminalEdits.Generate(new SetPassword(_stripe, NewSecret.Of("typed")));
        var generated = (SetPassword)TerminalEdits.Generate(new SetPassword(_stripe, NewSecret.Generated(SecretRecipe.Default)));

        Assert.Equal("typed", given.Password!.Text);
        Assert.Equal(20, generated.Password!.Text!.Length);
        Assert.Equal("a generated 20-character password", generated.Password.Describe("password"));
    }

    [Theory]
    [InlineData(ReservedGroups.Tokens, "x")]
    [InlineData("a//b", "x")]
    [InlineData("work", "")]
    public void Validation_refuses_a_name_the_CLI_would_not_send(string group, string title)
    {
        Assert.False(new SetPassword(new EntryName(group, title), NewSecret.Of("v")).TryValidate(out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validation_refuses_a_field_given_twice()
    {
        var edit = new SetFields(_stripe, [new FieldWrite("KEY", "a"), new FieldWrite("KEY", "b")]);

        Assert.False(edit.TryValidate(out var error));
        Assert.Equal("--field 'KEY' is given twice", error);
    }

    [Fact]
    public void No_edit_prints_its_value()
    {
        TerminalEdit[] edits =
        [
            new AddItem(_stripe, _sentinel, _sentinel, _sentinel, NewSecret.Of(_sentinel)),
            new SetPassword(_stripe, NewSecret.Of(_sentinel)),
            new SetFields(_stripe, [new FieldWrite("KEY", _sentinel)]),
            new EnvSetKey("billing", "dev", "KEY", null, NewSecret.Of(_sentinel)),
            new EnvPullFile("billing", "dev", null, $"KEY={_sentinel}"),
        ];

        Assert.All(edits, edit => Assert.DoesNotContain(_sentinel, edit.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(_sentinel, NewSecret.Of(_sentinel).ToString(), StringComparison.Ordinal);
    }
}
