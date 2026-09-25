using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Activity;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Draws every screen of the app to a PNG with a demo vault, for comparing against the design in
/// docs/design. It runs only when KEYPASTE_SCREENS_OUT names a folder, and is skipped otherwise:
/// <code>
/// KEYPASTE_SCREENS_OUT=/tmp/screens dotnet test tests/Keypaste.App.Tests -- --filter-class Keypaste.App.Tests.Rendering.ScreenRenderer
/// </code>
/// (PowerShell: <c>$env:KEYPASTE_SCREENS_OUT="$env:TEMP\screens"</c> first.) It writes one file per
/// sidebar destination, the unlock screen, the approval window and the shell with a toast. Add a
/// screen by adding a <see cref="Save"/> call.
/// </summary>
public sealed class ScreenRenderer
{
    private const string _variable = "KEYPASTE_SCREENS_OUT";
    private const string _master = "correct horse battery staple";
    private const int _width = 1280;
    private const int _height = 800;

    [Fact]
    public Task Every_screen_is_drawn_to_a_file() => HeadlessSession.On(() =>
    {
        var output = Environment.GetEnvironmentVariable(_variable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"{_variable} is not set");
        Directory.CreateDirectory(output!);

        using var demo = new DemoVault();
        DrawUnlock(demo, output!);
        DrawShell(demo, output!);
        DrawEnv(demo, output!);
        DrawApproval(output!);
        DrawComponents(output!);
        DrawSecrets(demo, output!);
        DrawLight(demo, output!);
        DrawLockAndImport(demo, output!);
        DrawPrompts(output!);
        DrawTrashWithRows(demo, output!);
    });

    /// <summary>
    /// Agents with what a working session holds: two clients attached over the app's own endpoint
    /// whose requests a person allowed for an hour, a request still waiting at the prompt, a client
    /// seen only in the log, a policy and two tokens; then the page in a wide window, the token form,
    /// a minted token, a token revoke being confirmed and the Connect card.
    /// </summary>
    [Fact]
    public Task The_agents_screen_is_drawn_with_grants_clients_and_tokens() => HeadlessSession.On(async () =>
    {
        var output = Environment.GetEnvironmentVariable(_variable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"{_variable} is not set");
        Directory.CreateDirectory(output!);

        using var demo = new DemoVault();
        var clock = new ManualClock();
        using var authority = new AppAuthority(new AppVaultSession(clock, TimeSpan.FromHours(8), demo.Home), null, () => new Allowing());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(demo.Path, master.Value));
        }

        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        await using var claude = await AgentAsync(serving, demo.Path, new Core.Ipc.AttachClient("claude", "2.0", "claude-code"));
        await using var cursor = await AgentAsync(serving, demo.Path, new Core.Ipc.AttachClient("cursor", "1.4", "cursor"));
        await AskAsync(claude, serving, demo.Path, "claude-code", "env/acme-api/DATABASE_URL");
        await AskAsync(claude, serving, demo.Path, "claude-code", "env/acme-api/STRIPE_SECRET_KEY");
        clock.Advance(TimeSpan.FromMinutes(18));
        await AskAsync(cursor, serving, demo.Path, "cursor", "env/acme-web/NEXT_PUBLIC_API");
        var waiting = AskAsync(cursor, serving, demo.Path, "cursor", Allowing.Held);
        await WaitUntilAsync(() => authority.Activity.Waiting.Count == 1);

        Assert.True(Core.Clients.ClientPolicies.TrySave(
            Core.Audit.KeypasteHome.ClientsPath(demo.Home),
            Core.Clients.ClientPolicies.Empty.With("cursor", Core.Clients.ClientPolicy.AskEveryTime).With("local-evals", Core.Clients.ClientPolicy.InjectOnly),
            out var saveError), saveError);

        using var shell = new ShellViewModel(authority.Session, demo.Home, authority, clipboard: new FakeClipboard(), clock: clock);
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.AgentActivity);
        var agents = Assert.IsType<AgentActivityViewModel>(shell.Content);
        var tokens = agents.Tokens!;
        Assert.True(tokens.Create("ci-github-actions", "read:acme-api/staging/*", TimeSpan.FromDays(30), false).Ok);
        Assert.True(tokens.Create("local-evals", "read:acme-api/dev/OPENAI_API_KEY", TimeSpan.FromDays(7), false).Ok);
        agents.Refresh();
        Save(window, output!, "20-agents");

        window.Height = 1500;
        Save(window, output!, "21-agents-full");

        window.Width = 1920;
        window.Height = 1100;
        Save(window, output!, "25-agents-wide");
        window.Width = _width;
        window.Height = 1500;

        agents.RevokeCommand.Execute(agents.Grants[0]);
        tokens.OpenFormCommand.Execute(null);
        tokens.Name = "deploy-preview";
        tokens.Scope = "read:acme-web/preview/*";
        tokens.Expiry = "7d";
        Save(window, output!, "22-agents-new-token");

        tokens.CreateCommand.Execute(null);
        Save(window, output!, "23-agents-token-minted");

        tokens.DoneMintedCommand.Execute(null);
        tokens.AskRevokeCommand.Execute(tokens.Rows[0]);
        Save(window, output!, "26-agents-token-revoke");

        tokens.CancelRevokeCommand.Execute(null);
        agents.ToggleConnectCommand.Execute(null);
        Save(window, output!, "24-agents-connect");

        window.Close();
        authority.Session.Lock(VaultLockReason.Manual);
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, deadline.Token);
        }
    }

    private static async Task<Core.Ipc.ApproverClient> AgentAsync(AuthorityStatus.Serving serving, string vault, Core.Ipc.AttachClient identity)
    {
        var client = await Core.Ipc.ApproverClient.TryConnectAsync(serving.Endpoint, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new Core.Ipc.AttachRequest(vault) { Client = identity }, CancellationToken.None))!.Attached);
        return client;
    }

    private static async Task AskAsync(Core.Ipc.ApproverClient client, AuthorityStatus.Serving serving, string vault, string label, string entry)
    {
        var reply = await client.RequestAsync(
            new Core.Ipc.CredentialRequest
            {
                Entry = entry,
                Field = "password",
                Reason = "run the api tests",
                TtlSeconds = 3600,
                Exposure = ["env/**"],
                ClientName = label,
                ClientLabel = label,
                Vault = vault,
                Session = serving.Session,
            },
            CancellationToken.None);
        Assert.NotNull(reply?.Value);
    }

    /// <summary>Allows every request but one, which it leaves waiting until the session ends.</summary>
    private sealed class Allowing : IApprovalChannel
    {
        internal const string Held = "env/acme-web/VERCEL_TOKEN";

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            if (prompt.Entry == Held)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return ApprovalAnswer.Approved;
        }
    }

    /// <summary>The Secrets screen in its states: a login, a variable with profiles, its menu, the new-entry form, history, and the default window.</summary>
    private static void DrawSecrets(DemoVault demo, string output)
    {
        using (var vault = Vault.Open(demo.Path, _master))
        {
            vault.UpdateEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "demo-gh-rotated-9Qk", Url = "https://github.com", Notes = "Recovery codes are in the safe.", GroupPath = "Work" });

            vault.Save();
        }

        // A live authority and this vault's audit lines, so the rows' dots and the Agent access card
        // are read the way the app reads them.
        var clock = new ManualClock();
#pragma warning disable CA2000 // The authority owns and disposes its session.
        using var authority = new AppAuthority(new AppVaultSession(clock, home: demo.Home), null, () => new Session.NobodyToAsk());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(demo.Path, master.Value));
        }

        var vaultKey = authority.Session.Identity!.Key;
        Granted(demo.Home, clock, new EntryName("env/acme-api", "DATABASE_URL"), "claude-code", vaultKey, TimeSpan.FromMinutes(4));
        Granted(demo.Home, clock, new EntryName("env/acme-api", "DATABASE_URL"), "cursor", vaultKey, TimeSpan.FromMinutes(52));
        Granted(demo.Home, clock, new EntryName("env/acme-api", "OPENAI_API_KEY"), "cursor", vaultKey, TimeSpan.FromMinutes(70));
        Granted(demo.Home, clock, new EntryName("env/acme-api", "REDIS_URL"), "claude-code", vaultKey, TimeSpan.FromHours(3));
        Granted(demo.Home, clock, new EntryName("Work", "github"), "claude-code", vaultKey, TimeSpan.FromDays(2));

        using var shell = new ShellViewModel(authority.Session, demo.Home, authority, clipboard: new FakeClipboard(), clock: clock);
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.Entries);
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);

        entries.Selected = entries.Rows.First(row => row.Title == "github");
        Save(window, output, "10-secrets-login");

        entries.SelectedGroup = entries.Groups.First(group => group.Path == "env/acme-api");
        entries.Selected = entries.Rows.First(row => row.Title == "DATABASE_URL" && row.GroupPath == "env/acme-api");
        Save(window, output, "11-secrets-variable");

        WindowInput.Drain();
        window.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "EntryMenu").IsChecked = true;
        Save(window, output, "12-secrets-menu");
        window.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "EntryMenu").IsChecked = false;

        entries.SelectedGroup = entries.Groups[0];
        entries.BeginAddCommand.Execute(null);
        Save(window, output, "13-secrets-new");
        entries.CancelAddCommand.Execute(null);

        entries.Selected = entries.Rows.First(row => row.Title == "github");
        WindowInput.Drain();
        entries.Detail!.History.ToggleCommand.Execute(null);
        WindowInput.Drain();
        entries.Detail.History.Selected = entries.Detail.History.Rows[0];
        Save(window, output, "14-secrets-history");
        entries.Detail.History.ToggleCommand.Execute(null);

        window.Width = 1000;
        window.Height = 680;
        entries.Selected = entries.Rows.First(row => row.Title == "DATABASE_URL" && row.GroupPath == "env/acme-api");
        Save(window, output, "15-secrets-1000");

        window.Width = 960;
        window.Height = 520;
        entries.Selected = entries.Rows.First(row => row.Title == "github");
        Save(window, output, "16-secrets-960");

        window.Width = _width;
        window.Height = _height;
        entries.Detail!.RotateCommand.Execute(null);
        Save(window, output, "17-secrets-rotate");
        entries.Detail.CancelRotateCommand.Execute(null);

        entries.DeleteCommand.Execute(null);
        Save(window, output, "18-secrets-delete");
        entries.CancelDeleteCommand.Execute(null);

        entries.OrganizeCommand.Execute(null);
        Save(window, output, "19-secrets-organize");
        entries.CancelOrganizeCommand.Execute(null);

        entries.Detail.EditCommand.Execute(null);
        Save(window, output, "20-secrets-edit");
        entries.Detail.CancelCommand.Execute(null);

        // A variable's edit form and rotate prompt, which are worded for a value in a profile, and a
        // filter that matches nothing.
        entries.Selected = entries.Rows.First(row => row.Title == "DATABASE_URL" && row.GroupPath == "env/acme-api");
        entries.Detail!.EditCommand.Execute(null);
        Save(window, output, "21-secrets-variable-edit");
        entries.Detail.CancelCommand.Execute(null);

        entries.Detail.RotateCommand.Execute(null);
        Save(window, output, "22-secrets-variable-rotate");
        entries.Detail.CancelRotateCommand.Execute(null);

        entries.Search = "no-such-secret";
        Save(window, output, "23-secrets-no-match");
        entries.Search = string.Empty;

        window.Close();
    }

    /// <summary>The lock screen's states and the KDBX import dialog over the shell.</summary>
    private static void DrawLockAndImport(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        var picker = new FakeVaultFilePicker();

        using (var session = new AppVaultSession(new ManualClock()))
        using (var unlock = new UnlockViewModel(session, demo.Home, picker, () => { }, lockedBy: VaultLockReason.Idle))
        {
            var window = Show(new UnlockView { DataContext = unlock });

            foreach (var c in "hunter22")
            {
                unlock.Type(c);
            }

            Save(window, output, "80-lock-idle");

            Wait(unlock.UnlockAsync());
            Assert.True(unlock.IsError);
            Save(window, output, "85-lock-wrong-password");

            picker.NewPath = Path.Combine(demo.Home, "work.kdbx");
            Assert.True(unlock.StartCreateCommand.CanExecute(null));
            Wait(unlock.StartCreateAsync());
            Save(window, output, "82-lock-create");

            foreach (var c in "one")
            {
                unlock.TypeNew(c);
            }

            foreach (var c in "two")
            {
                unlock.TypeConfirm(c);
            }

            Wait(unlock.CreateAsync());
            Assert.True(unlock.IsError);
            Save(window, output, "86-lock-create-mismatch");
            window.Close();
        }

        var empty = Directory.CreateTempSubdirectory("keypaste-screens-empty-").FullName;

        try
        {
            using var session = new AppVaultSession(new ManualClock());
            using var welcome = new UnlockViewModel(session, empty, picker, () => { });
            var window = Show(new UnlockView { DataContext = welcome });
            Save(window, output, "81-lock-welcome");
            window.Close();
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }

        var source = Path.Combine(demo.Home, "personal.kdbx");

        using (var foreign = Vault.Create(source, "keepassxc-demo"))
        {
            foreign.AddEntry(new VaultEntry { Title = "Checking", Username = "maya.ortiz", Password = "demo-bank-1", GroupPath = "Banking" });
            foreign.AddEntry(new VaultEntry { Title = "Savings", Username = "maya.ortiz", Password = "demo-bank-2", GroupPath = "Banking" });
            foreign.AddEntry(new VaultEntry { Title = "fastmail", Username = "maya@fastmail.com", Password = "demo-mail", GroupPath = "Email" });
            foreign.AddEntry(new VaultEntry { Title = "homelab", Username = "maya", Password = "demo-ssh-1", GroupPath = "SSH" });
            foreign.AddEntry(new VaultEntry { Title = "nas", Username = "admin", Password = "demo-ssh-2", GroupPath = "SSH" });
            foreign.AddEntry(new VaultEntry { Title = "POSTGRES_URL", Password = "demo-pg", GroupPath = "env/acme-jobs" });
            foreign.AddEntry(new VaultEntry { Title = "QUEUE_TOKEN", Password = "demo-queue", GroupPath = "env/acme-jobs" });
            foreign.AddEntry(new VaultEntry { Title = "old router", Password = "demo-old", GroupPath = "Email" });
            foreign.RemoveEntry(new EntryName("Email", "old router"));
            foreign.Save();
        }

        using (var session = new AppVaultSession(new ManualClock()))
        {
            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
            }

            picker.ExistingPath = source;
            using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(), picker: picker);
            var window = Show(new ShellView { DataContext = shell });

            Wait(shell.ImportCommand.ExecuteAsync());
            var import = shell.Import!;

            foreach (var c in "keepass")
            {
                import.TypePassword(c);
            }

            Save(window, output, "93-import-locked");

            Wait(import.UnlockAsync());
            Assert.True(import.HasMessage);
            Save(window, output, "98-import-wrong-password");

            foreach (var c in "keepassxc-demo")
            {
                import.TypePassword(c);
            }

            Wait(import.UnlockAsync());
            Assert.True(import.IsDecrypted, import.Message);
            Save(window, output, "94-import-mapped");

            import.Rows.First(row => row.SourceGroup == "SSH").Destination = ".keypaste/ssh";
            Save(window, output, "95-import-blocked");

            import.Rows.First(row => row.SourceGroup == "SSH").Destination = "personal/SSH";
            import.KeepEditingInPlace = true;
            Save(window, output, "96-import-in-place");

            import.KeepEditingInPlace = false;
            import.ConfirmCommand.Execute(null);
            Assert.Null(shell.Import);
            Save(window, output, "97-import-done");

            var damaged = Path.Combine(demo.Home, "notes.kdbx");
            File.WriteAllText(damaged, "not a vault");
            shell.OpenImport(damaged);
            Assert.True(shell.Import!.IsUnreadable);
            Save(window, output, "99-import-unreadable");
            shell.Import!.CancelCommand.Execute(null);
            window.Close();
        }

        using (var session = new AppVaultSession(new ManualClock()))
        using (var handOff = new UnlockViewModel(session, demo.Home, picker, () => { }))
        {
            Assert.True(handOff.Offer(source, null));
            var window = Show(new UnlockView { DataContext = handOff });
            Save(window, output, "87-lock-hand-off");
            window.Close();
        }

        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [
                new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow),
                new Core.Recent.RecentVault(source, DateTimeOffset.UtcNow.AddMinutes(-5)),
                new Core.Recent.RecentVault(Path.Combine(demo.Home, "moved.kdbx"), DateTimeOffset.UtcNow.AddDays(-2)),
            ]);

        using (var session = new AppVaultSession(new ManualClock()))
        using (var unlock = new UnlockViewModel(session, demo.Home, picker, () => { }, lockedBy: VaultLockReason.Requested))
        {
            var window = Show(new UnlockView { DataContext = unlock });
            Save(window, output, "83-lock-recent");

            Assert.True(unlock.OffersRestore);
            unlock.StartRestoreCommand.Execute(null);
            Save(window, output, "84-lock-restore");

            var restore = unlock.Restore!;

            foreach (var c in "wrong")
            {
                restore.Type(c);
            }

            Wait(restore.CheckAsync());
            Assert.True(restore.IsError);
            Save(window, output, "88-lock-restore-refused");

            foreach (var c in _master)
            {
                restore.Type(c);
            }

            Wait(restore.CheckAsync());
            Assert.True(restore.IsConfirming, restore.Message);
            Save(window, output, "89-lock-restore-confirm");
            window.Close();
        }
    }

    private static MainWindow Show(Control content)
    {
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = content;
        window.Show();
        WindowInput.Drain();
        return window;
    }

    /// <summary>Runs the dispatcher until <paramref name="task"/> is done, for work that hops off the UI thread.</summary>
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            WindowInput.Drain();
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>The env release and run prompts beside the credential one, each at its own size.</summary>
    private static void DrawPrompts(string output)
    {
        var directory = OperatingSystem.IsWindows() ? @"C:\Users\maya\acme\api" : "/home/maya/acme/api";
        var preview = new EnvPreview("acme-api", ["DATABASE_URL", "STRIPE_SECRET_KEY", "STRIPE_RESTRICTED_KEY_FOR_WEBHOOK_SIGNING_IN_PRODUCTION"]);
        var env = EnvReleasePrompt.For(preview, ["npm", "run", "migrate"], directory) with { GrantSeconds = 900 };
        DrawWindow(new EnvApprovalWindow(new EnvApprovalViewModel(env)), output, "91b-env-approval");

        var run = new RunPrompt
        {
            Client = "claude-code",
            Label = "work laptop",
            Reason = "Run the pending database migration for the api service before the deploy.",
            ReasonWasTruncated = false,
            ReasonWasAltered = false,
            Program = OperatingSystem.IsWindows() ? @"C:\Program Files\nodejs\npm.cmd" : "/usr/local/bin/npm",
            Command = "npm run migrate",
            Directory = directory,
            Project = "acme-api",
            Profile = "dev",
            Variables = [new("DATABASE_URL", "env/acme-api/DATABASE_URL", "password"), new("STRIPE_SECRET_KEY", "env/acme-api/STRIPE_SECRET_KEY", "password")],
            GrantSeconds = 900,
        };
        DrawWindow(new RunApprovalWindow(new RunApprovalViewModel(run)), output, "91c-run-approval");

        var protectedPrompt = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/acme-api/prod", "DATABASE_URL"),
            "password",
            "I need the \u202eproduction database URL to check the schema.",
            0,
            null);
        DrawWindow(new ApprovalWindow(new ApprovalViewModel(protectedPrompt)), output, "91d-approval-once-only");
    }

    private static void DrawWindow(PromptWindow window, string output, string name)
    {
        var prompt = (PromptViewModel)window.DataContext!;
        prompt.Tick(TimeSpan.FromSeconds(28));
        prompt.Arm();
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>The trash with two deleted entries in it; drawn last, because it changes the demo vault.</summary>
    private static void DrawTrashWithRows(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        session.Unlocked!.RemoveEntry(new EntryName("Personal", "home wifi"));
        session.Unlocked.RemoveEntry(new EntryName("env/acme-web", "VERCEL_TOKEN"));
        session.Unlocked.Save();

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock());
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        // Settings whole, tall enough that nothing is below the fold.
        window.Height = 1700;
        shell.Current = Destinations.Of(DestinationKind.Settings);
        Save(window, output, "95-settings-whole");
        window.Height = _height;

        shell.Current = Destinations.Of(DestinationKind.Trash);
        var trash = (TrashViewModel)shell.Content!;
        Save(window, output, "93a-trash-no-selection");
        trash.Selected = trash.Rows[0];
        Save(window, output, "93-trash-rows");

        trash.PurgeCommand.Execute(null);
        Save(window, output, "94-trash-confirm");

        window.Close();
    }

    private static void DrawUnlock(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        using var session = new AppVaultSession(new ManualClock());
        using var unlock = new UnlockViewModel(session, demo.Home, new FakeVaultFilePicker(), () => { });
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = unlock };
        window.Show();
        Save(window, output, "00-unlock");
        window.Close();
    }

    private static void DrawShell(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        var clock = new ManualClock();
        using var shares = DemoShares(session.Unlocked!, clock);
        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: clock) { ShareTransport = shares };
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            if (shell.Content is SharingViewModel sharing)
            {
                DrawSharing(window, output, sharing, shares, $"{destination.Shortcut:00}-{Slug(destination.Title)}");
                continue;
            }

            if (shell.Content is EntriesViewModel entries)
            {
                entries.Selected = entries.Rows.FirstOrDefault(row => row.Title == "github");
            }

            if (shell.Content is EnvSetsViewModel env)
            {
                env.OpenCommand.Execute("acme-api");
            }

            Save(window, output, $"{destination.Shortcut:00}-{Slug(destination.Title)}");
        }

        DrawActivityStates(shell, window, demo.Home, output);

        shell.Current = Destinations.Of(DestinationKind.Entries);
        shell.ShowToast("Copied the username");
        Save(window, output, "90-toast");

        window.Close();
    }

    /// <summary>
    /// The Activity screen under each filter, with the chain's verdict open, at the window's narrowest, and after a
    /// careless edit; then a machine with no log yet, and a filter nothing matches.
    /// </summary>
    /// <remarks>The edit comes last, because it leaves the demo log broken.</remarks>
    private static void DrawActivityStates(ShellViewModel shell, Window window, string home, string output)
    {
        shell.Current = Destinations.Of(DestinationKind.Log);
        var log = (LogViewModel)shell.Content!;

        log.Filter = log.Filters.Single(option => option.Filter == LogFilter.Denied);
        Save(window, output, "03-activity-denied");

        log.Filter = log.Filters.Single(option => option.Filter == LogFilter.You);
        Save(window, output, "03-activity-you");

        log.Filter = log.Filters[0];
        log.VerifyCommand.Execute(null);
        Save(window, output, "03-activity-verify");
        log.VerifyCommand.Execute(null);

        window.Width = 960;
        Save(window, output, "03-activity-narrow");
        window.Width = _width;

        var path = KeypasteHome.AuditPath(home);
        var lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var denied = Array.FindIndex(lines, line => line.Contains("the person denied it", StringComparison.Ordinal));
        lines[denied] = lines[denied].Replace("the person denied it", "the person okayed it", StringComparison.Ordinal);
        File.WriteAllText(path, string.Join('\n', lines) + '\n');
        log.Refresh();
        Save(window, output, "03-activity-broken");

        log.VerifyCommand.Execute(null);
        Save(window, output, "03-activity-broken-verify");

        var fresh = Directory.CreateTempSubdirectory("keypaste-screens-empty-").FullName;
        try
        {
            DrawLogAlone(new LogViewModel(fresh, new ManualClock()), output, "03-activity-empty");

            var clock = new ManualClock(new DateTimeOffset(2026, 7, 28, 8, 1, 53, TimeSpan.Zero));
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(fresh), clock, out var agentsOnly, out var error), error);
            using (agentsOnly)
            {
                Assert.True(agentsOnly.TryAppend(
                    new AuditRecord
                    {
                        Tool = "list_entry_names",
                        Client = new AuditClient("claude-code", "1.0.43", null),
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.Exposure,
                        Reason = "every name listed lies inside the exposure",
                    },
                    out var failure), failure);
            }

            var agents = new LogViewModel(fresh, new ManualClock());
            agents.Filter = agents.Filters.Single(option => option.Filter == LogFilter.You);
            DrawLogAlone(agents, output, "03-activity-you-empty");
        }
        finally
        {
            Directory.Delete(fresh, recursive: true);
        }
    }

    /// <summary>The Activity view on its own, the width of the shell's content area, for a state the demo home cannot reach.</summary>
    private static void DrawLogAlone(LogViewModel model, string output, string name)
    {
        var window = new Window { Width = _width - 232, Height = 520, Content = new LogView { DataContext = model } };
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>Env profiles in its own shell, with a file picker so Import and Export draw enabled.</summary>
    private static void DrawEnv(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(), picker: new FakeVaultFilePicker());
        var window = new MainWindow { Width = _width, Height = 1000 };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.EnvSets);
        var project = Assert.IsType<EnvSetsViewModel>(shell.Content).OpenProject!;
        project.SelectedProfile = "staging";
        Save(window, output, "40-env-staging");

        Hold(window, "REDIS_URL", () => Save(window, output, "42-env-held"));

        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Save(window, output, "43-env-light");
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        window.Width = 960;
        Save(window, output, "44-env-narrow");
        Hold(window, "DATABASE_URL", () => Save(window, output, "45-env-narrow-held"));
        window.Width = _width;

        project.BeginAddCommand.Execute(null);
        Save(window, output, "41-env-add");

        window.Close();

        static void Hold(Window window, string key, Action draw)
        {
            var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.DataContext is EnvVariableRow row && row.Key == key);
            cell.BeginReveal();
            draw();
            cell.EndReveal();
        }
    }

    /// <summary>
    /// Four links as the vault would remember them, one per status, and a share server that knows
    /// the two still open: one untouched, one opened once.
    /// </summary>
    private static FakeShareServer DemoShares(Vault vault, ManualClock clock)
    {
        var now = clock.GetUtcNow();
        var server = new FakeShareServer { Now = now };
        var store = new Core.Sharing.ShareStore(vault);

        void Add(string what, string field, string? to, TimeSpan ttl, TimeSpan age, int views, bool passphrase, int? viewsLeft)
        {
            var id = System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            var created = now - age;
            store.Add(new Core.Sharing.ShareInfo(id, what, field, to, created, created + ttl, views, passphrase, "https://keypaste.com"), "demo-revoke-" + id);

            if (viewsLeft is { } left)
            {
                server.Shares[id] = new FakeShareServer.Share("{}", left, created + ttl, FakeShareServer.Sha256("demo-revoke-" + id));
            }
        }

        Add("env/acme-api/STRIPE_SECRET_KEY", "password", "maya@acme.dev", TimeSpan.FromHours(24), TimeSpan.FromHours(2), 1, true, viewsLeft: null);
        Add("Work/aws-console", "login", null, TimeSpan.FromDays(7), TimeSpan.FromDays(1), 3, true, viewsLeft: 2);
        Add("Personal/home wifi", "password", "sam", TimeSpan.FromHours(24), TimeSpan.FromMinutes(20), 1, false, viewsLeft: 1);
        Add("Work/github", "password", "jordan@acme.dev", TimeSpan.FromHours(1), TimeSpan.FromHours(3), 1, false, viewsLeft: null);
        vault.Save();

        return server;
    }

    /// <summary>The Sharing screen as it opens, with statuses not yet asked for; then checked with the form filled; then once the server answers that it takes no shares.</summary>
    private static void DrawSharing(Window window, string output, SharingViewModel sharing, FakeShareServer shares, string name)
    {
        var opened = DateTime.UtcNow;
        while (sharing.IsEmpty && DateTime.UtcNow - opened < TimeSpan.FromSeconds(5))
        {
            WindowInput.Drain();
            Thread.Sleep(1);
        }

        Save(window, output, name + "-unchecked");

        Wait(sharing.RefreshCommand.ExecuteAsync());
        sharing.SelectedWhat = "env/acme-api/STRIPE_SECRET_KEY";
        sharing.Recipient = "sam@acme.dev";
        sharing.RequirePassphrase = true;
        Save(window, output, name);

        shares.Answer = _ => FakeShareServer.Json(System.Net.HttpStatusCode.NotFound, "{\"error\":\"not found\"}");
        sharing.RequirePassphrase = false;
        Wait(sharing.CreateCommand.ExecuteAsync());
        Save(window, output, name + "-unavailable");
        shares.Answer = null;
    }

    /// <summary>Secrets, Agents and the approval window in the light theme.</summary>
    private static void DrawLight(DemoVault demo, string output)
    {
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

        try
        {
            var clock = new ManualClock();
#pragma warning disable CA2000 // The authority owns and disposes its session.
            using var authority = new AppAuthority(new AppVaultSession(clock, home: demo.Home), null, () => new Session.NobodyToAsk());
#pragma warning restore CA2000

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(demo.Path, master.Value));
            }

            using var shell = new ShellViewModel(authority.Session, demo.Home, authority, clipboard: new FakeClipboard(), clock: clock);
            var window = new MainWindow { Width = _width, Height = _height };
            window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
            window.Show();

            shell.Current = Destinations.Of(DestinationKind.Entries);
            var entries = Assert.IsType<EntriesViewModel>(shell.Content);
            entries.Selected = entries.Rows.First(row => row.Title == "github");
            Save(window, output, "60-light-secrets");

            shell.Current = Destinations.Of(DestinationKind.AgentActivity);
            Save(window, output, "61-light-agents");
            window.Close();

            DrawApproval(output, "62-light-approval");
        }
        finally
        {
            Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }
    }

    private static void DrawApproval(string output, string name = "91-approval")
    {
        var prompt = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/acme-api", "DATABASE_URL"),
            "password",
            "Run the database migration for the api service.",
            3600,
            "work laptop");
        var model = new ApprovalViewModel(prompt);
        model.Tick(TimeSpan.FromSeconds(28));
        var window = new ApprovalWindow(model);
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>Appends a granted credential line written <paramref name="ago"/> before now, as a bridge wrote it then.</summary>
    private static void Granted(string home, ManualClock clock, EntryName entry, string client, string vaultKey, TimeSpan ago)
    {
        var then = new ManualClock(clock.GetUtcNow() - ago);
        Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(home), then, out var log, out var error), error);

        using (log)
        {
            Assert.True(log.TryAppend(
                new AuditRecord
                {
                    Tool = "request_credential",
                    Client = new AuditClient(client, null, null),
                    Args = AuditArgs.ForCredentialRequest(EntryActivity.KeyOf(entry), "password", 60, "demo"),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "a person approved this request",
                    Exposure = ["**"],
                    Vault = vaultKey,
                },
                out var failure), failure);
        }
    }

    /// <summary>The shared classes from Theme/*.axaml side by side, for checking them against the design.</summary>
    private static void DrawComponents(string output)
    {
        static Button Button(string text, string classes, bool enabled = true)
        {
            var button = new Button { Content = text, IsEnabled = enabled };
            button.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return button;
        }

        static TextBlock Text(string text, string classes)
        {
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            block.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return block;
        }

        static Border Box(string classes, Control? child = null)
        {
            var border = new Border { Child = child };
            border.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return border;
        }

        static StackPanel Row(params Control[] children)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.AddRange(children);
            return row;
        }

        var segments = Box("segmented", Row(
            new RadioButton { Content = "All", IsChecked = true, Classes = { "segment" } },
            new RadioButton { Content = "Agents", Classes = { "segment" } },
            new RadioButton { Content = "You", Classes = { "segment" } },
            new RadioButton { Content = "Denied", Classes = { "segment" } }));
        ((StackPanel)segments.Child!).Spacing = 0;

        var focused = new TextBox { Text = "DATABASE_URL", Width = 260, Classes = { "mono" } };

        var icons = new WrapPanel { MaxWidth = 1180 };
        foreach (var key in Application.Current!.Resources.MergedDictionaries
            .Select(provider => provider is ResourceInclude include ? include.Loaded : provider)
            .OfType<IResourceDictionary>()
            .SelectMany(dictionary => dictionary.Keys.OfType<string>())
            .Where(key => key.StartsWith("Icon.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            icons.Children.Add(new Border
            {
                Width = 36,
                Height = 32,
                Child = new KpIcon { Icon = key["Icon.".Length..], Size = 16 },
                [ToolTip.TipProperty] = key,
            });
        }

        var page = new StackPanel { Margin = new Thickness(32, 28), Spacing = 18 };
        page.Children.AddRange(
        [
            Row(new BrandLockup { MarkSize = 22, FontSize = 19 }, new BrandMark { Width = 56, Height = 56 }, new BrandMark { Width = 22, Height = 22, Classes = { "mono" } }),
            Row(Text("Agents", "h1"), Text("New share link", "dialog-title"), Text("Active grants", "section")),
            Row(Text("MCP clients and tokens that can ask for secrets.", "subtitle"), Text("Value", "label"), Text("TIME   ACTOR   RESULT", "th"), Text("kp://acme/api/dev/DATABASE_URL", "mono")),
            Row(Button("Connect client", ""), Button("Allow for 1 hour", "primary"), Button("Deny", "ghost"), Button("Revoke", "danger"), Button("New token", "link"), Button("Disabled", "primary", enabled: false), Button("Small", "sm"), Button("Large", "lg primary"), new Button { Classes = { "icon" }, Content = new KpIcon { Icon = "ellipsis", Size = 14 } }),
            Row(new TextBox { PlaceholderText = "Filter 7 secrets", Width = 260 }, focused, new TextBox { PlaceholderText = "Search secrets", Width = 300, Classes = { "search" } }),
            Row(new ComboBox { ItemsSource = new[] { "Ask every time", "Session grants up to 1h", "Inject only" }, SelectedIndex = 1, Width = 240 }, new CheckBox { Content = "Require a passphrase", IsChecked = true }, new CheckBox { Content = "Unchecked" }, new ToggleSwitch { IsChecked = true, Content = "Keep editing in place" }, new ToggleSwitch { IsChecked = false }),
            Row(segments, Box("chip", Text("dev", "")), Box("chip outline", Text("inject-only", "")), Box("chip amber", Text("approval required", "")), Box("keycap", Text("Ctrl K", ""))),
            Row(Box("dot ok"), Text("Granted", "ok medium"), Box("dot danger"), Text("Denied", "danger medium"), Box("dot info"), Text("Token", "info medium"), Box("dot idle"), Text("Idle", "muted"), Box("dot waiting"), Text("0:28", "mono sm amber"), new ProgressBar { Classes = { "thin" }, Value = 70, Width = 150 }),
            Row(
                new Border { Classes = { "card" }, Width = 260, Child = Text("card: hairline, no fill", "") },
                new Border { Classes = { "card", "filled" }, Width = 260, Child = Text("card filled", "") },
                new Border { Classes = { "terminal" }, Width = 300, Padding = new Thickness(12, 10), Child = Text("› keypaste run -p dev -- npm start", "") },
                new Border { Classes = { "field" }, Width = 300, Child = Text("kp://acme/api/dev/STRIPE_SECRET_KEY", "mono secondary") }),
            Row(
                new Border { Classes = { "dialog" }, Width = 360, Child = new StackPanel { Spacing = 8, Children = { Text("claude-code wants 2 secrets", "dialog-title"), Text("via MCP · ~/acme/api · profile dev", "subtitle") } } },
                new Border { Classes = { "toast" }, Child = Row(new KpIcon { Icon = "circle-check", Size = 15, Foreground = (IBrush)Application.Current.FindResource("KpOk")! }, Text("Granted claude-code for 1 hour", "")) }),
            icons,
        ]);

        var window = new Window { Width = _width, Height = 900, Content = new ScrollViewer { Content = page } };
        window.Show();
        focused.Focus();
        Save(window, output, "92-components");
        window.Close();
    }

    private static void Save(TopLevel window, string output, string name)
    {
        WindowInput.Drain();

        // Enough render ticks for every 120-200ms transition to finish, so no frame is mid-fade.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        WindowInput.Drain();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static string Slug(string title) => title.ToLowerInvariant().Replace(' ', '-');

    /// <summary>A vault that looks lived in: logins in groups, and env projects with keys.</summary>
    private sealed class DemoVault : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-screens-").FullName;

        internal DemoVault()
        {
            Path = System.IO.Path.Combine(_directory, "acme.kdbx");

            using var vault = Vault.Create(Path, _master);
            vault.AddEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "demo-gh-7Hq2x", Url = "https://github.com", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "aws-console", Username = "maya.ortiz", Password = "demo-aws-51Nf", Url = "https://console.aws.amazon.com", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "linear", Username = "maya@acme.dev", Password = "demo-lin-a91K", Url = "https://linear.app", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "gmail", Username = "maya.ortiz@gmail.com", Password = "demo-gm-3a11", Url = "https://mail.google.com", GroupPath = "Personal" });
            vault.AddEntry(new VaultEntry { Title = "home wifi", Password = "demo-wifi-f3a9", Notes = "Router in the hall cupboard.", GroupPath = "Personal" });

            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "OPENAI_API_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = "demo-" + key.ToLowerInvariant(), GroupPath = "env/acme-api" });
            }

            foreach (var key in new[] { "NEXT_PUBLIC_API", "VERCEL_TOKEN" })
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = "demo-" + key.ToLowerInvariant(), GroupPath = "env/acme-web" });
            }

            // Env profiles: staging and prod beside the flat dev set, with every state the matrix draws:
            // an expired staging key, a staging value reused from dev, and a key prod lacks.
            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "OPENAI_API_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                vault.AddEntry(new VaultEntry
                {
                    Title = key,
                    Password = key switch
                    {
                        "REDIS_URL" => "demo-redis_url",
                        "DATABASE_URL" => "postgres://api:demo-staging-pass@db.staging.acme.internal:5432/api",
                        _ => "demo-staging-" + key.ToLowerInvariant(),
                    },
                    GroupPath = "env/acme-api/staging",
                });
            }

            vault.SetExpiryUnchecked(new EntryName("env/acme-api/staging", "OPENAI_API_KEY"), new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = "demo-prod-" + key.ToLowerInvariant(), GroupPath = "env/acme-api/prod" });
            }

            vault.Save();
            WriteActivity(_directory);
        }

        internal string Path { get; }

        internal string Home => _directory;

        /// <summary>A morning of audit records, written through the real writer so the chain verifies.</summary>
        private static void WriteActivity(string home)
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 7, 27, 18, 2, 17, TimeSpan.Zero));
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(home), clock, out var log, out var error), error);

            using (log)
            {
                void Write(TimeSpan after, AuditRecord record)
                {
                    clock.Advance(after);
                    Assert.True(log.TryAppend(record, out var failure), failure);
                }

                var claude = new AuditClient("claude-code", "1.0.43", null);
                var cursor = new AuditClient("cursor", "1.2.4", null);

                static AuditRecord Credential(AuditClient client, string entry, string field, AuditDecision decision, AuditMethod method, string reason, int? seconds = null, string asked = "Run the database migration for the api service.") => new()
                {
                    Tool = "request_credential",
                    Client = client,
                    Args = AuditArgs.ForCredentialRequest(entry, field, 3600, asked),
                    Decision = decision,
                    Method = method,
                    Reason = reason,
                    GrantedSeconds = seconds,
                };

                Write(TimeSpan.Zero, Credential(cursor, "Work/linear", "password", AuditDecision.Granted, AuditMethod.Prompt, "approved once", 0, "Sign in to Linear to file the bug."));
                Write(new TimeSpan(13, 37, 55), new AuditRecord
                {
                    Tool = "list_entry_names",
                    Client = claude,
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Exposure,
                    Reason = "every name listed lies inside the exposure",
                });
                Write(TimeSpan.FromSeconds(41), Credential(claude, "env/acme-api/DATABASE_URL", "password", AuditDecision.Granted, AuditMethod.Prompt, "approved for 1 hour", 3600));
                Write(TimeSpan.FromMinutes(17), new AuditRecord
                {
                    Tool = "run",
                    Client = claude,
                    Args = AuditArgs.ForRun(null, "Run the migration."),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.GrantCache,
                    Reason = "served from a grant given 17 minutes ago",
                    Entries = ["env/acme-api/DATABASE_URL", "env/acme-api/STRIPE_SECRET_KEY"],
                    Command = "npm run migrate",
                });
                Write(TimeSpan.FromMinutes(2), Credential(claude, "env/acme-api/DATABASE_URL", "password", AuditDecision.Granted, AuditMethod.GrantCache, "served from a grant given 19 minutes ago", asked: "Print the connection string so I can check it."));
                Write(TimeSpan.FromMinutes(2), Credential(cursor, "Work/aws-console", "password", AuditDecision.Denied, AuditMethod.Prompt, "the person denied it"));
                Write(TimeSpan.FromMinutes(18), new AuditRecord
                {
                    Tool = "run",
                    Client = new AuditClient("keypaste run --token", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Token,
                    Reason = Core.Tokens.TokenAuditReason.Format("t7d2e", "ci-github-actions", "6 variable(s)"),
                    Entries = ["env/acme-api/DATABASE_URL", "env/acme-api/STRIPE_SECRET_KEY", "env/acme-api/OPENAI_API_KEY", "env/acme-api/REDIS_URL", "env/acme-api/JWT_SIGNING_KEY", "env/acme-api/SENTRY_DSN"],
                });
                Write(TimeSpan.FromMinutes(11), new AuditRecord
                {
                    Tool = "share",
                    Client = new AuditClient("keypaste share", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api/STRIPE_SECRET_KEY", Field = "password" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.ShareCreated,
                    Reason = "share 3a11: 1 view, expires 2026-07-29T08:30:53Z, to maya@acme.dev, passphrase",
                });
                Write(TimeSpan.FromMinutes(14), Credential(cursor, "env/acme-web/VERCEL_TOKEN", "password", AuditDecision.Denied, AuditMethod.TimedOut, "nobody answered within 60 seconds"));
                Write(TimeSpan.FromMinutes(20), Credential(claude, "Work/github", "username", AuditDecision.Granted, AuditMethod.Policy, "rule 'github-username' in policy.toml"));
                Write(TimeSpan.FromMinutes(3), new AuditRecord
                {
                    Tool = "run",
                    Client = new AuditClient("windsurf-cascade-background-agent", "0.9.2", null),
                    Args = AuditArgs.ForRun("env/acme-api-background-worker-queue/staging", "Run the nightly re-index."),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Policy,
                    Reason = "rule 'reindex' in policy.toml",
                    Entries = ["env/acme-api/OPENAI_API_KEY_FOR_EMBEDDINGS", "env/acme-api/JWT_SIGNING_KEY", "env/acme-api/SENTRY_DSN"],
                });
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
