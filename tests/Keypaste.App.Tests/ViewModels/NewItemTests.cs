using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// New item starts from a template, a preset of fields any of which can be added or removed, saves in any open vault and a folder of
/// its groups, and makes the item whole in one write with no history item; a refusal writes nothing and keeps the form open.
/// </summary>
public sealed class NewItemTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-new-item-tests-").FullName;
    private readonly string _vaultPath;
    private readonly AppVaultSession _session = new(new ManualClock(AppClock.Start));
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));
    private readonly EntriesViewModel _entries;

    public NewItemTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using (var vault = ClaimedVault.Create(_vaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "github", GroupPath = "Work", Password = "gh" });
            vault.AddEntry(new VaultEntry { Title = "github", Password = "top" });
            vault.AddEntry(new VaultEntry { Title = "old", GroupPath = "Work/Archive", Password = "o" });
            vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", GroupPath = "env/billing", Password = "sk" });
            vault.Save();
        }

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vaultPath, master.Value));
        }

        _entries = new EntriesViewModel(_session, _countdown);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_new_item_goes_to_the_vaults_top_level_and_offers_no_group()
    {
        var form = NewItemForm.Open(_entries, "gitlab");

        Assert.Equal(ItemTemplate.Login, form.Template);
        Assert.DoesNotContain(form.FieldOptions, option => option.Label == "Group");
        _entries.ConfirmAddCommand.Press();

        Assert.False(_entries.IsAdding);
        Assert.Empty(Reopened(new EntryName(string.Empty, "gitlab")).Entry.GroupPath);
    }

    /// <summary>The tag box offers each environment the vault's items are tagged into, less a tag already added, and a tag that puts no item in a project offers nothing.</summary>
    [Fact]
    public void The_tag_box_offers_the_vaults_project_tags_less_those_added()
    {
        Assert.True(_session.Unlocked!.AddTag(new EntryName("Work", "github"), "env:billing:prod"));
        Assert.True(_session.Unlocked.AddTag(new EntryName("Work", "github"), "work"));
        Assert.True(_session.Unlocked.AddTag(new EntryName("Work/Archive", "old"), "env:billing"));
        var form = NewItemForm.Open(_entries, "gitlab");

        Assert.Equal(["env:billing", "env:billing:prod"], form.TagSuggestions);

        form.DraftTag = form.TagSuggestions[1];
        form.AddTagCommand.Press();

        Assert.Equal(["env:billing:prod"], form.Tags.Select(chip => chip.Tag));
        Assert.Equal(["env:billing"], form.TagSuggestions);
    }

    [Fact]
    public void A_login_is_its_username_password_and_web_address()
    {
        var form = NewItemForm.Open(_entries, "gitlab");
        form.Username = "me";
        form.Url = "https://gitlab.com";
        form.Notes = "the work account";
        form.DraftTag = "work";
        form.AddTagCommand.Press();
        _entries.ConfirmAddCommand.Press();

        Assert.False(_entries.IsAdding);
        Assert.Equal("gitlab", _entries.Selected?.Title);
        var item = Reopened(new EntryName(string.Empty, "gitlab"));
        Assert.Equal(("me", "https://gitlab.com", "the work account"), (item.Entry.Username, item.Entry.Url, item.Entry.Notes));
        Assert.Equal(PasswordGenerator.DefaultLength, item.Entry.Password.Length);
        Assert.Equal(["work"], item.Tags);
        Assert.Empty(item.Fields);
        Assert.Equal(0, item.Revisions);
    }

    [Fact]
    public void An_API_key_is_its_env_named_key_held_as_a_protected_field()
    {
        var form = NewItemForm.Open(_entries, "OpenAI", ItemTemplate.ApiKey);
        form.KeyName = "OPENAI_API_KEY";
        Type(form.KeyValue, "sk-proj-n4");
        form.DraftTag = "env:acme";
        form.AddTagCommand.Press();
        _entries.ConfirmAddCommand.Press();

        var item = Reopened(new EntryName(string.Empty, "OpenAI"));
        Assert.Equal([new EntryField("OPENAI_API_KEY", true, false)], item.Fields);
        Assert.Equal("sk-proj-n4", item.Read("OPENAI_API_KEY"));
        Assert.Equal((string.Empty, string.Empty), (item.Entry.Username, item.Entry.Password));
        Assert.Equal(["env:acme"], item.Tags);
        Assert.Equal(0, item.Revisions);
    }

    [Theory]
    [InlineData("Database", "db.example.com:5432")]
    [InlineData("Server", "build.example.com")]
    public void A_database_or_server_is_its_host_username_and_password(string template, string host)
    {
        var form = NewItemForm.Open(_entries, "box", Enum.Parse<ItemTemplate>(template));
        form.Host = host;
        form.Username = "admin";
        form.GeneratePassword = false;
        Type(form.Password, "box-pw");
        _entries.ConfirmAddCommand.Press();

        var item = Reopened(new EntryName(string.Empty, "box"));
        Assert.Equal([new EntryField(NewItemViewModel.HostField, false, false)], item.Fields);
        Assert.Equal(host, item.Read(NewItemViewModel.HostField));
        Assert.Equal(("admin", "box-pw", string.Empty), (item.Entry.Username, item.Entry.Password, item.Entry.Url));
        Assert.Equal(0, item.Revisions);
    }

    [Fact]
    public void A_note_is_its_notes_alone()
    {
        var form = NewItemForm.Open(_entries, "recovery codes", ItemTemplate.Note);
        form.Username = "left over from the login";
        form.Notes = "1234-5678";
        _entries.ConfirmAddCommand.Press();

        var item = Reopened(new EntryName(string.Empty, "recovery codes"));
        Assert.Equal("1234-5678", item.Entry.Notes);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (item.Entry.Username, item.Entry.Password, item.Entry.Url));
        Assert.Empty(item.Fields);
    }

    [Fact]
    public void A_kind_starts_with_its_fields_added_and_the_list_offers_the_rest()
    {
        var form = NewItemForm.Open(_entries);

        Assert.True(form.ShowsUsername && form.ShowsPassword && form.ShowsUrl && !form.ShowsNotes);
        Assert.DoesNotContain(form.FieldOptions, option => option.Field is NewItemField.Username or NewItemField.Password or NewItemField.Url);
        Assert.Contains(form.FieldOptions, option => option.Field == NewItemField.Notes);

        form.RemoveFieldCommand.Execute(NewItemField.Url);
        form.AddFieldCommand.Execute(NewItemField.Notes);

        Assert.False(form.ShowsUrl);
        Assert.True(form.ShowsNotes);
        Assert.Contains(form.FieldOptions, option => option.Field == NewItemField.Url);
        Assert.DoesNotContain(form.FieldOptions, option => option.Field == NewItemField.Notes);
    }

    [Fact]
    public void A_custom_and_a_protected_field_are_written_with_the_item()
    {
        var form = NewItemForm.Open(_entries, "extras");
        form.AddFieldCommand.Execute(NewItemField.CustomField);
        form.AddFieldCommand.Execute(NewItemField.ProtectedField);
        form.CustomFields[0].Name = "Region";
        form.CustomFields[0].Value = "eu";
        form.CustomFields[1].Name = "RECOVERY_CODE";
        Type(form.CustomFields[1].Secret, "1234");
        _entries.ConfirmAddCommand.Press();

        var item = Reopened(new EntryName(string.Empty, "extras"));
        Assert.Equal([new EntryField("RECOVERY_CODE", true, false), new EntryField("Region", false, false)], item.Fields);
        Assert.Equal("eu", item.Read("Region"));
        Assert.Equal("1234", item.Read("RECOVERY_CODE"));
    }

    [Fact]
    public void A_field_name_keypaste_will_not_write_refuses_the_item_and_names_the_field()
    {
        var form = NewItemForm.Open(_entries, "refused");
        form.AddFieldCommand.Execute(NewItemField.CustomField);
        form.CustomFields[0].Name = "Password";
        _entries.ConfirmAddCommand.Press();

        Assert.True(form.CustomFields[0].HasNameProblem);
        Assert.NotNull(_entries.NewItem);
        Assert.Null(_session.Unlocked!.Find(new EntryName(string.Empty, "refused")));
    }

    [Fact]
    public void A_typed_custom_field_is_a_draft_to_keep()
    {
        var form = NewItemForm.Open(_entries);
        Assert.Null(form.UnsavedDraft);

        form.AddFieldCommand.Execute(NewItemField.CustomField);
        Assert.Null(form.UnsavedDraft);

        form.CustomFields[0].Value = "typed";
        Assert.NotNull(form.UnsavedDraft);
    }

    [Fact]
    public void The_generator_starts_from_the_password_and_changes_it_only_on_Apply()
    {
        var form = NewItemForm.Open(_entries);
        var before = form.Password.Compose();
        form.OpenGeneratorCommand.Execute(null);

        var panel = form.GeneratorPanel!;
        Assert.True(panel.Applies);
        Assert.Same(form.Password, panel.Target);
        Assert.Equal(before, panel.Reveal());

        panel.Generator.Length = 12;
        Assert.Equal(before, form.Password.Compose());
        Assert.Equal(12, panel.MaskedLength);

        panel.CancelCommand.Execute(null);
        Assert.False(form.HasGeneratorPanel);
        Assert.Equal(before, form.Password.Compose());
        Assert.NotEqual(12, form.Generator.Length);

        form.OpenGeneratorCommand.Execute(null);
        panel = form.GeneratorPanel!;
        panel.Generator.Length = 12;
        var chosen = panel.Reveal();
        panel.ApplyCommand.Execute(null);

        Assert.False(form.HasGeneratorPanel);
        Assert.Equal(chosen, form.Password.Compose());
        Assert.True(form.Password.IsGenerated);
        Assert.Equal(12, form.Generator.Length);
        Assert.True(panel.Value.IsZeroed);
    }

    [Fact]
    public void Choices_the_generator_refuses_disable_Apply()
    {
        var form = NewItemForm.Open(_entries);
        form.OpenGeneratorCommand.Execute(null);
        var panel = form.GeneratorPanel!;

        panel.Generator.Letters = false;
        panel.Generator.Digits = false;
        panel.Generator.Symbols = false;

        Assert.False(panel.ApplyCommand.CanExecute(null));

        panel.Generator.Digits = true;

        Assert.True(panel.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void The_username_box_suggests_the_usernames_the_vault_uses_and_never_another_field()
    {
        _session.Unlocked!.AddEntry(new VaultEntry { Title = "mail", Username = "dan@example.com", Password = "dan-secret-password", Notes = "dan-recovery-code" });
        _session.Unlocked.AddEntry(new VaultEntry { Title = "chat", Username = "Dan", Password = "p" });
        var form = NewItemForm.Open(_entries);

        Assert.Empty(form.SuggestedUsernames);
        Assert.Null(form.SuggestedUsernamesSpoken);

        form.Username = "dan";

        Assert.Equal(["Dan", "dan@example.com"], form.SuggestedUsernames);
        Assert.Equal("Completes to dan@example.com, used in this vault. Tab accepts it; Down arrow lists all 2 matches; Escape dismisses it.", form.SuggestedUsernamesSpoken);

        form.Username = "dan@example.com";

        Assert.Empty(form.SuggestedUsernames);

        form.Username = "secret";

        Assert.Empty(form.SuggestedUsernames);
    }

    [Fact]
    public void The_username_boxs_list_and_its_spoken_count_offer_at_most_eight_and_nothing_once_disposed()
    {
        for (var index = 0; index < 10; index++)
        {
            _session.Unlocked!.AddEntry(new VaultEntry { Title = $"mail {index}", Username = $"dan{index}@example.com", Password = "p" });
        }

        var form = NewItemForm.Open(_entries);
        form.Username = "example";

        Assert.Equal(8, form.SuggestedUsernames.Count);
        Assert.Contains("8 usernames", form.SuggestedUsernamesSpoken, StringComparison.Ordinal);
        Assert.Equal(8, form.UsedUsernames.Count(username => form.UsernameFilter("example", username)));

        form.Dispose();

        Assert.Empty(form.UsedUsernames);
        Assert.Empty(form.SuggestedUsernames);
    }

    [Fact]
    public void Adding_a_field_announces_the_rows_and_the_buttons_that_read_it()
    {
        var form = NewItemForm.Open(_entries);
        var raised = new List<string>();
        form.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        var asked = 0;
        form.AddTagCommand.CanExecuteChanged += (_, _) => asked++;

        form.AddFieldCommand.Execute(NewItemField.Tags);

        string[] shown =
        [
            nameof(NewItemViewModel.ShowsUsername),
            nameof(NewItemViewModel.ShowsPassword),
            nameof(NewItemViewModel.ShowsUrl),
            nameof(NewItemViewModel.ShowsOneTimeCode),
            nameof(NewItemViewModel.ShowsHost),
            nameof(NewItemViewModel.ShowsKey),
            nameof(NewItemViewModel.ShowsNotes),
            nameof(NewItemViewModel.ShowsTags),
            nameof(NewItemViewModel.FillsInBrowser),
            nameof(NewItemViewModel.HasPresetRows),
            nameof(NewItemViewModel.FieldOptions),
            nameof(NewItemViewModel.HasFieldOptions),
            nameof(NewItemViewModel.PresetCustomFields),
            nameof(NewItemViewModel.AddedCustomFields),
        ];
        Assert.Equal(shown.Order(StringComparer.Ordinal), raised.Order(StringComparer.Ordinal));

        form.DraftTag = "a,b";
        form.AddTagCommand.Press();

        Assert.Equal(1, asked);
        Assert.Contains(nameof(NewItemViewModel.HasTagProblem), raised);
        Assert.True(form.HasTagProblem);
    }

    [Fact]
    public void The_grid_comes_first_and_a_tile_opens_its_form_titled_and_holding_no_draft()
    {
        _entries.ChooseTemplateCommand.Press();
        var form = _entries.NewItem!;

        Assert.True(form.IsChoosingTemplate);
        Assert.Null(form.Template);
        Assert.Equal(TemplateChoice.All.Select(choice => choice.Label), form.Templates.Select(choice => choice.Label));
        Assert.DoesNotContain(form.Templates, choice => choice.Label.Contains("Passkey", StringComparison.Ordinal));
        Assert.Null(form.Create());

        form.ChooseTemplateCommand.Press(ItemTemplate.Card);

        Assert.False(form.IsChoosingTemplate);
        Assert.Equal(ItemTemplate.Card, form.Template);
        Assert.Equal("New card", form.Title);
        Assert.Null(form.UnsavedDraft);
        Assert.False(form.ChooseTemplateCommand.CanExecute(ItemTemplate.Login));
    }

    [Theory]
    [InlineData("Login", ItemKind.Login)]
    [InlineData("ApiKey", ItemKind.ApiKey)]
    [InlineData("Environment", ItemKind.Environment)]
    [InlineData("Card", ItemKind.Card)]
    [InlineData("Note", ItemKind.SecureNote)]
    [InlineData("Identity", ItemKind.Identity)]
    [InlineData("SshKey", ItemKind.SshKey)]
    [InlineData("Server", ItemKind.Server)]
    [InlineData("Database", ItemKind.Database)]
    [InlineData("Wifi", ItemKind.Wifi)]
    [InlineData("Blank", ItemKind.None)]
    public void Each_template_keeps_its_kind_and_takes_custom_fields(string template, ItemKind kind)
    {
        var form = NewItemForm.Open(_entries, "made", Enum.Parse<ItemTemplate>(template));
        form.KeyName = "MADE_KEY";
        Type(form.KeyValue, "k");
        form.AddFieldCommand.Execute(NewItemField.CustomField);
        form.CustomFields[^1].Name = "Region";
        form.CustomFields[^1].Value = "eu";
        _entries.ConfirmAddCommand.Press();

        Assert.False(_entries.IsAdding);
        var item = Reopened(new EntryName(string.Empty, "made"));
        Assert.Equal(kind, item.Entry.Kind);
        Assert.Equal("eu", item.Read("Region"));
    }

    [Theory]
    [InlineData("Card", "Credit Card", "Number", FieldCategory.Card)]
    [InlineData("Identity", "Identity", "Passport number", FieldCategory.Identity)]
    public void A_card_or_identity_writes_the_tag_that_makes_its_protected_fields_its_agent_category(string template, string tag, string field, FieldCategory category)
    {
        var form = NewItemForm.Open(_entries, "mine", Enum.Parse<ItemTemplate>(template));

        Assert.Equal([tag], form.Tags.Select(chip => chip.Tag));
        Assert.False(form.ShowsTags);
        Assert.Empty(form.ShownTags);
        Assert.Contains(form.PresetCustomFields, custom => custom.Name == field);

        Type(form.CustomFields.Single(custom => custom.Name == field).Secret, "4111");
        _entries.ConfirmAddCommand.Press();

        var item = Reopened(new EntryName(string.Empty, "mine"));
        Assert.Equal([tag], item.Tags);
        Assert.Equal([new EntryField(field, true, false)], item.Fields);
        Assert.Equal("4111", item.Read(field));
        Assert.Equal(category, FieldCategories.Of(field, item.Tags, isProtected: true));
    }

    [Fact]
    public void A_presets_field_is_removable_and_one_left_empty_is_neither_written_nor_a_draft()
    {
        var form = NewItemForm.Open(_entries, "card", ItemTemplate.Card);

        Assert.Equal(["Cardholder name", "Number", "Expiration date", "Security code"], form.CustomFields.Select(custom => custom.Name));
        Assert.True(form.CustomFields.Single(custom => custom.Name == "Number").IsProtected);

        form.CustomFields.Single(custom => custom.Name == "Expiration date").RemoveCommand.Press();
        form.CustomFields.Single(custom => custom.Name == "Cardholder name").Value = "M Okafor";
        Assert.NotNull(form.UnsavedDraft);
        _entries.ConfirmAddCommand.Press();

        Assert.Equal([new EntryField("Cardholder name", false, false)], Reopened(new EntryName(string.Empty, "card")).Fields);
    }

    [Theory]
    [InlineData("Login", true)]
    [InlineData("ApiKey", false)]
    [InlineData("Server", false)]
    public void Any_item_with_a_username_password_and_website_says_it_fills_in_the_browser(string template, bool fills)
    {
        var form = NewItemForm.Open(_entries, template: Enum.Parse<ItemTemplate>(template));

        Assert.Equal(fills, form.FillsInBrowser);

        foreach (var field in new[] { NewItemField.Username, NewItemField.Password, NewItemField.Url }.Where(field => form.FieldOptions.Any(option => option.Field == field)))
        {
            form.AddFieldCommand.Execute(field);
        }

        Assert.True(form.FillsInBrowser);

        form.RemoveFieldCommand.Execute(NewItemField.Url);

        Assert.False(form.FillsInBrowser);
    }

    [Fact]
    public void Save_in_lists_every_open_vault_and_another_takes_the_item_and_its_groups()
    {
        var otherPath = Path.Combine(_directory, "work.kdbx");

        using (var vault = ClaimedVault.Create(otherPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "aws", GroupPath = "Cloud", Password = "a" });
            vault.Save();
        }

        using var other = new AppVaultSession(new ManualClock(AppClock.Start));
        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, other.TryUnlock(otherPath, master.Value));
        }

        var toasts = new List<string>();
        using var entries = new EntriesViewModel(_session, _countdown)
        {
            VaultTitle = "vault",
            SaveInPlaces = () => [new SaveInChoice(other, "On this Mac", "work"), new SaveInChoice(_session, "On this Mac", "vault")],
            Toast = (text, _) => toasts.Add(text),
        };
        var before = File.ReadAllBytes(_vaultPath);
        var form = NewItemForm.Open(entries);

        Assert.Equal(["On this Mac › vault", "On this Mac › work"], form.Places.Select(place => place.Label));
        Assert.Same(_session, form.SaveIn.Session);

        form.SaveIn = form.Places[1];
        form.Title = "gcp";
        entries.ConfirmAddCommand.Press();

        Assert.False(entries.IsAdding);
        Assert.Equal(["Saved gcp in work."], toasts);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
        using var reopened = ClaimedVault.Open(otherPath, _master);
        Assert.Equal(ItemKind.Login, reopened.Find(new EntryName(string.Empty, "gcp"))?.Kind);
    }

    [Fact]
    public void New_item_opens_the_places_kind_or_a_login_where_every_kind_is_listed()
    {
        using var entries = new EntriesViewModel(_session, _countdown) { VaultTitle = "Acme", Place = ItemPlace.Cards };

        entries.BeginAddCommand.Press();
        Assert.Equal(ItemTemplate.Card, entries.NewItem!.Template);
        entries.CancelAddCommand.Press();

        entries.Place = ItemPlace.All;

        entries.BeginAddCommand.Press();
        Assert.Equal(ItemTemplate.Login, entries.NewItem!.Template);
    }

    [Theory]
    [InlineData("github", "otp", "is already in that group")]
    [InlineData("OpenAI", "otp", "Name the key as a project reads it")]
    [InlineData("OpenAI", "api key", "Name the key as a project reads it")]
    [InlineData("OpenAI", "KPXC_KEY", "Name the key as a project reads it")]
    public void A_refused_item_writes_nothing_and_keeps_the_form_open(string title, string key, string says)
    {
        var before = File.ReadAllBytes(_vaultPath);
        var form = NewItemForm.Open(_entries, "" + title, title == "github" ? ItemTemplate.Login : ItemTemplate.ApiKey);
        form.KeyName = key;
        Type(form.KeyValue, "sk-refused");
        _entries.ConfirmAddCommand.Press();

        Assert.True(_entries.IsAdding);
        Assert.Contains(says, form.Error ?? form.KeyNameProblem, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public void A_login_with_a_one_time_code_keeps_it_as_its_TOTP_in_the_same_write()
    {
        var form = NewItemForm.Open(_entries, "gitlab");
        Type(form.OneTimeCode, "gezd gnbv gy3t qojq gezd gnbv gy3t qojq");
        _entries.ConfirmAddCommand.Press();

        Assert.False(_entries.IsAdding);
        var item = Reopened(new EntryName(string.Empty, "gitlab"));
        Assert.Equal([new EntryField("otp", true, true)], item.Fields);
        Assert.Equal("otpauth://totp/gitlab?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&period=30&digits=6&issuer=gitlab", item.Read("otp"));
        Assert.Equal(0, item.Revisions);
    }

    [Fact]
    public void A_refused_one_time_code_says_why_writes_nothing_and_is_disposed_on_cancel()
    {
        var before = File.ReadAllBytes(_vaultPath);
        var form = NewItemForm.Open(_entries, "gitlab");
        Type(form.OneTimeCode, "otpauth://hotp/gitlab?secret=GEZDGNBVGY3TQOJQ&counter=1");
        Assert.Equal("this new item", form.UnsavedDraft);

        _entries.ConfirmAddCommand.Press();

        Assert.True(_entries.IsAdding);
        Assert.True(form.HasOneTimeCodeProblem);
        Assert.Equal("The link holds a counter-based HOTP, which keypaste does not compute.", form.OneTimeCodeProblem);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        _entries.CancelAddCommand.Press();

        Assert.True(form.OneTimeCode.IsZeroed);
    }

    [Fact]
    public void A_one_time_code_is_a_field_any_kind_can_add_and_removing_it_clears_what_was_typed()
    {
        var form = NewItemForm.Open(_entries, "recovery", ItemTemplate.Note);
        Assert.False(form.ShowsOneTimeCode);

        form.AddFieldCommand.Execute(NewItemField.OneTimeCode);
        Assert.DoesNotContain(form.FieldOptions, option => option.Field == NewItemField.OneTimeCode);
        Type(form.OneTimeCode, "gezd gnbv gy3t qojq");
        form.RemoveFieldCommand.Execute(NewItemField.OneTimeCode);

        Assert.False(form.ShowsOneTimeCode);
        Assert.False(form.OneTimeCode.HasValue);

        form.AddFieldCommand.Execute(NewItemField.OneTimeCode);
        Type(form.OneTimeCode, "gezd gnbv gy3t qojq gezd gnbv gy3t qojq");
        _entries.ConfirmAddCommand.Press();

        Assert.False(_entries.IsAdding);
        Assert.Equal([new EntryField("otp", true, true)], Reopened(new EntryName(string.Empty, "recovery")).Fields);
    }

    [Fact]
    public void Cancelling_or_locking_disposes_what_was_typed()
    {
        var form = NewItemForm.Open(_entries, template: ItemTemplate.ApiKey);
        Type(form.KeyValue, "sk-typed");

        _entries.CancelAddCommand.Press();

        Assert.Null(_entries.NewItem);
        Assert.True(form.KeyValue.IsZeroed);
    }

    private static void Type(SecretField field, string text)
    {
        foreach (var c in text)
        {
            field.Type(c);
        }
    }

    private Item Reopened(EntryName name)
    {
        using var vault = ClaimedVault.Open(_vaultPath, _master);
        var entry = vault.Find(name) ?? throw new Xunit.Sdk.XunitException($"no '{name}' in the file");
        var fields = vault.Fields(name)!;
        return new Item(
            entry,
            fields,
            vault.Tags(name)!,
            vault.ReadHistory(name)!.Count,
            fields.ToDictionary(field => field.Name, field => vault.ReadField(name, field.Name)!));
    }

    private sealed record Item(VaultEntry Entry, IReadOnlyList<EntryField> Fields, IReadOnlyList<string> Tags, int Revisions, Dictionary<string, string> Values)
    {
        internal string Read(string field) => Values[field];
    }
}
