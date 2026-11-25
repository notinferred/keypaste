using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Internal;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.JourneyDriver;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// From the four places, through the app as launch composes it, every act the old
/// sidebar reached is still reached, and the sidebar lists the four places and the projects only.
/// </summary>
/// <remarks>
/// <para>
/// Controls are pressed where the window draws them, and keys typed into it, so a control that is
/// hidden, covered or unbound fails here; no command is called directly.
/// </para>
/// <para>
/// As in <see cref="ClosingTheMainWindowTests"/>, the lifetime is attached through its internal
/// <c>SubscribeGlobalEvents</c> and the shutdown a closed window asks for is cancelled, because this
/// assembly shares one dispatcher. The vault is locked before the window closes: with a shell
/// showing, quitting calls a <c>Shutdown</c> that cannot be cancelled.
/// </para>
/// <para>
/// The home is the process's own (<see cref="IsolatedHome"/>), which other tests write to, so the
/// journey asserts only on records carrying its own client name.
/// </para>
/// </remarks>
public sealed class FourPlacesJourneyTests
{
    private const string _client = "four-places-journey";

    /// <summary>The bridge's client label, which History names the agent by.</summary>
    private const string _label = "four-places";
    private const string _sourcePassword = "foreign-source-password";

    [Fact]
    public Task Every_act_is_reached_from_the_four_places() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "journey.kdbx");

            using (var created = ClaimedVault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Username = "me", Password = "gh-journey" });
                ProjectVariables.Set(created, "billing", "STRIPE_KEY", "sk-journey");
                TempVault.Sees(created, _label);
                created.Save();
            }

            var foreign = Path.Combine(fixture.Home, "foreign.kdbx");
            KeePassInterop.WriteForeignUnchecked(foreign, Encoding.UTF8.GetBytes(_sourcePassword), null, "Argon2id", "ChaCha20");
            var picker = new FakeVaultFilePicker { ExistingPath = foreign };
            using var server = new FakeShareServer { Now = DateTimeOffset.UtcNow };

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Pickers = _ => picker, ShareTransport = server };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Width = 1280;
            main.Height = 800;
            main.Show();
            var root = main.FindControl<ContentControl>("Root")!;

            // Unlock through the unlock screen, as a person does.
            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            Assert.True(unlock.Offer(vault, null));
            WindowInput.Drain();
            WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
            WindowInput.Type(main, TempVault.Password);
            Key(main, PhysicalKey.Enter, RawInputModifiers.None);
            await Until(() => root.Content is ShellView);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);
            DrawnFrame.Capture(main);

            // The rail lists the four places and Access for later; the sidebar the one vault's places, with no account around them, and there is one search.
            var rail = Names(Named<ListBox>(main, "RailNav"));
            Assert.Equal("Items", rail[0]);
            Assert.StartsWith("Agents", rail[1], StringComparison.Ordinal);
            Assert.StartsWith("Security", rail[2], StringComparison.Ordinal);
            Assert.Equal(["Access, later", "Settings"], rail[3..]);
            var sidebar = Names(Named<ListBox>(main, "Nav"));
            Assert.Equal(["Favorites", "All items"], sidebar[..2]);
            Assert.DoesNotContain(AccountSection.LocalTitle, sidebar);
            Assert.Contains("Environments", sidebar);
            Assert.Equal("Trash", sidebar[^1]);
            Assert.DoesNotContain("Groups", sidebar);
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<Control>(), control => control.Name is "Search" or "McpCard");
            Assert.Equal(["ListSearch"], main.GetVisualDescendants().OfType<TextBox>().Where(box => box.Classes.Contains("search")).Select(box => box.Name));

            // An item found by the one search, in the vault's one flat list.
            Chord(main, PhysicalKey.Digit1);
            Chord(main, PhysicalKey.F);
            WindowInput.Type(main, "git");
            var items = Assert.IsType<EntriesViewModel>(shell.Content);
            Press(main, Row(main, "Rows", row => row is EntryRow { Title: "github" }));
            Assert.Equal("github", items.Selected?.Title);
            AccentElements.AssertOne(main, Named<Button>(main, "CopyPassword"), "Items, an item open");

            // A project's variables, from its row under the vault's Environments; the vault's All items returns to Items.
            Press(main, Row(main, "Nav", row => row is PlaceRow { Place: ItemPlace.Environments }));
            Press(main, Row(main, "Nav", row => row is ProjectRow { Name: "billing" }));
            var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
            Assert.Equal(["STRIPE_KEY"], env.OpenProject!.Variables.Select(row => row.Key));
            Assert.True(Named<Button>(main, "RunProject").IsEffectivelyEnabled);
            AccentElements.AssertOne(main, Named<Button>(main, "RunProject"), "a project's variables, whose Run… copies the run command where no terminal opens");
            Assert.False(shell.HasBack);
            Press(main, Row(main, "Nav", row => row is PlaceRow { Place: ItemPlace.All, IsTop: false }));
            Assert.IsType<EntriesViewModel>(shell.Content);

            // A KDBX import, from the File menu's Import.
            app.Menus!.All.Single(command => command.Header == AppCommands.ImportHeader).Run();
            WindowInput.Drain();
            await Until(() => shell.HasImport);
            Press(main, Named<Button>(main, "ChooseCopy"));
            WindowInput.Click(main, Named<MaskedInput>(main, "ImportPassword"));
            WindowInput.Type(main, _sourcePassword);
            Press(main, main.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, shell.Import!.UnlockCommand)));
            await Until(() => shell.Import is { CanConfirm: true });
            AccentElements.AssertOne(main, Named<Button>(main, "ImportConfirm"), "the import dialog");
            Press(main, Named<Button>(main, "ImportConfirm"));
            await Until(() => !shell.HasImport);
            Assert.Contains(app.Authority!.Session.Unlocked!.ReadEntries(), entry => entry.Title == "Checking");

            // Share…, from the item's ⋯ menu.
            Chord(main, PhysicalKey.Digit1);
            items = Assert.IsType<EntriesViewModel>(shell.Content);
            Press(main, Row(main, "Rows", row => row is EntryRow { Title: "github" }));
            var paneMenu = Named<Button>(main, "EntryMenu");
            Press(main, paneMenu);
            var menu = (MenuFlyout)paneMenu.Flyout!;
            Menus.Choose(menu, Menus.Item(menu, "ShareEntry"));
            Assert.True(shell.HasShare);
            AccentElements.AssertOne(main, Named<Button>(main, "CreateLink"), "the share dialog");
            Press(main, Named<Button>(main, "CreateLink"));
            await Until(() => !shell.HasShare);
            Assert.Single(server.Shares);

            // An agent's request, approved in the app's own prompt, which the app records before it answers.
            var session = app.Authority.Session.SessionId!;
            await using var client = await ConnectAsync(app.Authority, vault);
            var reply = client.RequestAsync(Request(vault, session), Token).AsTask();
            await Until(() => lifetime.Windows.Any(window => window is ApprovalWindow { IsVisible: true }));
            var prompt = lifetime.Windows.OfType<ApprovalWindow>().Single(window => window.IsVisible);
            await Until(() => prompt.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled && prompt.HasBeenInFront);
            DesktopApprovalTests.Click(prompt, "AllowOnce");
            var answered = await reply.WaitAsync(Wait, Token);
            Assert.Equal(AuditDecision.Granted, answered!.Decision);

            // Agents and Activity: the records and the chain's check.
            Chord(main, PhysicalKey.Digit2);
            Press(main, Named<Button>(main, "OpenActivity"));
            var log = Assert.IsType<LogViewModel>(shell.Content);
            AccentElements.AssertOne(main, null, "Activity");
            Assert.Contains(log.Rows, row => row.Actor == _label);
            log.VerifyCommand.Press();
            Assert.True(log.VerdictShown);
            AccentElements.AssertOne(main, null, "Activity, its verdict shown");

            // Settings › Share links: the share links, revoked.
            Chord(main, PhysicalKey.Digit4);
            AccentElements.AssertOne(main, null, "Settings");
            // The toasts the share and the prompt left, the clipboard's countdown among them, lie over the content until closed.
            foreach (var toast in shell.Toasts.Shown.ToList())
            {
                Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, toast.CloseCommand)));
            }

            if (shell.Clipboard.IsCounting)
            {
                Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, shell.Clipboard.ClearNowCommand)));
            }

            main.GetVisualDescendants().OfType<ListBox>().Single(list => list is { Name: "SettingsSections", IsEffectivelyVisible: true }).SelectedItem = SettingsSectionChoice.ShareLinks;
            Dispatcher.UIThread.RunJobs();
            var links = Assert.IsType<SharingViewModel>(shell.Content);
            await Until(() => links.Rows.Count == 1);
            AccentElements.AssertOne(main, null, "share links");
            DrawnFrame.Capture(main);
            Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, links.RevokeRowCommand)));
            Assert.Single(server.Shares);
            Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button is { Name: "ConfirmRevoke", IsEffectivelyVisible: true }));
            await Until(() => server.Shares.Count == 0);

            // Security, then Trash from the vault's places, then the lock, before the window closes.
            Chord(main, PhysicalKey.Digit3);
            Assert.IsType<RecommendationsViewModel>(shell.Content);
            Chord(main, PhysicalKey.Digit1);
            Press(main, Row(main, "Nav", row => row is PlaceRow { Place: ItemPlace.Trash }));
            Assert.IsType<TrashViewModel>(shell.Content);
            AccentElements.AssertOne(main, null, "Trash");
            Chord(main, PhysicalKey.L);
            await Until(() => root.Content is UnlockView);
            main.Close();
        });

    private static CredentialRequest Request(string vault, string session) => new()
    {
        Entry = "Work/github",
        Field = "password",
        Reason = "run the journey's deploy",
        Exposure = ["**"],
        ClientName = _client,
        ClientLabel = _label,
        Vault = vault,
        Session = session,
    };

    /// <summary>What the automation tree names each row of a list, as a screen reader reads them.</summary>
    private static List<string> Names(ListBox list)
    {
        List<string> names = [];
        Walk(ControlAutomationPeer.CreatePeerForElement(list));
        return names;

        void Walk(AutomationPeer peer)
        {
            if (peer.GetAutomationControlType() == AutomationControlType.ListItem)
            {
                names.Add(peer.GetName());
                return;
            }

            foreach (var child in peer.GetChildren())
            {
                Walk(child);
            }
        }
    }

    private static async Task<ApproverClient> ConnectAsync(AppAuthority authority, string vault)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(authority.Status).Endpoint;
        var client = await ApproverClient.TryConnectAsync(endpoint, Wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(vault) { Client = new AttachClient(_client, "1.0", _label) }, Token))!.Attached);
        return client;
    }
}
