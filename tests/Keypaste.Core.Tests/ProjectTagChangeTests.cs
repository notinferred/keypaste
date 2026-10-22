using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What a project tag change reaches, said before it is written (D-0415), and which agents it would let get the entry without asking.</summary>
public sealed class ProjectTagChangeTests : IDisposable
{
    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tag-change-tests-").FullName;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly Vault _vault;

    public ProjectTagChangeTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "v.kdbx"), EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "login" });
        Assert.True(_vault.SetFields(_stripe, [new FieldWrite("STRIPE_KEY", "s"), new FieldWrite("WEBHOOK", "w"), new FieldWrite("Region", "eu")]));
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string ClientsPath => Path.Combine(_directory, "clients.toml");

    private ProjectTagChange? Preview(IReadOnlyList<string> tags, bool adding) =>
        ProjectTagChange.Preview(_vault, _stripe, tags, adding, ClientsPath, _now);

    private void Record(string agent, AgentAccess access)
    {
        Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(_vault).Write(agent, access));
        _vault.Save();
    }

    private static AgentAccess AutomaticIn(string project, string environment)
    {
        Assert.True(ProjectScope.TryCreate(project, environment, out var scope, out var error), error);
        return AgentAccess.Defaults with { Projects = [new ProjectAccess(scope, AgentMode.Automatic)] };
    }

    [Fact]
    public void An_ordinary_tag_needs_no_question()
    {
        Assert.Null(Preview(["finance"], adding: true));
    }

    [Fact]
    public void Adding_a_member_tag_names_the_environment_its_protection_and_only_the_variable_fields()
    {
        var change = Preview(["env:billing:prod"], adding: true)!;

        Assert.True(change.Moves);
        Assert.Equal("billing/prod", change.Environment);
        Assert.Equal(
            [
                "env:billing:prod puts services/Stripe in billing/prod, a protected environment whose every release is asked live.",
                "Joining billing/prod: STRIPE_KEY, WEBHOOK.",
            ],
            change.Describe());
        Assert.DoesNotContain(change.Describe(), line => line.Contains("Region", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tag_that_leaves_the_entry_where_another_tag_puts_it_changes_no_environment()
    {
        Assert.True(_vault.AddTag(_stripe, "env:billing"));

        Assert.Equal(
            ["services/Stripe is already in billing/dev through another tag, so adding env:billing:dev changes no environment."],
            Preview(["env:billing:dev"], adding: true)!.Describe());

        Assert.True(_vault.AddTag(_stripe, "env:billing:dev"));
        Assert.Equal(
            ["services/Stripe stays in billing/dev through another tag, so removing env:billing:dev changes no environment."],
            Preview(["env:billing:dev"], adding: false)!.Describe());

        Assert.Equal(
            ["Removing env:billing, env:billing:dev takes services/Stripe out of billing/dev.", "Leaving billing/dev: STRIPE_KEY, WEBHOOK."],
            Preview(["env:billing", "env:billing:dev"], adding: false)!.Describe());
    }

    [Fact]
    public void A_malformed_tag_says_it_joins_no_project_and_whether_it_protects()
    {
        var shouted = Preview(["env:billing:Prod"], adding: true)!;
        var described = shouted.Describe();

        Assert.Null(shouted.Environment);
        Assert.StartsWith("env:billing:Prod puts services/Stripe in no project: ", described[0], StringComparison.Ordinal);
        Assert.Equal("While the item carries it, every agent request for the item is asked live.", described[1]);
        Assert.Equal("It changes no environment.", Preview(["Env:billing"], adding: true)!.Describe()[1]);
    }

    /// <summary>
    /// An agent set to Automatic where the change puts the entry, which sees it and is given values, is
    /// named on the change; saying so asks nothing more of the person.
    /// </summary>
    [Fact]
    public void A_tag_change_names_the_agents_it_would_let_get_the_entry_without_asking()
    {
        Record("claude-code", AutomaticIn("billing", "dev"));
        Record("commands-only", AutomaticIn("billing", "dev") with { Values = false });
        Record("asks", AgentAccess.Defaults);

        var change = Preview(["env:billing:dev"], adding: true)!;

        Assert.Equal(["claude-code"], change.Automatic);
        Assert.False(change.CeilingsUnread);
        Assert.Equal(
            "Automatic: claude-code would get services/Stripe without asking, each release recorded in history.",
            change.Describe()[^1]);

        // A protected environment always asks, and a ceiling held here applies.
        Assert.Empty(Preview(["env:billing:prod"], adding: true)!.Automatic);
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"claude-code\"\nmode = \"ask\"\n");
        Assert.Empty(Preview(["env:billing:dev"], adding: true)!.Automatic);
        Assert.Equal(2, Preview(["env:billing:dev"], adding: true)!.Describe().Count);

        // Already in billing/dev through another tag, the change gives nobody anything new.
        File.Delete(ClientsPath);
        Assert.True(_vault.AddTag(_stripe, "env:billing"));
        _vault.Save();
        Assert.Empty(Preview(["env:billing:dev"], adding: true)!.Automatic);
    }

    /// <summary>
    /// With this computer's <c>clients.toml</c> unreadable, the change names the agents the vault's settings
    /// alone would release the entry to, since the file can only lower them, and says they get it once the
    /// file reads again.
    /// </summary>
    [Fact]
    public void With_clients_toml_unreadable_the_agents_the_vaults_settings_would_release_to_are_named()
    {
        Record("claude-code", AutomaticIn("billing", "dev"));
        File.WriteAllText(ClientsPath, "this is not a clients file\n");

        var change = Preview(["env:billing:dev"], adding: true)!;

        Assert.True(change.CeilingsUnread);
        Assert.Equal(["claude-code"], change.Automatic);
        Assert.Equal(
            "Automatic: by the vault's settings, claude-code would get services/Stripe without asking, each release recorded in history, once this computer's clients.toml can be read again; until then every agent request is refused.",
            change.Describe()[^1]);
    }

    /// <summary>
    /// A variable joining an environment whose value holds a KeePass placeholder is named with each keyword it fills in
    /// from the entry, or with the one that refuses it, as a new value's prompt names them (D-0465); a change that puts
    /// the entry nowhere new names none, and no line holds a value.
    /// </summary>
    [Fact]
    public void Joining_names_each_variable_holding_a_placeholder_by_keyword_and_no_value()
    {
        Assert.True(_vault.SetFields(_stripe, [new FieldWrite("STRIPE_KEY", "sk-{USERNAME}:{PASSWORD}"), new FieldWrite("WEBHOOK", "{REF:P@I:0}")]));
        Record("claude-code", AutomaticIn("billing", "dev"));

        var described = Preview(["env:billing"], adding: true)!.Describe();

        Assert.Equal(
            [
                "env:billing puts services/Stripe in billing/dev.",
                "Joining billing/dev: STRIPE_KEY, WEBHOOK.",
                "STRIPE_KEY fills in {USERNAME} and {PASSWORD} from services/Stripe when it leaves.",
                "WEBHOOK holds {REF:…}, which keypaste does not fill in, so it is refused wherever it would leave.",
                "Automatic: claude-code would get services/Stripe without asking, each release recorded in history.",
            ],
            described);
        Assert.DoesNotContain(described, line => line.Contains("sk-", StringComparison.Ordinal) || line.Contains("login", StringComparison.Ordinal));

        Assert.True(_vault.AddTag(_stripe, "env:billing"));
        Assert.DoesNotContain(Preview(["env:billing"], adding: false)!.Describe(), line => line.Contains('{', StringComparison.Ordinal));
        Assert.DoesNotContain(Preview(["env:billing:dev"], adding: true)!.Describe(), line => line.Contains('{', StringComparison.Ordinal));
    }

    /// <summary>
    /// A restore puts back the revision's tags, so restoring one whose project tags differ from the entry's is the tag
    /// change D-0415 asks about: what joins goes first, naming the revision's variables and their placeholders, and
    /// what leaves names the entry's own. A revision whose project tags are the entry's changes no project.
    /// </summary>
    [Fact]
    public void Restoring_a_revision_whose_project_tags_differ_is_the_tag_change_it_makes()
    {
        Assert.True(_vault.SetFields(_stripe, [new FieldWrite("API_URL", "https://{USERNAME}@api")]));
        Assert.True(_vault.AddTag(_stripe, "env:billing:staging"));
        Assert.True(_vault.RemoveTags(_stripe, ["env:billing:staging"]));
        Assert.True(_vault.RemoveField(_stripe, "API_URL"));
        Assert.True(_vault.AddTag(_stripe, "env:billing"));
        Assert.True(_vault.SetFields(_stripe, [new FieldWrite("STRIPE_KEY", "s2")]));

        var history = _vault.ReadHistory(_stripe)!;
        var staging = history.Single(revision => revision.Tags is ["env:billing:staging"] && revision.CustomFields.Any(field => field.Name == "API_URL"));

        Assert.Equal(
            [
                "env:billing:staging puts services/Stripe in billing/staging.",
                "Joining billing/staging: API_URL, STRIPE_KEY, WEBHOOK.",
                "API_URL fills in {USERNAME} from services/Stripe when it leaves.",
                "Removing env:billing takes services/Stripe out of billing/dev.",
                "Leaving billing/dev: STRIPE_KEY, WEBHOOK.",
            ],
            Restoring(staging).SelectMany(change => change.Describe()));

        Assert.Empty(Restoring(history[0]));
        Assert.Equal(["env:billing"], history[0].Tags);
    }

    /// <summary>
    /// A restore that swaps the entry's tag for another reaching the same environment takes it out of none, so the
    /// preview says the environment stays as it is rather than that the entry leaves it and joins it again.
    /// </summary>
    [Fact]
    public void Restoring_a_revision_whose_tag_reaches_the_same_environment_changes_no_environment()
    {
        Assert.True(_vault.AddTag(_stripe, "env:billing"));
        Assert.True(_vault.AddTag(_stripe, "env:billing:dev"));
        Assert.True(_vault.RemoveTags(_stripe, ["env:billing"]));

        var bare = _vault.ReadHistory(_stripe)!.Single(read => read.Tags is ["env:billing"]);

        Assert.Equal(
            [
                "services/Stripe is already in billing/dev through another tag, so adding env:billing changes no environment.",
                "services/Stripe stays in billing/dev through another tag, so removing env:billing:dev changes no environment.",
            ],
            Restoring(bare).SelectMany(change => change.Describe()));
    }

    private IReadOnlyList<ProjectTagChange> Restoring(EntryRevision revision) =>
        ProjectTagChange.Restoring(_vault, _stripe, revision, ClientsPath, _now);
}
