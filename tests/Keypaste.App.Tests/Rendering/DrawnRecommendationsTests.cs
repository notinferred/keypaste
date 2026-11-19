using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// V-C.2 and V.9 on screen: Security draws the keys left in each item's notes and each weak or reused
/// password by item, and no frame or automation name carries a value.
/// </summary>
public sealed class DrawnRecommendationsTests
{
    private const string _keyValue = "SENTINEL-NOTE-VALUE-81c4e2";
    private const string _token = "ghp_SENTINELDRAWNTOKEN4f6a";
    private const string _stripeLogin = "SENTINEL-STRIPE-LOGIN-7c2e90";
    private const string _weak = "hunter2";

    [Fact]
    public Task Recommendations_draws_an_items_keys_as_one_finding_and_no_value_and_counts_it_without_the_accent() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = _stripeLogin, Notes = $"STRIPE_SECRET_KEY={_keyValue}\n{_token}" }));
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);

        var recommendations = shell.Show<RecommendationsViewModel>(DestinationKind.Recommendations);
        Assert.Single(recommendations.Rows).IsExpanded = true;
        RenderedShell.Drain();
        var card = shell.Named<StackPanel>("NoteKeysCard").GetVisualDescendants().OfType<TextBlock>().ToList();
        var heading = card.First(block => block.Name == "RecommendationKey" && block.Text == "2 keys in the notes of Stripe");
        var key = shell.Named<ItemsControl>("RecommendationKeys").GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "STRIPE_SECRET_KEY");
        var frame = shell.Frame();
        var mono = TextStyle.Of(key);
        var sans = TextStyle.Of(heading);

        Assert.True(frame.Shows("2 keys in the notes of Stripe", sans, frame.Of(heading, shell.Window)), "the finding is not drawn");

        Assert.True(frame.Shows("STRIPE_SECRET_KEY", mono, frame.Of(key, shell.Window)), "the key is not drawn");

        foreach (var value in new[] { _keyValue, _token })
        {
            Assert.False(frame.Shows(value, mono, frame.Everywhere), "a value left in notes is drawn in mono");
            Assert.False(frame.Shows(value, sans, frame.Everywhere), "a value left in notes is drawn in sans");
            AutomationSurface.AssertNothingExposes(shell.Window, value);
        }

        var badge = shell.Named<ListBox>("RailNav").GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailBadge" && border.IsEffectivelyVisible);
        Assert.Equal("1", Assert.IsType<TextBlock>(badge.Child).Text);
    });

    [Fact]
    public Task Recommendations_draws_each_weak_and_reused_password_by_item_and_never_the_password() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(
            seed: vault =>
            {
                vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Mail", Password = _weak });
                vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Shop", Password = RenderedShell.Current });
            });
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);

        shell.Show<RecommendationsViewModel>(DestinationKind.Recommendations);
        var card = shell.Named<ItemsControl>("PasswordGroups");
        var problem = card.GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "PasswordProblem" && block.Text == "Weak passwords");
        var item = card.GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "PasswordItem" && block.Text == "Shop");
        var where = card.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Also on github");
        var frame = shell.Frame();
        var label = TextStyle.Of(problem);
        var caption = TextStyle.Of(where);

        Assert.True(frame.Shows("Weak passwords", label, frame.Of(problem, shell.Window)), "the problem is not drawn");
        Assert.Equal(3, Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(problem).GetHeadingLevel());
        Assert.True(frame.Shows("Shop", TextStyle.Of(item), frame.Of(item, shell.Window)), "the item is not drawn");
        Assert.True(frame.Shows("Also on github", caption, frame.Of(where, shell.Window)), "what only the item has is not drawn");

        foreach (var value in new[] { _weak, RenderedShell.Current })
        {
            Assert.False(frame.Shows(value, label, frame.Everywhere), "a password is drawn in the problem's style");
            Assert.False(frame.Shows(value, caption, frame.Everywhere), "a password is drawn in the caption's style");
            AutomationSurface.AssertNothingExposes(shell.Window, value);
        }

        var badge = shell.Named<ListBox>("RailNav").GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailBadge" && border.IsEffectivelyVisible);
        Assert.Equal("3", Assert.IsType<TextBlock>(badge.Child).Text);
    });

    [Fact]
    public Task No_main_screen_draws_a_recommendation_or_a_banner_for_one() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "p", Notes = $"STRIPE_SECRET_KEY={_keyValue}" }));
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);
        Assert.NotEmpty(shell.Shell.Recommendations.Rows);

        foreach (var destination in Destinations.Places.Where(place => place.Kind != DestinationKind.Recommendations))
        {
            shell.Shell.Current = destination;
            RenderedShell.Drain();

            Assert.DoesNotContain(
                shell.Window.GetVisualDescendants().OfType<TextBlock>(),
                block => block.IsEffectivelyVisible && block.Text is "STRIPE_SECRET_KEY" or "Weak passwords");
            Assert.Null(shell.Shell.Notice);
        }
    });
}
