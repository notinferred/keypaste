using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// An item's tile draws the mark of the service its website belongs to, matched by registrable domain from what the app carries,
/// an icon for what it is or the host it names, or its letter.
/// </summary>
public sealed class ItemMarksTests
{
    [Theory]
    [InlineData("https://github.com/login", "github")]
    [InlineData("github.com", "github")]
    [InlineData("https://gist.github.com/me", "github")]
    [InlineData("HTTPS://Dashboard.Stripe.COM/test/apikeys", "stripe")]
    [InlineData("https://mail.google.com", "google")]
    [InlineData("https://twitter.com/home", "x")]
    [InlineData("  https://linear.app  ", "linear")]
    public void A_site_or_its_subdomain_draws_its_services_mark(string url, string service) =>
        Assert.Equal(new ItemMark(service, null), ItemMarks.Of(url, EntryKind.Login, inProject: false));

    [Theory]
    [InlineData("https://github.com.evil.example")]
    [InlineData("https://githubb.com")]
    [InlineData("https://my-github.com")]
    [InlineData("https://me.github.io")]
    [InlineData("https://github.co")]
    [InlineData("https://xn--gthub-n4a.com")]
    [InlineData("https://user@github.com")]
    [InlineData("ftp://github.com")]
    [InlineData("https://example.com")]
    [InlineData("")]
    public void A_lookalike_another_site_or_no_website_keeps_the_letter(string url) =>
        Assert.Equal(ItemMark.Letter, ItemMarks.Of(url, EntryKind.Login, inProject: false));

    [Theory]
    [InlineData("http://localhost:3000", "laptop")]
    [InlineData("localhost", "laptop")]
    [InlineData("http://127.0.0.1:8080/admin", "laptop")]
    [InlineData("http://[::1]:5432", "laptop")]
    [InlineData("https://192.168.1.1", "ethernet-port")]
    [InlineData("10.0.0.5:8443", "ethernet-port")]
    [InlineData("http://[fe80::1]", "ethernet-port")]
    [InlineData("http://nas:5000", "house")]
    [InlineData("https://printer.local", "house")]
    [InlineData("http://router.lan", "house")]
    [InlineData("https://hub.home.arpa", "house")]
    public void A_login_on_this_computer_an_address_or_the_home_network_draws_its_hosts_icon(string url, string icon) =>
        Assert.Equal(new ItemMark(null, icon), ItemMarks.Of(url, EntryKind.Login, inProject: false));

    [Theory]
    [InlineData(nameof(EntryKind.ApiKey), false, "key")]
    [InlineData(nameof(EntryKind.Server), false, "server")]
    [InlineData(nameof(EntryKind.Database), false, "database")]
    [InlineData(nameof(EntryKind.Note), false, "file-text")]
    [InlineData(nameof(EntryKind.Card), false, "credit-card")]
    [InlineData(nameof(EntryKind.Identity), false, "id-card")]
    [InlineData(nameof(EntryKind.SshKey), false, "key-square")]
    [InlineData(nameof(EntryKind.Wifi), false, "wifi")]
    [InlineData(nameof(EntryKind.Environment), false, "braces")]
    [InlineData(nameof(EntryKind.Password), true, "braces")]
    [InlineData(nameof(EntryKind.ApiKey), true, "key")]
    public void An_item_with_no_service_draws_its_kinds_icon(string kind, bool inProject, string icon) =>
        Assert.Equal(new ItemMark(null, icon), ItemMarks.Of("http://10.0.0.5", Enum.Parse<EntryKind>(kind), inProject));

    [Fact]
    public void A_service_outranks_the_kind_and_a_plain_login_or_password_keeps_the_letter()
    {
        Assert.Equal(new ItemMark("anthropic", null), ItemMarks.Of("https://console.anthropic.com", EntryKind.ApiKey, inProject: true));
        Assert.Equal(ItemMark.Letter, ItemMarks.Of(string.Empty, EntryKind.Password, inProject: false));
    }

    [Fact]
    public Task Every_service_has_its_mark_and_colour_and_every_icon_its_outline() => HeadlessSession.On(() =>
    {
        var app = Application.Current!;

        Assert.All(ItemMarks.Services, service =>
        {
            Assert.True(app.TryGetResource("Service." + service, ThemeVariant.Default, out var mark) && mark is Geometry, service);
            Assert.True(app.TryGetResource("ServiceColor." + service, ThemeVariant.Default, out var colour) && colour is Color, service);
        });
        Assert.All(new[] { "laptop", "ethernet-port", "house" }.Concat(Enum.GetValues<EntryKind>().Select(EntryKinds.Icon)), icon =>
            Assert.True(app.TryGetResource("Icon." + icon, ThemeVariant.Default, out var outline) && outline is Geometry, icon));
    });

    [Theory]
    [InlineData("#181717", "#FFFFFF")]
    [InlineData("#1ED760", "#1D1D1F")]
    [InlineData("#FFFC00", "#1D1D1F")]
    public void A_mark_takes_white_or_ink_whichever_reads_on_its_brands_colour(string brand, string ink) =>
        Assert.Equal(Color.Parse(ink), Monogram.InkOn(Color.Parse(brand)));
}
