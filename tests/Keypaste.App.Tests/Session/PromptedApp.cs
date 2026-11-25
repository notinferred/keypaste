using Avalonia.Controls;
using Keypaste.App.Notifications;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>The app's authority, composed with its own prompt, holding a vault with one credential in it.</summary>
internal sealed class PromptedApp : IAsyncDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly TempVault _fixture;
    private ApproverClient? _client;

    private PromptedApp(TempVault fixture, string vault, Func<IApprovalChannel, IApprovalChannel>? around, FakeNotifications? notifications, Func<bool>? inFront)
    {
        var notices = notifications is null ? null : new RequestNotices(notifications, Clock, inFront ?? (() => false));

        _fixture = fixture;
        Vault = vault;

        // The authority owns the session, as it does at launch.
#pragma warning disable CA2000
        Authority = new AppAuthority(
            new AppVaultSession(Clock, home: fixture.Home),
            null,
            () =>
            {
                var channel = new WindowApprovalChannel(Clock, ApprovalLimits.Default.Window, notices: notices);
                channel.Shown += (_, window) => Windows.Add(window);
                return around is null ? channel : around(channel);
            },
            turns: Turns);
#pragma warning restore CA2000
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal ManualClock Clock { get; } = new(AppClock.Start);

    /// <summary>The queue the app's requests wait their turn in behind the prompt on screen, as every vault open in the app shares one.</summary>
    internal PromptTurns Turns { get; } = new();

    internal AppAuthority Authority { get; }

    internal string Vault { get; }

    /// <summary>The keypaste home the app's session reads <c>clients.toml</c> from.</summary>
    internal string Home => _fixture.Home;

    internal string SessionId { get; private set; } = string.Empty;

    internal List<Window> Windows { get; } = [];

    /// <param name="around">What the gate asks instead of the app's prompt, given that prompt.</param>
    /// <param name="notifications">The system the app's notifications go to, or null to post none.</param>
    /// <param name="inFront">Whether keypaste is in front when a prompt appears; it is not, unless this says so.</param>
    /// <param name="seed">What else the vault holds, written before it is saved.</param>
    internal static async Task<PromptedApp> StartAsync(Func<IApprovalChannel, IApprovalChannel>? around = null, FakeNotifications? notifications = null, Func<bool>? inFront = null, Action<Core.Vault>? seed = null)
    {
        var fixture = new TempVault();
        var vault = Path.Combine(fixture.Home, "agents.kdbx");

        using (var created = ClaimedVault.Create(vault, TempVault.Password))
        {
            created.AddEntry(new VaultEntry { GroupPath = "env/ci", Title = "DEPLOY_KEY", Password = Sentinel });
            Assert.True(created.SetFields(new EntryName("env/ci", "DEPLOY_KEY"), [new FieldWrite("DEPLOY_KEY", Sentinel)]));
            created.AddTag(new EntryName("env/ci", "DEPLOY_KEY"), "env:ci");
            created.AddEntry(new VaultEntry { GroupPath = "env/ci/prod", Title = "DEPLOY_KEY", Password = Sentinel });
            created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = Sentinel });
            created.AddTag(new EntryName("services", "Stripe"), "env:ci:prod");
            created.AddEntry(new VaultEntry { GroupPath = "api", Title = "OpenAI", Password = Sentinel });
            Assert.True(created.SetFields(new EntryName("api", "OpenAI"), [new FieldWrite("OPENAI_API_KEY", ApiKeySentinel)]));
            TempVault.Sees(created, DesktopApprovalTests.Label);
            seed?.Invoke(created);
            created.Save();
        }

        var app = new PromptedApp(fixture, vault, around, notifications, inFront);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, app.Authority.Session.TryUnlock(vault, master.Value));
        }

        app._client = await app.ConnectAsync();
        app.SessionId = app.Authority.Session.SessionId!;
        return app;
    }

    /// <summary>Unlocks the vault once more, into a new session, and attaches this app's bridge to it.</summary>
    internal async Task UnlockAgainAsync()
    {
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, Authority.Session.TryUnlock(Vault, master.Value));
        }

        await _client!.DisposeAsync();
        _client = await ConnectAsync();
        SessionId = Authority.Session.SessionId!;
    }

    /// <summary>Another bridge, attached to the session.</summary>
    internal async Task<ApproverClient> ConnectAsync()
    {
        var endpoint = Assert.IsType<AuthorityStatus.Serving>(Authority.Status).Endpoint;
        var client = await ApproverClient.TryConnectAsync(endpoint, _wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(Vault), Token))!.Attached);
        return client;
    }

    internal Task<CredentialReply?> Ask(CancellationToken? cancellationToken = null, string entry = EntryPath, string exposure = "env/**", string field = "password") =>
        Ask(_client!, cancellationToken, entry, exposure, field);

    internal Task<CredentialReply?> Ask(ApproverClient client, CancellationToken? cancellationToken = null, string entry = EntryPath, string exposure = "env/**", string field = "password") =>
        client.RequestAsync(Credential(entry, exposure, field), cancellationToken ?? Token).AsTask();

    internal CredentialRequest Credential(string entry = EntryPath, string exposure = "env/**", string field = "password") => new()
    {
        Entry = entry,
        Field = field,
        Reason = "deploy the billing service",
        Exposure = [exposure],
        ClientName = "claude-code",
        ClientLabel = DesktopApprovalTests.Label,
        Vault = Vault,
        Session = SessionId,
    };

    /// <summary>Waits for the prompt a request raised to be on screen, in front long enough to take a click, and drawn.</summary>
    /// <param name="count">How many prompts this app has raised once that one is up.</param>
    internal async Task<Window> PromptAsync(int count = 1)
    {
        await Until(() => Windows.Count >= count && (IsInFront(Windows[^1]) || !Windows[^1].IsVisible));
        var window = Windows[^1];
        DrawnFrame.Capture(window);
        return window;
    }

    /// <summary>Lets the arming delay pass, as a person reading the prompt does, and waits for its keys to arm.</summary>
    /// <remarks>The delay's continuation runs on the thread pool, so the arming is posted some time after the clock moves.</remarks>
    internal async Task ArmAsync()
    {
        Clock.Advance(PromptViewModel.ArmingDelay);
        await Until(() => IsArmed(Windows[^1]));
    }

    /// <summary>
    /// Moves <paramref name="clock"/> from the thread pool, as the system clock's timers fire, then runs what that posted to the UI
    /// thread, so every wait to arm that came due has armed or been refused before this returns.
    /// </summary>
    /// <remarks>The UI thread's synchronization context sends a due wait's continuation to the pool, to post its arming later; off it, the continuation runs inside the move.</remarks>
    internal static async Task AdvanceAsync(ManualClock clock, TimeSpan by)
    {
        await Task.Run(() => clock.Advance(by), Token);
        WindowInput.Drain();
    }

    internal static async Task WithdrawnAsync(Window window) => await Until(() => !window.IsVisible);

    /// <summary>A <c>keypaste run --session</c> request for the <c>ci</c> project on this bridge's connection.</summary>
    internal Task<EnvReply?> AskEnv(CancellationToken? cancellationToken = null) =>
        _client!.ReleaseEnvAsync(Env(), cancellationToken ?? Token).AsTask();

    internal EnvRequest Env() =>
        new("ci", ["deploy", "--to", "staging area"], Path.Combine(_fixture.Home, "work")) { Vault = Vault, Session = SessionId };

    /// <summary>An agent's run of the <c>ci</c> set on this bridge's connection.</summary>
    internal Task<RunReply?> AskRun(CancellationToken? cancellationToken = null) =>
        _client!.ReleaseRunAsync(Run(), cancellationToken ?? Token).AsTask();

    internal RunRequest Run() => new()
    {
        Program = OperatingSystem.IsWindows() ? @"C:\tools\deploy.exe" : "/usr/bin/deploy",
        Command = ["deploy", "--to", "staging"],
        Directory = Path.Combine(_fixture.Home, "work"),
        Project = "ci",
        Reason = "deploy the billing service",
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientLabel = DesktopApprovalTests.Label,
        Vault = Vault,
        Session = SessionId,
    };

    /// <summary>The frame a bridge sends for one kind of prompted request: <c>credential</c>, <c>env</c> or <c>run</c>.</summary>
    internal byte[] Frame(string kind) => kind switch
    {
        "credential" => ApproverProtocol.Encode(Credential()),
        "env" => ApproverProtocol.Encode(Env()),
        _ => ApproverProtocol.Encode(Run()),
    };

    /// <summary>Asks for one kind of prompted request on the attached bridge: a <see cref="CredentialReply"/>, <see cref="EnvReply"/> or <see cref="RunReply"/>.</summary>
    internal async Task<object?> AskAsync(string kind) => kind switch
    {
        "credential" => (object?)await Ask(),
        "env" => await AskEnv(),
        _ => await AskRun(),
    };

    /// <summary>Asks for one kind of prompted request on the attached bridge, and says whether anything was released.</summary>
    internal async Task<bool> ReleasedAsync(string kind) => kind switch
    {
        "credential" => (await Ask())?.Value is not null,
        "env" => (await AskEnv())?.Set.Outcome == EnvOutcome.Resolved,
        _ => (await AskRun())?.Set.Outcome == EnvOutcome.Resolved,
    };

    /// <summary>Another bridge, attached without awaiting anything, so the UI thread runs nothing meanwhile.</summary>
    internal Peer Attach()
    {
        var peer = new Peer(Assert.IsType<AuthorityStatus.Serving>(Authority.Status).Endpoint);
        peer.Send(ApproverProtocol.Encode(new AttachRequest(Vault)));
        Assert.True(ApproverProtocol.TryDecode(peer.Receive(), out AttachReply? attached) && attached.Attached, "the peer was not attached");
        return peer;
    }

    internal async Task HangUpAsync()
    {
        await _client!.DisposeAsync();
        _client = null;
    }

    internal static Task UntilAsync(Func<bool> condition) => Until(condition);

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the prompt never reached that state");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        Authority.Dispose();
        WindowInput.Drain();
        _fixture.Dispose();
    }
}
