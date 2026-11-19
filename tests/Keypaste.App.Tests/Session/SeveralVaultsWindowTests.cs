using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The window with two vaults open, as <see cref="App.Launch"/> composes it: a vault locked alone leaves the other open in
/// the window, with the locked one's unlock form in view, a page's list reaches the right view, and Lock locks both.
/// </summary>
/// <remarks>The lifetime is attached as <see cref="ClosingTheMainWindowTests"/> attaches it, and the app is disposed rather than quit.</remarks>
public sealed class SeveralVaultsWindowTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Locking_the_vault_the_window_was_built_on_keeps_the_other_open_with_its_unlock_form_in_view() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var acmePath = Path.Combine(fixture.Home, "acme.kdbx");

            using (var created = Vault.Create(acmePath, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "acme-gh-9Qe2" });
                created.Save();
            }

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = new DesktopPreferences(fixture.Home) };
            app.Launch(lifetime);
            var main = lifetime.MainWindow!;
            main.Show();

            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(Root(main)).DataContext);
            Assert.True(unlock.Offer(fixture.Path_));

            foreach (var c in TempVault.Password)
            {
                unlock.Type(c);
            }

            await unlock.UnlockAsync();
            var first = await Until(() => Shell(main));
            var vaults = app.Vaults!;
            var acme = vaults.Add(acmePath);

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, acme.Session.TryUnlock(acmePath, master.Value));
            }

            vaults.LockAlone(vaults.InView);

            var rebuilt = await Until(() => Shell(main) is { } shell && shell != first ? shell : null);
            Assert.Same(acme.Session, rebuilt.Anchor);
            Assert.True(acme.Session.IsUnlocked);
            Assert.Equal("test", Assert.IsType<LockedVaultViewModel>(rebuilt.Content).Unlock.Heading);

            vaults.Pairings.Show!(BrowserShow.NewLogin, "acme", "https://github.com");
            await Until(() => rebuilt.Content is EntriesViewModel { NewItem: not null } items ? items : null);
            Assert.Equal("https://github.com", Assert.IsType<EntriesViewModel>(rebuilt.Content).NewItem!.Url);

            await rebuilt.LockCommand.ExecuteAsync(null);

            await Until(() => Root(main) as UnlockView);
            Assert.Empty(vaults.Unlocked);
        });

    private static object? Root(Window main) => main.FindControl<ContentControl>("Root")!.Content;

    private static ShellViewModel? Shell(Window main) => (Root(main) as ShellView)?.DataContext as ShellViewModel;

    private static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    private static async Task<T> Until<T>(Func<T?> found)
        where T : class
    {
        var deadline = DateTime.UtcNow + _wait;

        while (true)
        {
            if (found() is { } value)
            {
                return value;
            }

            Assert.True(DateTime.UtcNow < deadline, "the app never reached that state");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }
}
