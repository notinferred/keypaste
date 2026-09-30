using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Closing the main window ends the session even while a request's prompt window is open: the request is refused
/// as locked and the prompt comes down (V-F.21). Without the tray the app quits; with it the app stays, locked, in
/// the menu bar or tray (G.4a).
/// </summary>
/// <remarks>
/// <para>
/// The app is composed by <see cref="App.Launch"/> into Avalonia's own
/// <see cref="ClassicDesktopStyleApplicationLifetime"/>, whose window tracking decides whether a
/// closed window ends the application. The defect was that lifetime's default: with the prompt
/// still open, the main window was not the last one, so nothing asked the app to quit.
/// </para>
/// <para>
/// <b>Three things differ from a launch</b>, because this assembly shares one dispatcher and a
/// completed shutdown ends it. The lifetime is attached to the running session through its internal
/// <c>SubscribeGlobalEvents</c>, which only <c>StartWithClassicDesktopLifetime</c> calls. The test
/// cancels the shutdown after the app has answered it. And the session is unlocked directly rather
/// than through the unlock screen, so no shell is showing and quitting ends the authority at once;
/// with a shell the app clears the clipboard first and then calls <c>Shutdown</c>, which cannot be
/// cancelled.
/// </para>
/// </remarks>
public sealed class ClosingTheMainWindowTests
{
    private const string _sentinel = "SENTINEL-CLOSE-WITH-PROMPT-8c2e47";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static readonly byte[] _keySecret = Convert.FromHexString("3c1d5e7f90a2b4c6d8e0f1325476980badcfe102");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Closing_the_main_window_with_a_prompt_open_quits_and_refuses_the_request_as_locked() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Vault(fixture);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: false) };
            app.Launch(lifetime);

            var requested = false;
            lifetime.ShutdownRequested += (_, e) =>
            {
                requested = true;
                e.Cancel = true;
            };

            var main = lifetime.MainWindow!;
            main.Show();

            Unlock(app, vault);
            await using var client = await ConnectAsync(app.Authority!, vault);
            var reply = Request(client, app, vault);
            var prompt = await PromptAsync(lifetime);

            main.Close();

            Assert.True(requested, "closing the main window did not ask the app to quit");
            await AssertRefusedAsLocked(reply);

            await Until(() => !prompt.IsVisible);
            Assert.DoesNotContain(prompt, lifetime.Windows);
            Assert.DoesNotContain(main, lifetime.Windows);
        });

    [Fact]
    public Task Closing_the_main_window_into_the_tray_ends_the_session_and_keeps_the_app() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Vault(fixture);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: true) };
            app.Launch(lifetime);

            var requested = false;
            lifetime.ShutdownRequested += (_, e) =>
            {
                requested = true;
                e.Cancel = true;
            };

            var main = lifetime.MainWindow!;
            main.Show();
            Assert.True(app.Tray!.IsShown);

            Unlock(app, vault);
            await using var client = await ConnectAsync(app.Authority!, vault);
            var reply = Request(client, app, vault);
            var prompt = await PromptAsync(lifetime);
            Assert.True(Item(app, AppTray.LockHeader).IsEnabled, "Lock is off while the vault is open");

            main.Close();

            Assert.False(requested, "closing into the tray asked the app to quit");
            await AssertRefusedAsLocked(reply);
            await Until(() => !prompt.IsVisible);

            Assert.False(main.IsVisible);
            Assert.Contains(main, lifetime.Windows);
            Assert.False(app.Authority!.Session.IsUnlocked);
            Assert.IsType<AuthorityStatus.Locked>(app.Authority.Status);
            await Until(() => main.FindControl<ContentControl>("Root")!.Content is UnlockView);
            await Until(() => !Item(app, AppTray.LockHeader).IsEnabled);

            Item(app, AppTray.OpenHeader).Command!.Execute(null);
            Assert.True(main.IsVisible);
            Assert.IsType<UnlockView>(main.FindControl<ContentControl>("Root")!.Content);

            Item(app, AppTray.QuitHeader).Command!.Execute(null);
            Assert.True(requested, "Quit on the tray menu did not ask the app to quit");
        });

    [Fact]
    public Task An_unlock_still_under_way_when_the_window_closes_into_the_tray_leaves_the_vault_locked() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var key = new HeldKey(new SoftwareYubiKey(_keySecret));
            var vault = KeyVault(fixture);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: true), HardwareKeys = () => key };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Show();

            var unlocking = StartUnlock(main, vault);
            await Until(() => key.Asked);

            main.Close();
            key.Answer();
            await Until(() => unlocking.IsCompleted);
            await unlocking;

            Assert.False(app.Authority!.Session.IsUnlocked, "an unlock that finished behind the closed window opened the vault");
            Assert.IsType<AuthorityStatus.Locked>(app.Authority.Status);
            Assert.False(main.IsVisible);
            await Until(() => main.FindControl<ContentControl>("Root")!.Content is UnlockView);
        });

    [Fact]
    public Task A_yubikey_waiting_for_its_touch_stops_waiting_when_the_window_closes_into_the_tray() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var key = new SoftwareYubiKey(_keySecret) { NeverTouched = true };
            var vault = KeyVault(fixture);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: true), HardwareKeys = () => key };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Show();

            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(main.FindControl<ContentControl>("Root")!.Content).DataContext);
            var unlocking = StartUnlock(main, vault);
            await Until(() => unlock.IsWaitingForTouch);

            main.Close();

            // The key itself would wait thirty seconds; only the close stops it within the wait.
            await Until(() => unlocking.IsCompleted);
            await unlocking;
            Assert.False(app.Authority!.Session.IsUnlocked);
        });

    [Fact]
    public Task A_start_at_login_opens_no_window_and_holds_no_claim() =>
        HeadlessSession.On(() =>
        {
            using var fixture = new TempVault();
            var vault = Vault(fixture);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: true) };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            Assert.Null(lifetime.MainWindow);
            Assert.False(app.Main!.IsVisible);
            Assert.False(app.Authority!.Session.IsUnlocked);

            var home = KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
            Assert.True(
                VaultClaim.TryAcquire(home, vault, OwnerKind.CommandLine, out var claim, out var refusal),
                $"a start at login holds the vault: {refusal}");
            claim?.Dispose();

            app.OpenWindow();

            Assert.Same(app.Main, lifetime.MainWindow);
            Assert.True(app.Main.IsVisible);
            app.Main.Close();
        });

    [Fact]
    public Task Without_the_tray_a_start_at_login_shows_the_window() =>
        HeadlessSession.On(() =>
        {
            using var fixture = new TempVault();

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(fixture, stays: false) };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            Assert.Same(app.Main, lifetime.MainWindow);
        });

    private static string Vault(TempVault fixture)
    {
        var vault = Path.Combine(fixture.Home, "agents.kdbx");

        using var created = Core.Vault.Create(vault, TempVault.Password);
        created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = _sentinel });
        created.Save();

        return vault;
    }

    private static string KeyVault(TempVault fixture)
    {
        var vault = Path.Combine(fixture.Home, "keyed.kdbx");

        using var key = new HardwareKey(new SoftwareYubiKey(_keySecret), 2);
        using var created = Core.Vault.CreateWith(vault, TempVault.Password, null, key);
        created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = _sentinel });
        created.Save();

        return vault;
    }

    private static Task StartUnlock(Window main, string vault)
    {
        var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(main.FindControl<ContentControl>("Root")!.Content).DataContext);
        Assert.True(unlock.Offer(vault));
        unlock.UseHardwareKeyCommand.Execute(null);
        Assert.Equal(2, unlock.HardwareKeySlot);

        foreach (var c in TempVault.Password)
        {
            unlock.Type(c);
        }

        return unlock.UnlockAsync();
    }

    private static DesktopPreferences Tray(TempVault fixture, bool stays)
    {
        var preferences = new DesktopPreferences(fixture.Home);
        preferences.Update(AppSettings.Default with { StayInTray = stays });
        return preferences;
    }

    private static NativeMenuItem Item(App app, string header) =>
        app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);

    private static void Unlock(App app, string vault)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, app.Authority!.Session.TryUnlock(vault, master.Value));
    }

    private static Task<CredentialReply?> Request(ApproverClient client, App app, string vault) =>
        client.RequestAsync(
            new CredentialRequest
            {
                Entry = "env/ci/DEPLOY_KEY",
                Field = "password",
                Reason = "deploy the billing service",
                TtlSeconds = 60,
                Exposure = ["env/**"],
                ClientName = "claude-code",
                ClientLabel = "f21",
                Vault = vault,
                Session = app.Authority!.Session.SessionId!,
            },
            Token).AsTask();

    private static async Task<Window> PromptAsync(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        await Until(() => lifetime.Windows.Any(window => window is ApprovalWindow));
        return lifetime.Windows.Single(window => window is ApprovalWindow);
    }

    private static async Task AssertRefusedAsLocked(Task<CredentialReply?> reply)
    {
        var answered = await reply.WaitAsync(_wait, Token);

        Assert.NotNull(answered);
        Assert.Equal(AuditDecision.Denied, answered.Decision);
        Assert.Equal(AuditMethod.VaultLocked, answered.Method);
        Assert.Null(answered.Value);
    }

    private static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    private static async Task<ApproverClient> ConnectAsync(AppAuthority authority, string vault)
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(authority.Status).Endpoint;
        var client = await ApproverClient.TryConnectAsync(endpoint, _wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(vault), Token))!.Attached);
        return client;
    }

    /// <summary>A key that answers only when the test says, whatever cancels its wait, standing in for an unlock still deriving its key.</summary>
    private sealed class HeldKey(SoftwareYubiKey inner) : IChallengeResponseDevice
    {
        private volatile bool _asked;
        private volatile bool _answered;

        internal bool Asked => _asked;

        internal void Answer() => _answered = true;

        public IReadOnlyList<HardwareKeyInfo> Find() => inner.Find();

        public byte[] Respond(int slot, byte[] challenge, Action touchNeeded, CancellationToken cancellationToken)
        {
            _asked = true;
            Assert.True(SpinWait.SpinUntil(() => _answered, _wait), "the test never let the key answer");
            return inner.Respond(slot, challenge, touchNeeded, CancellationToken.None);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the app never reached that state");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }
}
