using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.GlobalShortcuts;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.GlobalShortcuts;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;
using static Keypaste.App.Tests.Session.ClosingTheMainWindowTests;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The search shortcut, pressed in another app, and Search… in the menu bar or tray bring keypaste forward on Items with
/// its search empty and holding the keyboard; with an agent's request waiting, its prompt comes forward instead, on Deny
/// and disarmed for a second, as Show the prompt brings it (T-11).
/// </summary>
/// <remarks>The app is composed by <see cref="App.Launch"/> as <see cref="ClosingTheMainWindowTests"/> composes it, with the system's chord a fake a test presses.</remarks>
public sealed class SearchFromAnyAppTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>With the keypaste window chosen over quick search in Settings, which <see cref="QuickSearchTests"/> covers.</summary>
    [Fact]
    public Task The_shortcut_and_Search_bring_Items_forward_with_an_empty_search_holding_the_keyboard() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Vault(fixture);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            var system = new FakeGlobalShortcut();
            var preferences = Tray(fixture, stays: true);
            preferences.Update(preferences.Current with { SearchOpens = SearchOpens.Window });
            using var app = new App { Preferences = preferences, GlobalShortcutFor = () => system };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            Assert.Equal(GlobalChord.Default(OperatingSystem.IsMacOS()), system.Registered);

            var main = lifetime.MainWindow!;
            main.Show();
            var root = main.FindControl<ContentControl>("Root")!;
            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            Assert.True(unlock.Offer(vault));

            foreach (var c in TempVault.Password)
            {
                unlock.Type(c);
            }

            await unlock.UnlockAsync();
            await Until(() => root.Content is ShellView);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);

            foreach (var press in new Action[] { system.Press, () => Item(app, AppTray.SearchHeader).Command!.Press() })
            {
                shell.Search = "deploy";
                shell.Current = Destinations.Of(DestinationKind.Settings);
                main.Close();
                Assert.False(main.IsVisible);

                press();

                await Until(() => main.FocusManager?.GetFocusedElement() is TextBox { Name: "ListSearch" });
                Assert.True(main.IsVisible);
                Assert.IsType<EntriesViewModel>(shell.Content);
                Assert.Equal(string.Empty, shell.Search);
            }

            main.Close();
        });

    [Fact]
    public Task With_a_request_waiting_the_shortcut_brings_its_prompt_forward_on_Deny_and_disarmed() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Vault(fixture);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            var system = new FakeGlobalShortcut();
            using var app = new App { Preferences = Tray(fixture, stays: true), GlobalShortcutFor = () => system };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Show();
            Unlock(app, vault);
            main.Close();
            await using var client = await ConnectAsync(app.Authority!, vault);
            var reply = Request(client, app, vault);
            var prompt = await PromptAsync(lifetime);
            await Until(() => IsArmed(prompt));
            prompt.FindControl<Button>("AllowOnce")!.Focus();

            system.Press();
            WindowInput.Drain();

            Assert.True(prompt.FindControl<Button>("Deny")!.IsFocused, "the prompt came forward off Deny");
            Assert.False(IsArmed(prompt), "the prompt came forward armed");
            Assert.False(main.IsVisible, "the shortcut showed the window over a waiting request");
            Assert.False(reply.IsCompleted);

            await Until(() => IsArmed(prompt));
            Click(prompt, "Deny");

            var answered = await reply.WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(AuditDecision.Denied, answered!.Decision);
        });

    private static NativeMenuItem Item(App app, string header) =>
        app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);
}
