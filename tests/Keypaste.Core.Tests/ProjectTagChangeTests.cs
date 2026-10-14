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
        Assert.Equal("While the entry carries it, every agent request for the entry is asked live.", described[1]);
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
}
