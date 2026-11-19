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
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Pairing a browser with the app: a request brings the app's own window at once, with the code the extension shows and the build
/// the browser named, Deny focused, Pair armed after a moment and only for a name a new pairing can take; a Deny holds the next
/// request back unasked; Settings lists each pairing as it is made and Remove ends it.
/// </summary>
public sealed partial class BrowserPairingTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static readonly string _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [Fact]
    public Task A_request_to_pair_shows_the_code_at_once_pairs_under_the_name_typed_and_Settings_lists_it() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        using var browserFill = new BrowserFillViewModel(app.Authority);
        Assert.True(browserFill.HasNoRows);

        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync());

        Assert.Equal("Pair Chrome with keypaste?", Text(window, "TitleText"));
        Assert.Equal(PairingCode.Of(_key), Text(window, "CodeText"));
        Assert.Equal("Pair only if Chrome shows the same code.", Text(window, "CheckText"));
        Assert.Equal("keypaste's extension from the Chrome Web Store (fipabobollckgdobkjfkkchimlphcicg)", Text(window, "IdentityText"));
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

        var row = Assert.Single(browserFill.Rows);
        Assert.Equal(("work laptop", "keypaste's extension from the Chrome Web Store (fipabobollckgdobkjfkkchimlphcicg)"), (row.Name, row.Identity));
        Assert.StartsWith("Chrome · last used", row.Summary, StringComparison.Ordinal);
    });

    [Fact]
    public Task Pair_stays_disarmed_for_a_name_another_pairing_has_and_Deny_pairs_nothing() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");

        await using var link = await AttachAsync(app, BrowserIdentity.FirefoxAddOns);
        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync(2));

        Assert.Equal("keypaste's extension from Firefox Add-ons (browser@keypaste.com)", Text(window, "IdentityText"));
        window.FindControl<TextBox>("NameBox")!.Text = "work laptop";
        app.Clock.Advance(PromptViewModel.ArmingDelay);
        await PromptedApp.UntilAsync(() => Text(window, "NameProblemText") == "Another pairing has this name.");
        await Task.Delay(200, Token);
        Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);

        Click(window, "Deny");

        Assert.Equal(BrowserOutcome.Refused, (await pairing.WaitAsync(_wait, Token))?.Outcome);
        Assert.Single(new BrowserFillViewModel(app.Authority).Rows);
    });

    [Fact]
    public Task A_Deny_holds_the_next_request_to_pair_back_and_shows_nothing() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await using var link = await AttachAsync(app, BrowserIdentity.EdgeAddOns);

        var pairing = link.PairAsync(Pair(app), Token).AsTask();
        var window = Assert.IsType<PairingWindow>(await app.PromptAsync());
        Click(window, "Deny");
        Assert.Equal(BrowserOutcome.Refused, (await pairing.WaitAsync(_wait, Token))?.Outcome);
        await PromptedApp.WithdrawnAsync(window);

        var again = await link.PairAsync(Pair(app), Token);

        Assert.Equal(BrowserOutcome.Refused, again?.Outcome);
        Assert.Contains("connect again in", again!.Reason, StringComparison.Ordinal);
        Assert.Single(app.Windows);
        Assert.Empty(new BrowserFillViewModel(app.Authority).Rows);
    });

    [Fact]
    public Task Remove_ends_a_pairing_and_its_next_check_is_refused() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check(app, "work laptop"), Token))?.Outcome);

        using var browserFill = new BrowserFillViewModel(app.Authority);
        var pairing = Assert.Single(browserFill.Rows);
        browserFill.AskRemoveCommand.Press(pairing);
        Assert.True(pairing.IsConfirmingRemove);
        Assert.Single(app.Authority.Pairings());
        browserFill.CancelRemoveCommand.Press(pairing);
        Assert.False(pairing.IsConfirmingRemove);
        browserFill.AskRemoveCommand.Press(pairing);
        browserFill.RemoveCommand.Press(pairing);

        Assert.Empty(browserFill.Rows);
        Assert.StartsWith("Removed the pairing work laptop.", browserFill.Message, StringComparison.Ordinal);
        Assert.Equal(BrowserOutcome.NotPaired, (await link.CheckAsync(Check(app, "work laptop"), Token))?.Outcome);
    });

    /// <summary>
    /// A confirmation on screen when the desktop merges another program's save is withdrawn with what else waited on the person: its
    /// window closes, the browser is refused, and no pairing is saved.
    /// </summary>
    [Fact]
    public Task A_confirmation_waiting_when_another_programs_save_is_merged_is_withdrawn_and_pairs_nothing() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
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
        Assert.Empty(new BrowserFillViewModel(app.Authority).Rows);
    });

    /// <summary>The Browser section says nothing in retired words and names every control by its words.</summary>
    [Fact]
    public Task The_Browser_fill_card_holds_no_retired_word_and_names_its_controls() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync();
        await PairAsync(app, "work laptop");
        using var browserFill = new BrowserFillViewModel(app.Authority);
        using var settings = new SettingsViewModel(app.Authority.Session, app.Home, new DesktopPreferences(app.Home), _ => { }) { BrowserFill = browserFill, Section = SettingsSectionChoice.Browser };
        var window = new Window { Content = new SettingsView { DataContext = settings } };
        window.Show();
        WindowInput.Drain();

        var card = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "BrowserFillCard");
        Assert.True(card.IsEffectivelyVisible);

        var said = card.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty).ToList();
        Assert.Contains("Paired browsers", said);
        Assert.Contains("work laptop", said);
        Assert.Contains("Fills without asking", said);
        Assert.DoesNotContain(Keypaste.App.Resources.Strings.PairInTheExtension, said);
        Assert.DoesNotContain(said, text => Retired().IsMatch(text));

        var names = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)
            .Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToList();
        Assert.Equal(["Remove the pairing work laptop"], names);

        window.Close();
    });

    [Fact]
    public Task A_first_pick_on_a_site_opens_a_confirmation_that_names_the_login_and_the_site_and_shows_no_password() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync(seed: vault =>
        {
            vault.AddEntry(new VaultEntry { GroupPath = "web", Title = "GitHub", Username = "dana", Password = "fill-s3cret", Url = "https://github.com" });
            vault.AddEntry(new VaultEntry { GroupPath = "web", Title = "GitHub work", Username = "dana-work", Password = "work-s3cret", Url = "https://github.com" });
        });
        await PairAsync(app, "laptop");
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check(app, "laptop"), Token))?.Outcome);

        var listed = await link.ListAsync(List(app, "https://github.com/login"), Token);
        Assert.Equal(["GitHub", "GitHub work"], listed!.Names.Select(name => name.Title));
        Assert.Single(app.Windows);

        var filling = link.ReleaseAsync(Release(app, "https://github.com/login", listed.Names[1].Uuid), Token).AsTask();
        var window = Assert.IsType<FillWindow>(await app.PromptAsync(2));

        Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
        Assert.False(window.FindControl<Button>("Allow")!.IsEffectivelyEnabled);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.IsDefault);
        var title = window.FindControl<TextBlock>("TitleText")!;
        Assert.Equal("Fill on github.com?", title.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Aggregate(string.Empty, (text, run) => text + run.Text).Replace("\u200b", string.Empty, StringComparison.Ordinal));
        Assert.Equal("Fill GitHub work on github.com?", Avalonia.Automation.AutomationProperties.GetName(title));
        Assert.Equal("https://github.com:443", window.FindControl<TextBlock>("OriginText")!.Text);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => (text.Text ?? string.Empty).Contains("s3cret", StringComparison.Ordinal));

        await app.ArmAsync("Allow");
        Click(window, "Allow");

        var reply = await filling.WaitAsync(_wait, Token);
        Assert.Equal("work-s3cret", Assert.Single(reply!.Logins).Password);
        await PromptedApp.WithdrawnAsync(window);
        Assert.Single(new BrowserFillViewModel(app.Authority).Allowances);
    });

    [Fact]
    public Task A_site_filled_before_fills_at_once_is_listed_in_Settings_and_forgetting_it_asks_again() => HeadlessSession.On(async () =>
    {
        await using var app = await PromptedApp.StartAsync(seed: vault =>
            vault.AddEntry(new VaultEntry { Title = "GitHub", Username = "dana", Password = "fill-s3cret", Url = "https://github.com" }));
        await PairAsync(app, "laptop");
        await using var link = await AttachAsync(app, BrowserIdentity.ChromeWebStore);
        Assert.Equal(BrowserOutcome.Done, (await link.CheckAsync(Check(app, "laptop"), Token))?.Outcome);
        var item = Assert.Single((await link.ListAsync(List(app, "https://github.com/login"), Token))!.Names).Uuid;

        var filling = link.ReleaseAsync(Release(app, "https://github.com/login", item), Token).AsTask();
        var window = Assert.IsType<FillWindow>(await app.PromptAsync(2));
        await app.ArmAsync("Allow");
        Click(window, "Allow");
        Assert.Single((await filling.WaitAsync(_wait, Token))!.Logins);
        await PromptedApp.WithdrawnAsync(window);

        using var browserFill = new BrowserFillViewModel(app.Authority);
        var row = Assert.Single(browserFill.Allowances);
        Assert.Equal(("GitHub", "github.com in laptop · until you remove it"), (row.Item, row.Where));

        var atOnce = await link.ReleaseAsync(Release(app, "https://github.com/", item), Token);
        Assert.Single(atOnce!.Logins);
        Assert.Equal(2, app.Windows.Count);

        browserFill.AskUnallowCommand.Execute(row);
        Assert.True(row.IsConfirmingStop);
        Assert.Single(browserFill.Allowances);
        browserFill.UnallowCommand.Execute(row);
        Assert.Empty(browserFill.Allowances);
        var askedAgain = link.ReleaseAsync(Release(app, "https://github.com/", item), Token).AsTask();
        var again = Assert.IsType<FillWindow>(await app.PromptAsync(3));
        Click(again, "Deny");
        Assert.Empty((await askedAgain.WaitAsync(_wait, Token))!.Logins);
    });

    private static BrowserListRequest List(PromptedApp app, string url) =>
        new("laptop", _key, url) { Vault = app.Vault, Session = app.SessionId };

    private static BrowserReleaseRequest Release(PromptedApp app, string url, string item) =>
        new("laptop", _key, url, item) { Vault = app.Vault, Session = app.SessionId };

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
