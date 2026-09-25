using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Closing the main window quits the app even while a request's prompt window is open: the app is
/// asked to quit, the request is refused as locked and the prompt comes down (V-F.21).
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

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Closing_the_main_window_with_a_prompt_open_quits_and_refuses_the_request_as_locked() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "agents.kdbx");

            using (var created = Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = _sentinel });
                created.Save();
            }

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App();
            app.Launch(lifetime);

            var requested = false;
            lifetime.ShutdownRequested += (_, e) =>
            {
                requested = true;
                e.Cancel = true;
            };

            var main = lifetime.MainWindow!;
            main.Show();

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, app.Authority!.Session.TryUnlock(vault, master.Value));
            }

            var session = app.Authority.Session.SessionId!;
            await using var client = await ConnectAsync(app.Authority, vault);
            var reply = client.RequestAsync(
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
                    Session = session,
                },
                Token).AsTask();

            await Until(() => lifetime.Windows.Any(window => window is ApprovalWindow));
            var prompt = lifetime.Windows.Single(window => window is ApprovalWindow);

            main.Close();

            Assert.True(requested, "closing the main window did not ask the app to quit");

            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(AuditDecision.Denied, answered.Decision);
            Assert.Equal(AuditMethod.VaultLocked, answered.Method);
            Assert.Null(answered.Value);

            await Until(() => !prompt.IsVisible);
            Assert.DoesNotContain(prompt, lifetime.Windows);
            Assert.DoesNotContain(main, lifetime.Windows);
        });

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
