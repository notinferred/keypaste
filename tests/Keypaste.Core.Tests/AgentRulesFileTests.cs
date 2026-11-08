using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary><c>agents.toml</c>: what every agent gets on this computer, which an absent file leaves at the defaults and a file keypaste cannot read refuses.</summary>
public sealed class AgentRulesFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-agent-rules-").FullName;

    private AgentRulesFile File => new(Path.Combine(_directory, "agents.toml"));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_absent_file_asks_about_every_value_shows_usernames_and_runs_nothing()
    {
        Assert.True(File.TryRead(out var rules, out var problem), problem);

        Assert.True(rules.ShowsUsername);
        Assert.All(FieldCategories.Ruled, category => Assert.Equal(CategoryRule.Ask, rules.RuleFor(category)));
        Assert.Equal(CategoryRule.Block, rules.RuleFor(FieldCategory.Passkey));
        Assert.False(rules.Runs("claude-code"));
    }

    [Fact]
    public void What_is_written_reads_back_as_itself()
    {
        var written = (AgentRules.Defaults with { ShowsUsername = false })
            .Allowing(FieldCategory.ProjectVariable, confirmed: true)
            .With(FieldCategory.Notes, CategoryRule.Block)
            .WithRun("claude-code", run: true)
            .WithRun("cursor", run: false);

        Assert.True(File.TryWrite(written, "0123abcd", out var problem), problem);
        Assert.True(File.TryReadFile(out var file, out problem), problem);
        var read = file.Rules;

        Assert.Equal(written, read);
        Assert.Equal(("0123abcd", true), (file.Seal, file.Present));
        Assert.True(read.Runs("claude-code"));
        Assert.False(read.Runs("cursor"));
        Assert.Equal(CategoryRule.Allow, read.RuleFor(FieldCategory.ProjectVariable));
    }

    [Fact]
    public void An_agents_own_rules_and_remembered_answers_read_back_as_themselves()
    {
        const string item = "0123456789abcdef0123456789abcdef";
        var written = AgentRules.Defaults
            .With("claude-code", FieldCategory.Password, CategoryRule.Block)
            .Allowing("claude-code", FieldCategory.Notes, confirmed: true)
            .Remembering(new RememberedAnswer("claude-code", item, "password", CategoryRule.Allow), confirmed: true)
            .Remembering(new RememberedAnswer("cursor", item, "attachment:deploy key.pem", CategoryRule.Block), confirmed: true)
            .Remembering(new RememberedPick("claude-code", "services/Mail", "password", item.ToUpperInvariant()), confirmed: true);

        Assert.True(File.TryWrite(written, seal: null, out var problem), problem);
        Assert.True(File.TryRead(out var read, out problem), problem);

        Assert.Equal(written, read);
        Assert.Equal(AgentRulesFile.Canonical(written), AgentRulesFile.Canonical(read));
        Assert.Equal(CategoryRule.Block, read.RuleFor("claude-code", FieldCategory.Password));
        Assert.Null(read.RuleFor("cursor", FieldCategory.Password));
        Assert.Equal(item, read.PickFor("claude-code", "services/Mail", "password"));
        Assert.Null(read.PickFor("cursor", "services/Mail", "password"));
    }

    [Theory]
    [InlineData("[[agent]]\nname = \"a\"\npassword = \"sometimes\"\n", "line 3")]
    [InlineData("[[agent]]\nname = \"a\"\npasskey = \"allow\"\n", "line 3")]
    [InlineData("[[always]]\nagent = \"a\"\nitem = \"0123\"\nfield = \"password\"\nrule = \"allow\"\n", "line 1")]
    [InlineData("[[always]]\nagent = \"a\"\nitem = \"0123456789abcdef0123456789abcdef\"\nfield = \"password\"\nrule = \"ask\"\n", "line 1")]
    [InlineData("[[pick]]\nagent = \"*\"\nname = \"Mail\"\nfield = \"password\"\nitem = \"0123456789abcdef0123456789abcdef\"\n", "line 1")]
    [InlineData("[[rules]]\npassword = \"sometimes\"\n", "line 2")]
    [InlineData("[[rules]]\npasskey = \"allow\"\n", "line 2")]
    [InlineData("[[client]]\nlabel = \"claude-code\"\n", "line 1")]
    [InlineData("[[agent]]\nname = \"claude-code\"\nrun = 2\n", "line 3")]
    [InlineData("[[agent]]\nname = \"a\"\n[[agent]]\nname = \"a\"\n", "same agent")]
    public void A_file_keypaste_cannot_read_refuses_and_is_left_as_it_is(string text, string said)
    {
        var path = Path.Combine(_directory, "agents.toml");
        System.IO.File.WriteAllText(path, text);

        Assert.False(File.TryRead(out var rules, out var problem));
        Assert.Null(rules);
        Assert.Contains(said, problem, StringComparison.Ordinal);

        Assert.False(File.TryWrite(AgentRules.Defaults, seal: null, out _));
        Assert.Equal(text, System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void A_home_has_one_random_identity_made_once()
    {
        Assert.Null(File.HomeId());
        Assert.True(File.TryEnsureHomeId(out var made, out var problem), problem);
        Assert.True(File.TryEnsureHomeId(out var again, out problem), problem);

        Assert.Equal(32, made.Length);
        Assert.Equal(made, again);
        Assert.Equal(made, File.HomeId());
    }

    [Fact]
    public void Removing_an_agent_turns_its_run_off()
    {
        var rules = AgentRules.Defaults.WithRun("claude-code", run: true).Without("claude-code");

        Assert.False(rules.Runs("claude-code"));
        Assert.Empty(rules.Agents);
        Assert.Throws<ArgumentException>(() => AgentRules.Defaults.WithRun("*", run: true));
    }
}
