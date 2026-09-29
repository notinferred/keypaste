using Avalonia.Automation;
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
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tokens;
using Xunit;
using static Keypaste.App.Tests.Session.JourneyDriver;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// V-N.1b: through the app as launch composes it (D-0342), each advanced feature is reached from its
/// one home: scoped tokens, the log's hash check and diagnostics from Settings' Advanced card, and a
/// client's policy from its card's ⋯ menu on Agents; none of them is left on Agents or on Settings' page.
/// </summary>
/// <remarks>
/// Driven as <see cref="FourPlacesJourneyTests"/> is, pressing controls where the window draws them.
/// The home is the process's own, so the journey writes and reads only under its own client label.
/// </remarks>
public sealed class OneHomeJourneyTests
{
    private const string _client = "one-home-journey";
    private const string _label = "one-home";
    private const string _token = "one-home-ci";

    /// <summary>What only the moved controls and facts are called; none may be named where they no longer live.</summary>
    private static readonly string[] _moved =
        ["New token", "Token name", "Token scope", "Create token", "Copy token", "Verify chain", "Copy latest hash", "KEYPASTE_HOME", "keypaste home"];

    [Fact]
    public Task Every_advanced_feature_is_reached_from_its_one_home() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "one-home.kdbx");

            using (var created = Vault.Create(vault, TempVault.Password))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "STRIPE_KEY", Password = "sk-one-home" });
                created.Save();
            }

            var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);
            Record(vault);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App();
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            var main = lifetime.MainWindow!;
            main.Width = 1280;
            main.Height = 800;
            main.Show();
            var root = main.FindControl<ContentControl>("Root")!;

            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            Assert.True(unlock.Offer(vault, null));
            WindowInput.Drain();
            WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
            WindowInput.Type(main, TempVault.Password);
            Key(main, PhysicalKey.Enter, RawInputModifiers.None);
            await Until(() => root.Content is ShellView);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);

            // Settings' own page opens each advanced screen and carries none of their controls or facts.
            Chord(main, PhysicalKey.Digit4);
            AssertNamesNone(main, "Settings");
            Assert.Contains("Open the scoped tokens", AutomationNames(main));
            Assert.Contains("Open diagnostics", AutomationNames(main));
            AmberElements.AssertOne(main, null, "Settings");

            // A scoped token minted, copied and revoked, from Settings › Advanced.
            Press(main, Named<Button>(main, "OpenScopedTokens"));
            var tokens = Assert.IsType<ScopedTokensViewModel>(shell.Content);
            AmberElements.AssertOne(main, Named<Button>(main, "NewToken"), "Scoped tokens");
            Press(main, Named<Button>(main, "NewToken"));
            Fill(main, "Token name", _token);
            Fill(main, "Token scope", "read:billing/dev/*");
            AmberElements.AssertOne(main, Named<Button>(main, "CreateToken"), "a new token's form");
            Press(main, Named<Button>(main, "CreateToken"));
            Assert.True(tokens.HasMinted, tokens.FormError);
            AmberElements.AssertOne(main, Named<Button>(main, "CopyToken"), "a minted token");
            Press(main, Named<Button>(main, "CopyToken"));
            await Until(() => shell.Clipboard.IsCounting, () => shell.Clipboard.Failure ?? "not counting");
            Assert.Equal(_token, shell.Clipboard.Label);
            Assert.Contains(new TokenStore(app.Authority!.Session.Unlocked!).List(), token => token.Name == _token);
            Press(main, Named<Button>(main, "DoneToken"));
            Press(main, VisibleButton(main, "Revoke"));
            Press(main, VisibleButton(main, "Revoke"));
            Assert.DoesNotContain(new TokenStore(app.Authority.Session.Unlocked!).List(), token => token.Name == _token);
            AmberElements.AssertOne(main, Named<Button>(main, "NewToken"), "Scoped tokens, the token revoked");

            // The whole log's hash check, from Settings › Advanced.
            Press(main, Named<Button>(main, "Back"));
            Press(main, Named<Button>(main, "OpenActivityLog"));
            var log = Assert.IsType<LogViewModel>(shell.Content);
            Press(main, Named<Button>(main, "VerifyChain"));
            Assert.True(log.VerdictShown);
            Press(main, Named<Button>(main, "CopyHash"));
            await Until(() => shell.Clipboard.Label == "Latest hash");
            AmberElements.AssertOne(main, null, "the activity log, its verdict shown");

            // The facts a support question needs, from Settings › Advanced.
            Press(main, Named<Button>(main, "Back"));
            Press(main, Named<Button>(main, "OpenDiagnostics"));
            Assert.IsType<DiagnosticsViewModel>(shell.Content);
            var facts = AutomationNames(main);
            Assert.Contains(vault, facts);
            Assert.Contains(KeypasteHome.Resolve(home), facts);
            Assert.Contains(DiagnosticsViewModel.Version, facts);
            AmberElements.AssertOne(main, null, "Diagnostics");

            // A client's policy, from its card's ⋯ menu on Agents, which carries no dropdown for it.
            await using var bridge = await ConnectAsync(app.Authority, vault);
            Chord(main, PhysicalKey.Digit2);
            await Until(() => Card(main) is not null, () => "no card for the bridge");
            AssertNamesNone(main, "Agents");
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<ComboBox>(), box => box.IsEffectivelyVisible);
            AmberElements.AssertOne(main, Named<Button>(main, "ConnectClient"), "Agents");
            Press(main, Card(main)!);
            Assert.True(Named<Border>(main, "ClientMenuPanel").IsEffectivelyVisible);
            AmberElements.AssertOne(main, Named<Button>(main, "ConnectClient"), "Agents, a card's menu open");
            Press(main, Choice(main, ClientPolicy.AskEveryTime));
            await Until(() => Policy(home) == ClientPolicy.AskEveryTime, () => $"clients.toml holds {Policy(home)}");
            await Until(() => !main.GetVisualDescendants().OfType<Border>().Any(panel => panel.Name == "ClientMenuPanel" && panel.IsEffectivelyVisible));
            AmberElements.AssertOne(main, Named<Button>(main, "ConnectClient"), "Agents, the policy changed");

            // History shows the records and leaves the check to the activity log.
            Press(main, Named<Button>(main, "AgentHistory"));
            Assert.True(Assert.IsType<LogViewModel>(shell.Content).IsAgentHistory);
            AssertNamesNone(main, "History");
            AmberElements.AssertOne(main, null, "History");

            Chord(main, PhysicalKey.L);
            await Until(() => root.Content is UnlockView);
            main.Close();
        });

    /// <summary>Fails when the automation tree names a control or fact that has moved to Settings › Advanced.</summary>
    private static void AssertNamesNone(Window window, string where)
    {
        var names = AutomationNames(window);
        var left = _moved.Where(names.Contains).ToList();
        Assert.True(left.Count == 0, $"{where} still carries {string.Join(", ", left)}");
    }

    private static void Fill(Window window, string field, string text)
    {
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == field && box.IsEffectivelyVisible);
        WindowInput.Click(window, box);
        WindowInput.Type(window, text);
    }

    private static Button VisibleButton(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Content as string == content);

    /// <summary>The ⋯ toggle of this journey's client card, once the screen lists it.</summary>
    private static ToggleButton? Card(Window window) =>
        window.GetVisualDescendants().OfType<ToggleButton>()
            .SingleOrDefault(toggle => toggle is { Name: "ClientMenu", DataContext: ClientCardRow { Label: _label }, IsEffectivelyVisible: true });

    private static Button Choice(Window window, ClientPolicy policy) =>
        window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.DataContext is PolicyChoice { Row.Label: _label } choice && choice.Policy == policy);

    private static ClientPolicy? Policy(string? home) =>
        ClientPolicies.TryLoad(KeypasteHome.ClientsPath(home), out var policies, out _) ? policies.For(_label) : null;

    /// <summary>A record of this journey's own, so the log has a table to check.</summary>
    private static void Record(string vault)
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
                        Decision = AuditDecision.Denied,
                        Method = AuditMethod.Prompt,
                        Reason = "the journey's own record",
                        Vault = vault,
                    },
                    out var failure),
                failure);
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
