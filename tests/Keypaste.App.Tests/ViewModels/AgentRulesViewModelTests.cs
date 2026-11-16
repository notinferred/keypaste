using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Settings › Agents over the app's own authority: each change sealed by it, and Allow or a switch turned on only once the person confirmed it.</summary>
public sealed class AgentRulesViewModelTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly AppAuthority _authority;

    public AgentRulesViewModelTests()
    {
#pragma warning disable CA2000 // The authority owns the session and disposes it.
        _authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home), null, () => new NobodyToAsk());
#pragma warning restore CA2000

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _authority.Session.TryUnlock(_fixture.Path_, master.Value));
    }

    public void Dispose()
    {
        _authority.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Allow_holds_only_once_the_person_confirms_it_and_names_what_it_releases()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ChooseCommand.Execute(Choice(model, FieldCategory.Password, CategoryRule.Allow));

        Assert.True(model.IsConfirming);
        Assert.Equal("Every agent will receive every password value without asking.", model.ConfirmText);
        Assert.Equal(CategoryRule.Ask, Held(FieldCategory.Password));

        model.ConfirmGrantCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.Equal(CategoryRule.Allow, Held(FieldCategory.Password));
        Assert.True(Choice(model, FieldCategory.Password, CategoryRule.Allow).IsChosen);
    }

    [Fact]
    public void Cancel_leaves_the_rule_as_it_was()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ChooseCommand.Execute(Choice(model, FieldCategory.Notes, CategoryRule.Allow));
        model.CancelGrantCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.Equal(CategoryRule.Ask, Held(FieldCategory.Notes));
    }

    [Fact]
    public void Block_and_turning_the_username_switch_off_take_effect_at_once()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ChooseCommand.Execute(Choice(model, FieldCategory.Attachment, CategoryRule.Block));
        model.ShowsUsername = false;

        Assert.False(model.IsConfirming);
        Assert.Equal((CategoryRule.Block, false), (Held(FieldCategory.Attachment), Rules().ShowsUsername));
        Assert.False(model.IsIgnored);
    }

    [Fact]
    public void Turning_usernames_on_holds_only_once_the_person_confirms_what_agents_will_see()
    {
        using var model = new AgentRulesViewModel(_authority);
        model.ShowsUsername = false;
        var redrawn = Redrawn(model);

        model.ShowsUsername = true;

        Assert.True(model.IsConfirming);
        Assert.Equal("Every agent will see each item's username in its searches and receive it without asking. What an agent sees reaches its AI tool's provider.", model.ConfirmText);
        Assert.Equal("Show usernames", model.ConfirmAction);
        Assert.Contains(nameof(AgentRulesViewModel.ShowsUsername), redrawn);
        Assert.False(model.ShowsUsername);
        Assert.False(Rules().ShowsUsername);

        model.ConfirmGrantCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.True(model.ShowsUsername);
        Assert.True(Rules().ShowsUsername);
    }

    [Fact]
    public void Cancel_leaves_usernames_hidden()
    {
        using var model = new AgentRulesViewModel(_authority);
        model.ShowsUsername = false;

        model.ShowsUsername = true;
        model.CancelGrantCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.False(model.ShowsUsername);
        Assert.False(Rules().ShowsUsername);
    }

    [Fact]
    public void Hosts_and_references_start_hidden_turn_on_once_confirmed_and_off_at_once()
    {
        using var model = new AgentRulesViewModel(_authority);
        Assert.False(model.ShowsLinkedHosts);
        var redrawn = Redrawn(model);

        model.ShowsLinkedHosts = true;

        Assert.True(model.IsConfirming);
        Assert.Equal("Every agent's searches will show the host in each item's Host field and which items its fields reference. What an agent sees reaches its AI tool's provider.", model.ConfirmText);
        Assert.Equal("Show hosts and references", model.ConfirmAction);
        Assert.Contains(nameof(AgentRulesViewModel.ShowsLinkedHosts), redrawn);
        Assert.False(model.ShowsLinkedHosts);
        Assert.False(Rules().ShowsLinkedHosts);

        model.ConfirmGrantCommand.Execute(null);

        Assert.True(model.ShowsLinkedHosts);
        Assert.True(Rules().ShowsLinkedHosts);
        Assert.Equal("Agents' searches show hosts and references.", model.Message);

        model.ShowsLinkedHosts = false;

        Assert.False(model.IsConfirming);
        Assert.False(Rules().ShowsLinkedHosts);
        Assert.False(model.IsIgnored);
    }

    [Fact]
    public void Cancel_leaves_hosts_and_references_hidden()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ShowsLinkedHosts = true;
        model.CancelGrantCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.False(model.ShowsLinkedHosts);
        Assert.False(Rules().ShowsLinkedHosts);
    }

    [Fact]
    public void Rules_changed_outside_keypaste_are_said_to_be_ignored()
    {
        File.WriteAllText(KeypasteHome.AgentsPath(_fixture.Home), "[[rules]]\npassword = \"allow\"\n");

        using var model = new AgentRulesViewModel(_authority);

        Assert.True(model.IsIgnored);
        Assert.StartsWith("Agent rules and browser fill allowances were changed outside keypaste", model.Ignored, StringComparison.Ordinal);
        Assert.True(Choice(model, FieldCategory.Password, CategoryRule.Ask).IsChosen);
    }

    private CategoryRule Held(FieldCategory category) => Rules().RuleFor(category);

    private AgentRules Rules()
    {
        Assert.True(_authority.TryRules(out var rules, out _));
        return rules;
    }

    /// <summary>The properties the model says changed from now on, as a bound switch reads them again.</summary>
    private static List<string?> Redrawn(AgentRulesViewModel model)
    {
        List<string?> changed = [];
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    private static CategoryChoice Choice(AgentRulesViewModel model, FieldCategory category, CategoryRule rule) =>
        model.Rows.Single(row => row.Category == category).Choices.Single(choice => choice.Rule == rule);
}
