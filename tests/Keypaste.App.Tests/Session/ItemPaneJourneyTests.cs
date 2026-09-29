using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The item pane as a password manager's (V-N.5), through the app as launch composes it: a web
/// address opens through the launcher and copies, another scheme opens nothing and says why, the
/// reference and identifier wait under "…", and the Agent access card shows only once agents can
/// see the item.
/// </summary>
public sealed class ItemPaneJourneyTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(15);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <remarks>Its items' names are its own: the process shares one audit log, where another test's share of a <c>Work/github</c> would count as a release of any vault's.</remarks>
    [Fact]
    public Task The_pane_opens_web_addresses_keeps_names_under_its_menu_and_shows_agent_access_when_agents_can_see() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "pane.kdbx");

            using (var created = Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "pane-login", Username = "me", Password = "gh-pane", Url = "https://github.com/login" });
                created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "bookmarklet", Password = "bm-pane", Url = "javascript:alert(document.cookie)" });
                created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "calculator", Password = "calc-pane", Url = "file:///C:/Windows/System32/calc.exe" });
                created.AddEntry(new VaultEntry { GroupPath = "Personal", Title = "pane-bank", Password = "bank-pane", Url = "bank.example" });
                created.Save();
            }

            var launcher = new RecordingLauncher();
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Pickers = _ => new FakeVaultFilePicker(), WebLaunchers = _ => launcher, KeePassXcConfig = null };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Width = 1280;
            main.Height = 900;
            main.Show();
            var root = main.FindControl<ContentControl>("Root")!;

            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            Assert.True(unlock.Offer(vault));
            WindowInput.Drain();
            WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
            WindowInput.Type(main, TempVault.Password);
            Key(main, PhysicalKey.Enter);
            await Until(() => root.Content is ShellView);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);
            var items = Assert.IsType<EntriesViewModel>(shell.Content);
            DrawnFrame.Capture(main);

            // An https address opens through the launcher, and its Copy copies it.
            Select(main, items, "pane-login");
            Assert.False(Named<Border>(main, "AgentAccessCard").IsEffectivelyVisible, "the card showed with no agent able to see the item");
            Press(main, Named<Button>(main, "OpenUrl"));
            Assert.Equal(["https://github.com/login"], launcher.Opened.Select(uri => uri.AbsoluteUri));
            Press(main, Named<Button>(main, "CopyUrl"));
            var copied = SHA256.HashData(Encoding.UTF8.GetBytes("https://github.com/login"));
            var deadline = DateTime.UtcNow + _wait;
            while ((await new AvaloniaClipboard(main).TryReadHashAsync())?.SequenceEqual(copied) != true)
            {
                Assert.True(DateTime.UtcNow < deadline, "Copy never put the web address on the clipboard");
                WindowInput.Drain();
                await Task.Delay(20, Token);
            }

            // The reference and the identifier are not in the tree until "…" is opened.
            Assert.DoesNotContain(Names(main), name => name.StartsWith("kp://", StringComparison.Ordinal) || name.StartsWith("uuid ", StringComparison.Ordinal));
            Assert.DoesNotContain("Copy the reference", Names(main));
            Press(main, Named<ToggleButton>(main, "EntryMenu"));
            var menu = Names(main);
            Assert.Contains(menu, name => name.StartsWith("kp:///Work/pane-login", StringComparison.Ordinal));
            Assert.Contains(menu, name => name.StartsWith("uuid ", StringComparison.Ordinal));
            Assert.Contains("Copy the reference", menu);
            Key(main, PhysicalKey.Escape);

            // Any other scheme is text that opens nothing and says why.
            foreach (var title in (string[])["bookmarklet", "calculator"])
            {
                Select(main, items, title);
                Assert.False(Named<Button>(main, "OpenUrl").IsEffectivelyVisible, $"{title}'s address was offered as a link");
                Assert.True(Named<TextBlock>(main, "UrlNote").IsEffectivelyVisible, $"{title}'s address did not say why it opens nothing");
                Assert.True(Named<Button>(main, "CopyUrl").IsEffectivelyEnabled);
            }

            Assert.Single(launcher.Opened);

            // After an agent receives pane-login's password, its card is there.
            var session = app.Authority!.Session.SessionId!;
            await using (var quiet = await ConnectAsync(app.Authority, vault, exposure: null))
            {
                var reply = quiet.RequestAsync(Request(vault, session), Token).AsTask();
                await Until(() => lifetime.Windows.Any(window => window is ApprovalWindow { IsVisible: true }));
                var prompt = lifetime.Windows.OfType<ApprovalWindow>().Single(window => window.IsVisible);
                await Until(() => prompt.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);
                DesktopApprovalTests.Click(prompt, "AllowOnce");
                Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
            }

            Select(main, items, "pane-login");
            await Until(() => Named<Border>(main, "AgentAccessCard").IsEffectivelyVisible, () => "pane-login's card never showed after its release");

            // A bridge announcing an exposure that covers bank shows bank's card, with nothing released.
            Select(main, items, "pane-bank");
            Assert.False(Named<Border>(main, "AgentAccessCard").IsEffectivelyVisible);
            await using (await ConnectAsync(app.Authority, vault, exposure: ["Personal/*"]))
            {
                await Until(() => Named<Border>(main, "AgentAccessCard").IsEffectivelyVisible, () => "bank's card never showed for an attached exposure");
            }

            main.Close();
        });

    private static CredentialRequest Request(string vault, string session) => new()
    {
        Entry = "Work/pane-login",
        Field = "password",
        Reason = "check the pane's agent card",
        TtlSeconds = 60,
        Exposure = ["**"],
        ClientName = "claude-code",
        ClientLabel = "n5",
        Vault = vault,
        Session = session,
    };

    private static async Task<ApproverClient> ConnectAsync(AppAuthority authority, string vault, IReadOnlyList<string>? exposure)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(authority.Status).Endpoint;
        var client = await ApproverClient.TryConnectAsync(endpoint, _wait, Token);
        Assert.NotNull(client);
        var attach = new AttachRequest(vault) { Client = new AttachClient("claude-code", "1.0", "n5"), Exposure = exposure };
        Assert.True((await client.AttachAsync(attach, Token))!.Attached);
        return client;
    }

    /// <summary>Chooses the item's row in the table, as a person does.</summary>
    private static void Select(Window window, EntriesViewModel items, string title)
    {
        var row = Named<ListBox>(window, "Rows").GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is EntryRow { Title: var shown } && shown == title);
        Press(window, row);
        Assert.Equal(title, items.Selected?.Title);
    }

    private static List<string> Names(Window window)
    {
        WindowInput.Drain();
        return [.. Visible(ControlAutomationPeer.CreatePeerForElement(window)).Select(peer => peer.GetName() ?? string.Empty)];
    }

    private static IEnumerable<AutomationPeer> Visible(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            if (child is ControlAutomationPeer { Owner.IsEffectivelyVisible: true })
            {
                yield return child;

                foreach (var descendant in Visible(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Press(Window window, Control control)
    {
        WindowInput.Reveal(window, control);
        WindowInput.Press(window, control);
        WindowInput.Release(window, control);
        WindowInput.Drain();
    }

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        WindowInput.Drain();
    }

    private static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    private static async Task Until(Func<bool> condition, Func<string>? state = null)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"the app never reached that state{(state is null ? string.Empty : ": " + state())}");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }

    private sealed class RecordingLauncher : IWebLauncher
    {
        internal List<Uri> Opened { get; } = [];

        public Task<bool> OpenAsync(Uri address)
        {
            Opened.Add(address);
            return Task.FromResult(true);
        }
    }
}
