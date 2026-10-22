using System.Text.Json;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste search</c>: items found by title, group, username, URL or tag, named by path, the
/// fields that matched and the tags that matched, never by a value (D-0278).
/// </summary>
public sealed class SearchVerbTests : IDisposable
{
    private const string _master = "search-verb-master";

    private readonly CliHarness _harness = new();

    public SearchVerbTests()
    {
        _harness.SeedVault(_master);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.AddEntry(new VaultEntry
        {
            GroupPath = "work",
            Title = "GitHub",
            Username = "octo-user",
            Url = "https://github.example",
            Password = "needle-in-password",
            Notes = "needle-in-notes",
        });
        vault.AddEntry(new VaultEntry { GroupPath = "home/router", Title = "admin", Username = "root", Password = "pw" });
        vault.AddEntry(new VaultEntry { GroupPath = "home/router", Title = "printer", Password = "pw" });
        Assert.True(vault.AddTag(new EntryName("home/router", "printer"), "env:office:prod"));
        vault.AddEntry(new VaultEntry { GroupPath = "acme", Title = "acme portal", Username = "admin@acme", Url = "https://acme.test", Password = "pw" });
        vault.AddEntry(new VaultEntry { Title = "Bank", Url = "https://bank.example", Password = "pw" });
        vault.AddEntry(new VaultEntry { Title = "github token record", GroupPath = ReservedGroups.Tokens, Password = "verifier" });
        vault.AddEntry(new VaultEntry { GroupPath = "old", Title = "github old", Password = "pw" });
        Assert.True(vault.SetFields(new EntryName("work", "GitHub"), [new FieldWrite("API_KEY", "needle-in-field")]));
        vault.Save();
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("old", "github old")));
        vault.Save();

        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Prompt.Enqueue(_master);
        return _harness.Run([.. args, "--vault", _harness.VaultPath]);
    }

    private string[] Lines() => _harness.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void A_title_match_is_listed_by_path_alone()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "github"));

        Assert.Equal(["work/GitHub  URL"], Lines());
    }

    [Fact]
    public void A_username_or_url_match_names_the_field_and_not_its_value()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "octo"));
        Assert.Equal(["work/GitHub  username"], Lines());
        Assert.DoesNotContain("octo-user", _harness.Out, StringComparison.Ordinal);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "example"));
        Assert.Equal(["Bank  URL", "work/GitHub  URL"], Lines());
    }

    [Fact]
    public void A_group_match_lists_each_item_under_it_in_path_order()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "router"));

        Assert.Equal(["home/router/admin", "home/router/printer"], Lines());
    }

    [Fact]
    public void A_username_and_url_match_names_both()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "acme"));

        Assert.Equal(["acme/acme portal  username, URL"], Lines());
    }

    [Fact]
    public void A_tag_match_names_the_tag()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "prod"));

        Assert.Equal(["home/router/printer  tag env:office:prod"], Lines());
    }

    [Fact]
    public void Json_names_the_tags_that_matched()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "office", "--json"));

        var item = Assert.Single(JsonDocument.Parse(_harness.Out).RootElement.EnumerateArray());
        Assert.Equal("home/router/printer", item.GetProperty("path").GetString());
        Assert.Equal(["tag"], item.GetProperty("matched").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(["env:office:prod"], item.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public void Passwords_notes_and_custom_fields_are_never_matched()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "needle"));

        Assert.Empty(_harness.Out);
        Assert.Contains("no item matches", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Recycled_items_and_keypastes_own_groups_are_not_found()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "github", "--json"));

        var paths = JsonDocument.Parse(_harness.Out).RootElement.EnumerateArray()
            .Select(item => item.GetProperty("path").GetString());
        Assert.Equal(["work/GitHub"], paths);
    }

    [Fact]
    public void Json_names_each_item_and_every_field_that_matched()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "acme", "--json"));

        var item = Assert.Single(JsonDocument.Parse(_harness.Out).RootElement.EnumerateArray());
        Assert.Equal("acme/acme portal", item.GetProperty("path").GetString());
        Assert.Equal("acme", item.GetProperty("group").GetString());
        Assert.Equal("acme portal", item.GetProperty("title").GetString());
        Assert.Equal(["title", "group", "username", "url"], item.GetProperty("matched").EnumerateArray().Select(field => field.GetString()));
        Assert.DoesNotContain("admin@acme", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("acme.test", _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void No_match_is_success_with_an_empty_array_and_never_echoes_the_query()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("search", "zzz-nothing-here", "--json"));

        Assert.Equal("[]" + Environment.NewLine, _harness.Out);
        Assert.DoesNotContain("zzz-nothing-here", _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_is_a_usage_error_before_the_vault_opens(string query)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("search", query, "--vault", _harness.VaultPath));

        Assert.Contains("ls lists every item", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void Two_words_unquoted_are_a_usage_error()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("search", "git", "hub", "--vault", _harness.VaultPath));

        Assert.Contains("quote words that go together", _harness.Err, StringComparison.Ordinal);
    }
}
