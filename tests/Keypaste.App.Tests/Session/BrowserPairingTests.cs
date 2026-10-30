using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Pairing a browser with the app (8.1d): Settings › Browser fill's Pair a browser arms it for two minutes and one pairing; armed,
/// the app's own window names the build the browser named, Deny focused, Pair armed after a moment and only for a name a new
/// pairing can take; unarmed, the request is refused unasked and the app says how to pair; Settings lists each pairing and
/// Remove ends it (D-0634, D-0456).
/// </summary>
public sealed partial class BrowserPairingTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [Fact]
    public Task Pair_a_browser_lets_one_browser_pair_under_the_name_typed_and_Settings_lists_it() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        Assert.True(browserFill.HasNoRows);

        browserFill.PairCommand.Execute(null);

        Assert.True(browserFill.IsArmed);
        Assert.False(browserFill.PairCommand.CanExecute(null));
        Assert.StartsWith("Pairing is on for 2:00.", browserFill.ArmedText, StringComparison.Ordinal);

        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync());

        Assert.Equal("KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)", Text(window, "IdentityText"));
        Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
        Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.IsDefault);
        Assert.False(string.IsNullOrEmpty(window.FindControl<TextBox>("NameBox")!.Text));

        window.FindControl<TextBox>("NameBox")!.Text = "work laptop";
        await app.ArmAsync("Allow");
        Click(window, "Allow");

        var reply = await pairing.WaitAsync(_wait, Token);
        Assert.Equal(new BrowserReply(BrowserOutcome.Done, string.Empty) { Pairing = "work laptop" }, reply);
        await PromptedApp.WithdrawnAsync(window);

        using var listed = new BrowserFillViewModel(app.Authority, app.Clock);
        var row = Assert.Single(listed.Rows);
        Assert.Equal(new PairingItem("work laptop", "KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)", row.Used), row);
        Assert.Contains("last used", row.Used, StringComparison.Ordinal);
        Assert.False(listed.IsArmed);
    });

    [Fact]
    public Task Pair_stays_disarmed_for_a_name_another_pairing_has_and_Deny_pairs_nothing() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");

        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        browserFill.PairCommand.Execute(null);
        await using var link = await AttachAsync(app, BrowserIdentity.FirefoxAddOns);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync(2));

        Assert.Equal("KeePassXC-Browser from Firefox Add-ons (keepassxc-browser@keepassxc.org)", Text(window, "IdentityText"));
        window.FindControl<TextBox>("NameBox")!.Text = "work laptop";
        app.Clock.Advance(PromptViewModel.ArmingDelay);
        await PromptedApp.UntilAsync(() => Text(window, "NameProblemText") == "another pairing already has this name");
        await Task.Delay(200, Token);
        Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);

        Click(window, "Deny");

        Assert.Equal(BrowserOutcome.Refused, (await pairing.WaitAsync(_wait, Token))?.Outcome);
        Assert.Single(new BrowserFillViewModel(app.Authority, app.Clock).Rows);
    });

    [Fact]
    public Task Unarmed_a_request_to_pair_is_refused_unasked_and_the_app_says_how_to_pair() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await using var link = await AttachAsync(app, BrowserIdentity.EdgeAddOns);

        var reply = await link.PairAsync(Pair(app), Token);
        var notice = Assert.IsType<PairingNoticeWindow>(await app.PromptAsync());

        Assert.Equal(BrowserOutcome.Refused, reply?.Outcome);
        Assert.Equal(PairingNotice.AppText, Text(notice, "NoticeText"));
        Assert.Equal("KeePassXC-Browser from Microsoft Edge Add-ons (pdffhmdngciaglkoonimfcmckehcpafo)", Text(notice, "IdentityText"));
        Assert.Empty(new BrowserFillViewModel(app.Authority, app.Clock).Rows);

        var again = await link.PairAsync(Pair(app), Token);
        Assert.Equal(BrowserOutcome.Refused, again?.Outcome);
        Assert.Single(app.Windows);
        notice.Close();
    });

    [Fact]
    public Task Remove_ends_a_pairing_and_its_next_check_is_refused() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check(app, "work laptop"), Token))?.Outcome);

        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        browserFill.RemoveCommand.Execute(Assert.Single(browserFill.Rows));

        Assert.Empty(browserFill.Rows);
        Assert.StartsWith("Removed the pairing work laptop.", browserFill.Message, StringComparison.Ordinal);
        Assert.Equal(BrowserOutcome.NotPaired, (await link.CheckAsync(Check(app, "work laptop"), Token))?.Outcome);
    });

    /// <summary>
    /// A confirmation on screen when the desktop merges another program's save is withdrawn with what else waited on the person: its
    /// window closes, the browser is refused, and no pairing is saved (D-0542, D-0634).
    /// </summary>
    [Fact]
    public Task A_confirmation_waiting_when_another_programs_save_is_merged_is_withdrawn_and_pairs_nothing() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        browserFill.PairCommand.Execute(null);
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync());

        using (var other = Core.Vault.Open(app.Vault, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { GroupPath = "services", Title = "Fly", Password = "external" });
            other.Save();
        }

        Assert.True((await Task.Run(() => app.Authority.Session.Merge(), Token))?.Merged);
        await PromptedApp.WithdrawnAsync(window);

        var reply = await pairing.WaitAsync(_wait, Token);
        Assert.Equal(BrowserOutcome.Refused, reply?.Outcome);
        Assert.EndsWith("before the pairing was saved", reply!.Reason, StringComparison.Ordinal);
        Assert.Empty(new BrowserFillViewModel(app.Authority, app.Clock).Rows);
    });

    /// <summary>The card says nothing in the words D-0457 retired and names every control by its words.</summary>
    [Fact]
    public Task The_Browser_fill_card_holds_no_retired_word_and_names_its_controls() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");
        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        browserFill.PairCommand.Execute(null);
        using var settings = new SettingsViewModel(app.Authority.Session, app.Home, new DesktopPreferences(app.Home), _ => { }) { BrowserFill = browserFill };
        var window = new Window { Content = new SettingsView { DataContext = settings } };
        window.Show();
        WindowInput.Drain();

        var card = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "BrowserFillCard");
        Assert.True(card.IsEffectivelyVisible);

        var said = card.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty).ToList();
        Assert.Contains("Browser fill", said);
        Assert.Contains("work laptop", said);
        Assert.DoesNotContain(said, text => Retired().IsMatch(text));

        var names = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
            .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToList();
        Assert.Equal(["Stop pairing", "Remove this pairing"], names);

        window.Close();
    });

    private static BrowserPairRequest Pair(PromptedApp app) => new(_key) { Vault = app.Vault, Session = app.SessionId };

    private static BrowserCheckRequest Check(PromptedApp app, string pairing) => new(pairing, _key) { Vault = app.Vault, Session = app.SessionId };

    private static async Task<BrowserLink> AttachAsync(PromptedApp app, BrowserIdentity browser)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(app.Authority.Status).Endpoint;
        var link = await BrowserLink.TryConnectAsync(endpoint, _wait, Token);
        Assert.NotNull(link);
        var attached = await link.AttachAsync(new AttachRequest(app.Vault) { Browser = browser.Caller }, Token);
        Assert.True(attached?.Attached, attached?.Reason);
        Assert.Matches("^[0-9a-f]{64}$", attached!.DatabaseHash!);
        return link;
    }

    private static async Task PairAsync(PromptedApp app, string name)
    {
        using var browserFill = new BrowserFillViewModel(app.Authority, app.Clock);
        browserFill.PairCommand.Execute(null);
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = await app.PromptAsync(app.Windows.Count + 1);
        window.FindControl<TextBox>("NameBox")!.Text = name;
        await app.ArmAsync("Allow");
        Click(window, "Allow");
        Assert.Equal(name, (await pairing.WaitAsync(_wait, Token))?.Pairing);
        await PromptedApp.WithdrawnAsync(window);
    }
}
