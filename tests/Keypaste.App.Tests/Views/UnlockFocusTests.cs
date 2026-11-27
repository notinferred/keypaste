using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The keyboard-first claim, asserted rather than believed.
/// </summary>
/// <remarks>
/// <para>
/// "Launch, type your password, press Enter, never touch the mouse" is what the unlock screen
/// promises, and it is exactly the kind of claim that quietly stops being true. These
/// run headless, in process, so they do not depend on which window happens to be in the foreground —
/// which is precisely why driving the real app with synthetic keystrokes proved nothing.
/// </para>
/// <para>
/// <b>Every test that types needs a selected vault, and that is the product's behaviour rather than
/// a fixture detail.</b> With nothing selected there is nothing to unlock, so the password field is
/// disabled and cannot take focus. The first version of this file missed that and failed for the
/// right reason.
/// </para>
/// </remarks>
public sealed class UnlockFocusTests
{
    [Fact]
    public Task The_password_field_has_focus_when_a_vault_is_waiting() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        var window = Show(model);

        Assert.NotEmpty(model.Recent);
        Assert.True(model.HasSelection);
        Assert.True(Password(window).IsFocused, "the master password field should have focus on open");
    });

    /// <summary>
    /// A recent list must not take the first keystrokes. Binding its <c>SelectedItem</c> gives a
    /// <c>ListBoxItem</c> focus after <c>Loaded</c> has run, which is why focus is posted rather
    /// than set inline.
    /// </summary>
    [Fact]
    public Task The_recent_list_does_not_steal_focus() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        var window = Show(model);

        // By name: the restore panel has a list of its own.
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "RecentList");

        Assert.False(list.IsFocused);
        Assert.True(Password(window).IsFocused);
    });

    /// <summary>
    /// With nothing selected there is nothing to type into, and the field says so by being
    /// disabled rather than by swallowing keystrokes.
    /// </summary>
    [Fact]
    public Task With_no_vault_selected_the_welcome_shows_and_no_password_field_can_take_keys() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        var window = Show(model);

        Assert.False(model.HasSelection);
        Assert.True(model.ShowsWelcome);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<MaskedInput>(), field => field.IsEffectivelyVisible);
    });

    [Fact]
    public Task Typing_reaches_the_buffer_and_backspace_removes() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        var window = Show(model);

        window.KeyTextInput("h");
        window.KeyTextInput("i");
        Assert.Equal(2, model.MaskedLength);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal(1, model.MaskedLength);
    });

    /// <summary>
    /// The mask is derived from a length, never from the password — so there is nothing in the
    /// visual tree that could be read back, by an automation peer or by anything else.
    /// </summary>
    [Fact]
    public Task The_control_shows_dots_and_never_the_characters() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });

        var window = Show(model);

        window.KeyTextInput("s");
        window.KeyTextInput("e");
        window.KeyTextInput("c");

        var password = Password(window);

        Assert.Equal("•••", password.Display);
        Assert.DoesNotContain('s', password.Display);
    });

    [Fact]
    public Task The_create_form_takes_focus_when_it_opens_and_gives_it_back_on_cancel() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        var picker = new FakeVaultFilePicker { NewPath = Path.Combine(fixture.Home, "created.kdbx") };
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, picker, () => { });

        var window = Show(model);
        model.StartCreateAsync().GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(Field(window, "NewPassword").IsFocused);
        window.KeyTextInput("a");
        Assert.Equal(1, model.NewMaskedLength);

        model.CancelCreateAsync().GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(Password(window).IsFocused);
    });

    [Fact]
    public Task Where_quick_unlock_stands_alone_its_button_has_the_keyboard_and_Return_presses_it() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        var keys = new FakeQuickUnlockKeys(MachineQuickUnlock.TouchIdName);
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home, quickUnlock: keys);

        using (var first = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { }))
        {
            foreach (var c in TempVault.Password)
            {
                first.Type(c);
            }

            await first.UnlockAsync();
        }

        session.Lock(VaultLockReason.Manual);
        var landed = 0;
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => landed++, lockedBy: VaultLockReason.Manual);
        var window = Show(model);
        var quickUnlock = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "QuickUnlock");

        Assert.True(model.ShowsQuickUnlockAlone);
        Assert.True(quickUnlock.IsFocused, "the quick unlock should have the keyboard where it stands alone");
        Assert.True(quickUnlock.IsDefault);
        Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Unlock").IsDefault);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.True(SpinWait.SpinUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return landed == 1; }, TimeSpan.FromSeconds(10)), "Return did not press the quick unlock");
        Assert.Equal(2, keys.Asked);
        Assert.True(session.IsUnlocked);
    });

    [Fact]
    public Task Use_master_password_moves_the_keyboard_to_the_password() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        var keys = new FakeQuickUnlockKeys();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home, quickUnlock: keys);

        using (var first = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { }))
        {
            foreach (var c in TempVault.Password)
            {
                first.Type(c);
            }

            await first.UnlockAsync();
        }

        session.Lock(VaultLockReason.Manual);
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { }, lockedBy: VaultLockReason.Manual);
        var window = Show(model);

        model.UsePasswordCommand.Press();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(Password(window).IsFocused);
        Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Unlock").IsDefault);
    });

    private static MaskedInput Field(Window window, string name) =>
        window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == name);

    // By name, not Single(): the two create fields are in the tree too, even with the form hidden.
    private static MaskedInput Password(Window window) =>
        window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == "Password");

    private static Window Show(UnlockViewModel model)
    {
        var window = new Window { Content = new UnlockView { DataContext = model } };
        window.Show();

        // Focus is posted at Background priority, so drain the jobs first, as the real window does.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }
}
