using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Session;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Each toast's tone and time, each wait's moving indicator and label, and the next step of each error and empty list.</summary>
public sealed class SayWhatIsHappeningTests
{
    private readonly ManualClock _clock = new(AppClock.Start);

    [Theory]
    [InlineData(nameof(ToastTone.Success), "circle-check")]
    [InlineData(nameof(ToastTone.Information), "info")]
    [InlineData(nameof(ToastTone.Warning), "alert-triangle")]
    public Task A_toast_draws_its_tone(string tone, string icon) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-toast-tone-");
        shell.Shell.ShowToast("Said.", Enum.Parse<ToastTone>(tone));
        RenderedShell.Drain();
        shell.Frame();

        var toast = shell.Window.GetVisualDescendants().OfType<Border>().Single(border => border.DataContext is Toast);
        Assert.Equal(icon, toast.GetVisualDescendants().OfType<KpIcon>().Single(mark => mark.IsEffectivelyVisible && mark.Icon != "x").Icon);
    });

    [Fact]
    public void A_toast_lasts_four_seconds_and_longer_for_longer_text()
    {
        Assert.Equal(TimeSpan.FromSeconds(4), ToastStack.DurationOf("Saved."));
        Assert.True(ToastStack.DurationOf(new string('x', 200)) > TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_toast_waits_while_held_then_has_its_whole_time_again()
    {
        using var stack = new ToastStack(_clock, action => action());
        stack.Show("Saved.", ToastTone.Success);
        var toast = Assert.Single(stack.Shown);

        toast.Hold();
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(stack.Shown);

        toast.Release();
        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Single(stack.Shown);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(stack.Shown);
    }

    [Fact]
    public void A_later_toast_replaces_the_one_before_and_the_same_one_restarts()
    {
        using var stack = new ToastStack(_clock, action => action());

        stack.Show("Not saved. Try again.", ToastTone.Warning);
        stack.Show("Copied.", ToastTone.Success);
        Assert.Equal(["Copied."], stack.Shown.Select(toast => toast.Message));

        _clock.Advance(TimeSpan.FromSeconds(3));
        stack.Show("Copied.", ToastTone.Success);
        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Single(stack.Shown);
    }

    [Fact]
    public void A_toast_said_while_a_dialog_is_up_waits_for_it_to_close()
    {
        using var stack = new ToastStack(_clock, action => action());

        stack.IsHeld = true;
        stack.Show("Imported 7 items.", ToastTone.Success);
        Assert.Empty(stack.Shown);

        stack.IsHeld = false;
        Assert.Equal(["Imported 7 items."], stack.Shown.Select(toast => toast.Message));
    }

    [Fact]
    public Task Unlocking_moves_and_says_so() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(_clock);
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = new Window { Content = new UnlockView { DataContext = model } };
        window.Show();
        WindowInput.Drain();

        Type(model, "not the password");
        var unlocking = model.UnlockAsync();

        AssertWaiting(window, "Unlocking…");
        await unlocking;
    });

    [Fact]
    public Task Creating_a_vault_moves_and_says_so() => HeadlessSession.On(async () =>
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(_clock);
        var picker = new FakeVaultFilePicker { NewPath = home.FreeVaultPath };
        using var model = new UnlockViewModel(session, home.Path, picker, () => { });
        var window = new Window { Content = new UnlockView { DataContext = model } };
        window.Show();
        await model.StartCreateAsync();
        WindowInput.Drain();

        foreach (var c in "a long new password")
        {
            model.TypeNew(c);
            model.TypeConfirm(c);
        }

        var creating = model.CreateAsync();

        AssertWaiting(window, "Creating vault…");
        await creating;
    });

    [Fact]
    public Task Looking_for_tools_and_running_a_plan_move_and_say_so() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using var authority = new AppAuthority(new AppVaultSession(_clock, home: fixture.Home), null, () => new NobodyToAsk());
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var tools = new FakeTools(Path.Combine(fixture.Home, "computer")).ClaudeCode(connected: false);
        using var looking = new ManualResetEventSlim();
        tools.Runner.Hold = looking;
        using var model = new AgentActivityViewModel(authority, fixture.Home, _clock, action => Dispatcher.UIThread.Post(action), tools.Connector());
        var window = new Window { Content = new AgentActivityView { DataContext = model } };
        window.Show();
        WindowInput.Drain();

        AssertWaiting(window, "Looking for AI tools on this computer…");
        looking.Set();
        await model.Looking;
        WindowInput.Drain();

        var row = model.Rows.Single(row => row.Label == "claude-code");
        model.ConnectCommand.Press(row);
        await model.Opening;
        WindowInput.Drain();

        using var running = new ManualResetEventSlim();
        tools.Runner.Hold = running;
        var run = model.Connection!.ConfirmCommand.ExecuteAsync(null);

        AssertWaiting(window, "Running…");
        running.Set();
        await run;
    });

    [Fact]
    public Task Making_a_link_and_checking_status_move_and_say_so() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Password = "a-value" });
            vault.Save();
        }

        using var session = new AppVaultSession(_clock, home: fixture.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var clipboard = new ClipboardCountdown(new Tests.Clipboard.FakeClipboard(), _clock);
        using var server = new FakeShareServer { Now = _clock.GetUtcNow() };
        using var held = new HeldHandler(server);
        using var model = new SharingViewModel(session, clipboard, new ShareService(new ShareClient(held, ShareEndpoint.Default), _clock, () => AuditLog.TryOpen(KeypasteHome.AuditPath(fixture.Home), _clock, out var log, out _) ? log : null));
        var dialog = new Window { Content = new ShareDialogView { DataContext = model } };
        var list = new Window { Content = new SharingView { DataContext = model } };
        dialog.Show();
        list.Show();
        WindowInput.Drain();
        model.SelectedWhat = model.Candidates[0];

        held.Gate = new TaskCompletionSource();
        var creating = model.CreateCommand.ExecuteAsync(null);
        AssertWaiting(dialog, "Creating link…");
        held.Gate.SetResult();
        await creating;
        WindowInput.Drain();

        held.Gate = new TaskCompletionSource();
        var checking = model.RefreshCommand.ExecuteAsync(null);
        AssertWaiting(list, "Checking status…");
        held.Gate.SetResult();
        await checking;
    });

    [Fact]
    public async Task A_password_that_fails_says_so_in_one_line_naming_the_key_file_it_may_also_need()
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(_clock, home: fixture.Home, hardwareKeys: new SoftwareYubiKey(new byte[20]));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        Type(model, "not the password");
        await model.UnlockAsync();

        Assert.Equal("Wrong password or key file.", model.Message);
        Assert.True(model.UseHardwareKeyCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_locked_vault_says_to_unlock_it()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(_clock, home: fixture.Home);
        using var clipboard = new ClipboardCountdown(new Tests.Clipboard.FakeClipboard(), _clock);
        using var server = new FakeShareServer();
        using var model = new SharingViewModel(session, clipboard, new ShareService(new ShareClient(server, ShareEndpoint.Default), _clock, () => null));

        await model.CreateCommand.ExecuteAsync(null);

        Assert.Equal("The vault is locked. Unlock it, then try again.", model.Error);
    }

    [Fact]
    public Task A_search_with_no_match_offers_Clear_search() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-empty-search-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        shell.Shell.Search = "nothing is called this";
        RenderedShell.Drain();
        shell.Frame();

        var buttons = shell.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
        var clear = buttons.Single(button => button.Name == "ClearSearch");

        clear.Command!.Execute(null);
        Assert.Empty(shell.Shell.Search);
        Assert.Empty(entries.Search);
    });

    [Fact]
    public void An_empty_log_filter_offers_Show_all()
    {
        using var home = new TempAuditHome();
        home.Append("env/dev/STRIPE_KEY");
        var model = new LogViewModel(home.Home) { ShowDenied = true };

        Assert.True(model.OffersShowAll);

        model.ShowWholeLogCommand.Press();

        Assert.True(model.ShowAll);
        Assert.False(model.OffersShowAll);
        Assert.False(model.FilterEmpty);
    }

    [Fact]
    public Task No_tokens_offers_New_token() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(_clock, home: fixture.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var model = new ScopedTokensViewModel(session);
        var window = new Window { Content = new ScopedTokensView { DataContext = model } };
        window.Show();
        WindowInput.Drain();

        var offer = window.GetVisualDescendants().OfType<Button>().Single(button => button is { Name: "NewTokenFromEmpty", IsEffectivelyVisible: true });
        offer.Command!.Execute(null);

        Assert.False(model.CanOpenForm);
    });

    private static void Type(UnlockViewModel model, string password)
    {
        foreach (var c in password)
        {
            model.Type(c);
        }
    }

    private static void AssertWaiting(Window window, string label)
    {
        Assert.Contains(label, window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
        Assert.Contains(window.GetVisualDescendants().OfType<KpIcon>(), mark => mark.IsEffectivelyVisible && mark.Classes.Contains("spinning"));
    }

    private sealed class HeldHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal TaskCompletionSource? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Gate is { } gate)
            {
                await gate.Task.ConfigureAwait(false);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
