using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Settings › Agents over the app's own authority: each change sealed by it, and Allow only once the person confirmed it.</summary>
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

        model.ConfirmAllowCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.Equal(CategoryRule.Allow, Held(FieldCategory.Password));
        Assert.True(Choice(model, FieldCategory.Password, CategoryRule.Allow).IsChosen);
    }

    [Fact]
    public void Cancel_leaves_the_rule_as_it_was()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ChooseCommand.Execute(Choice(model, FieldCategory.Notes, CategoryRule.Allow));
        model.CancelAllowCommand.Execute(null);

        Assert.False(model.IsConfirming);
        Assert.Equal(CategoryRule.Ask, Held(FieldCategory.Notes));
    }

    [Fact]
    public void Block_and_the_username_switch_take_effect_at_once()
    {
        using var model = new AgentRulesViewModel(_authority);

        model.ChooseCommand.Execute(Choice(model, FieldCategory.Attachment, CategoryRule.Block));
        model.ShowsUsername = false;

        Assert.True(_authority.TryRules(out var rules, out _));
        Assert.Equal((CategoryRule.Block, false), (rules.RuleFor(FieldCategory.Attachment), rules.ShowsUsername));
        Assert.False(model.IsIgnored);
    }

    [Fact]
    public void Rules_changed_outside_keypaste_are_said_to_be_ignored()
    {
        File.WriteAllText(KeypasteHome.AgentsPath(_fixture.Home), "[[rules]]\npassword = \"allow\"\n");

        using var model = new AgentRulesViewModel(_authority);

        Assert.True(model.IsIgnored);
        Assert.StartsWith("Agent rules were changed outside keypaste", model.Ignored, StringComparison.Ordinal);
        Assert.True(Choice(model, FieldCategory.Password, CategoryRule.Ask).IsChosen);
    }

    private CategoryRule Held(FieldCategory category)
    {
        Assert.True(_authority.TryRules(out var rules, out _));
        return rules.RuleFor(category);
    }

    private static CategoryChoice Choice(AgentRulesViewModel model, FieldCategory category, CategoryRule rule) =>
        model.Rows.Single(row => row.Category == category).Choices.Single(choice => choice.Rule == rule);
}
