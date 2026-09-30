using System.Text;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
/// V-N.1a: from the four places, through the app as launch composes it (D-0342), every act the old
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

            using (var created = Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Username = "me", Password = "gh-journey" });
                created.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "STRIPE_KEY", Password = "sk-journey" });
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

            // The sidebar lists the four places, the vault's group tree under Items and the project
            // under its heading (D-0376), and there is one search.
            Assert.Equal(["Items", "Work", "env", "Projects", "billing", "Agents"], Names(Named<ListBox>(main, "Nav")));
            Assert.Equal(["Trash", "Settings"], Names(Named<ListBox>(main, "FooterNav")));
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<Control>(), control => control.Name is "Search" or "McpCard");
            Assert.Equal(["ShellSearch"], main.GetVisualDescendants().OfType<TextBox>().Where(box => box.Classes.Contains("search")).Select(box => box.Name));

            // An item found by the one search, then the scope it searches.
            Chord(main, PhysicalKey.Digit1);
            Chord(main, PhysicalKey.K);
            WindowInput.Type(main, "git");
            var items = Assert.IsType<EntriesViewModel>(shell.Content);
            Press(main, Row(main, "Rows", row => row is EntryRow { Title: "github" }));
            Assert.Equal("github", items.Selected?.Title);
            Press(main, Row(main, "Nav", row => row is GroupRow { Path: "Work" }));
            Assert.Equal("Work", Named<Border>(main, "SearchScope").GetVisualDescendants().OfType<TextBlock>().First().Text);
            Assert.True(Named<Border>(main, "SearchScope").IsEffectivelyVisible);
            AmberElements.AssertOne(main, Named<ToggleButton>(main, "AddEntry"), "Items, an item open and the search scoped");

            // A project's variables, from its row beneath Items; Back returns to Items.
            Press(main, Row(main, "Nav", row => row is ProjectRow { Name: "billing" }));
            var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
            Assert.Equal(["STRIPE_KEY"], env.OpenProject!.Variables.Select(row => row.Key));
            AmberElements.AssertOne(main, null, "a project's variables");
            var back = Named<Button>(main, "Back");
            Assert.Equal("Back to Items", AutomationProperties.GetName(back));
            Press(main, back);
            Assert.IsType<EntriesViewModel>(shell.Content);

            // A KDBX import, from Items' "+".
            Press(main, Named<ToggleButton>(main, "AddEntry"));
            Press(main, Named<Button>(main, "ImportKdbx"));
            await Until(() => shell.HasImport);
            WindowInput.Click(main, Named<MaskedInput>(main, "ImportPassword"));
            WindowInput.Type(main, _sourcePassword);
            Press(main, main.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, shell.Import!.UnlockCommand)));
            await Until(() => shell.Import is { CanConfirm: true });
            AmberElements.AssertOne(main, Named<Button>(main, "ImportConfirm"), "the import dialog");
            Press(main, Named<Button>(main, "ImportConfirm"));
            await Until(() => !shell.HasImport);
            Assert.Contains(app.Authority!.Session.Unlocked!.ReadEntries(), entry => entry.Title == "Checking");

            // Share…, from the item's ⋯ menu.
            Chord(main, PhysicalKey.Digit1);
            items = Assert.IsType<EntriesViewModel>(shell.Content);
            Press(main, Row(main, "Rows", row => row is EntryRow { Title: "github" }));
            Press(main, Named<ToggleButton>(main, "EntryMenu"));
            Press(main, Named<Button>(main, "ShareEntry"));
            Assert.True(shell.HasShare);
            AmberElements.AssertOne(main, Named<Button>(main, "CreateLink"), "the share dialog");
            Press(main, Named<Button>(main, "CreateLink"));
            await Until(() => !shell.HasShare);
            Assert.Single(server.Shares);

            // An agent's request, approved in the app's own prompt, recorded as the bridge records it.
            var session = app.Authority.Session.SessionId!;
            await using var client = await ConnectAsync(app.Authority, vault);
            var reply = client.RequestAsync(Request(vault, session), Token).AsTask();
            await Until(() => lifetime.Windows.Any(window => window is ApprovalWindow { IsVisible: true }));
            var prompt = lifetime.Windows.OfType<ApprovalWindow>().Single(window => window.IsVisible);
            await Until(() => prompt.FindControl<Button>("Approve")!.IsEffectivelyEnabled);
            DesktopApprovalTests.Click(prompt, "Approve");
            var answered = await reply.WaitAsync(Wait, Token);
            Assert.Equal(AuditDecision.Granted, answered!.Decision);

            Record(vault, session);

            // Agents: the waiting count and dot, revoking the grant, and History.
            Chord(main, PhysicalKey.Digit2);
            await Until(() => shell.MainNav[1].Count == "1", () => $"grants {app.Authority.Activity.Grants.Count}, row '{shell.MainNav[1].Count}'");
            Assert.True(shell.MainNav[1].DotLive);
            AmberElements.AssertOne(main, Named<Button>(main, "ConnectClient"), "Agents, a grant in force");
            Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Content as string == "Revoke"));
            Assert.Empty(app.Authority.Activity.Grants);
            Press(main, Named<Button>(main, "AgentHistory"));
            var history = Assert.IsType<LogViewModel>(shell.Content);
            Assert.True(history.IsAgentHistory);
            AmberElements.AssertOne(main, null, "History");
            Assert.Contains(history.Rows, row => row.Actor == _label);

            // Settings › Advanced: the whole log and its check, then the share links, revoked.
            Chord(main, PhysicalKey.Digit4);
            AmberElements.AssertOne(main, null, "Settings");
            Press(main, Named<Button>(main, "OpenActivityLog"));
            var log = Assert.IsType<LogViewModel>(shell.Content);
            Press(main, Named<Button>(main, "VerifyChain"));
            Assert.True(log.VerdictShown);
            AmberElements.AssertOne(main, null, "the activity log, its verdict shown");
            Press(main, Named<Button>(main, "Back"));
            Press(main, Named<Button>(main, "OpenShareLinks"));
            var links = Assert.IsType<SharingViewModel>(shell.Content);
            await Until(() => links.Rows.Count == 1);
            AmberElements.AssertOne(main, null, "share links");
            DrawnFrame.Capture(main);
            Press(main, main.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Classes.Contains("row-action")));
            await Until(() => server.Shares.Count == 0);

            // Trash, then the lock, before the window closes.
            Chord(main, PhysicalKey.Digit3);
            Assert.IsType<TrashViewModel>(shell.Content);
            AmberElements.AssertOne(main, null, "Trash");
            Chord(main, PhysicalKey.L);
            await Until(() => root.Content is UnlockView);
            main.Close();
        });

    private static CredentialRequest Request(string vault, string session) => new()
    {
        Entry = "Work/github",
        Field = "password",
        Reason = "run the journey's deploy",
        TtlSeconds = 3600,
        Exposure = ["**"],
        ClientName = _client,
        ClientLabel = _label,
        Vault = vault,
        Session = session,
    };

    /// <summary>The bridge's own audit line for the answered request, which History reads.</summary>
    private static void Record(string vault, string session)
    {
        var path = KeypasteHome.AuditPath(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
        Assert.True(AuditLog.TryOpen(path, TimeProvider.System, out var log, out var error), error);

        using (log)
        {
            Assert.True(
                log!.TryAppend(
                    new AuditRecord
                    {
                        Tool = "request_credential",
                        Client = new AuditClient(_client, "1.0", _label),
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.Prompt,
                        Reason = "the person approved it for an hour",
                        Vault = vault,
                        Session = session,
                    },
                    out var failure),
                failure);
        }
    }

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
