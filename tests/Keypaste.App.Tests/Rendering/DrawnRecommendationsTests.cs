using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// V-C.2 on screen: Settings draws each key left in notes, and no frame or automation name carries
/// a value (D-0303, D-0232).
/// </summary>
public sealed class DrawnRecommendationsTests
{
    private const string _keyValue = "SENTINEL-NOTE-VALUE-81c4e2";
    private const string _token = "ghp_SENTINELDRAWNTOKEN4f6a";

    [Fact]
    public Task Settings_draws_each_key_and_no_value_and_counts_them_without_amber() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "p", Notes = $"STRIPE_SECRET_KEY={_keyValue}\n{_token}" }));

        shell.Show<SettingsViewModel>(DestinationKind.Settings);
        var key = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Name == "RecommendationKey" && block.Text == "STRIPE_SECRET_KEY");
        var where = shell.Named<Border>("RecommendationsCard").GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "in the notes of services/Stripe");
        var frame = shell.Frame();
        var mono = TextStyle.Of(key);
        var sans = TextStyle.Of(where);

        Assert.True(frame.Shows("STRIPE_SECRET_KEY", mono, frame.Of(key, shell.Window)), "the key is not drawn");

        foreach (var value in new[] { _keyValue, _token })
        {
            Assert.False(frame.Shows(value, mono, frame.Everywhere), "a value left in notes is drawn in mono");
            Assert.False(frame.Shows(value, sans, frame.Everywhere), "a value left in notes is drawn in sans");
            AutomationSurface.AssertNothingExposes(shell.Window, value);
        }

        var count = shell.Named<ListBox>("FooterNav").GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "2");
        Assert.DoesNotContain("amber", count.Classes);
    });

    [Fact]
    public Task No_main_screen_draws_a_recommendation_or_a_banner_for_one() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "p", Notes = $"STRIPE_SECRET_KEY={_keyValue}" }));

        foreach (var destination in Destinations.Main)
        {
            shell.Shell.Current = destination;
            RenderedShell.Drain();

            Assert.DoesNotContain(
                shell.Window.GetVisualDescendants().OfType<TextBlock>(),
                block => block.IsEffectivelyVisible && block.Text is "Needs review" or "STRIPE_SECRET_KEY");
            Assert.Null(shell.Shell.Notice);
        }
    });
}
