using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The keyboard-first claim, asserted rather than believed.
/// </summary>
/// <remarks>
/// <para>
/// "Launch, type your password, press Enter, never touch the mouse" is 4.1's acceptance criterion
/// for the unlock screen, and it is exactly the kind of claim that quietly stops being true. These
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

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, fixture.Home, () => { });

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

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, fixture.Home, () => { });

        var window = Show(model);

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();

        Assert.False(list.IsFocused);
        Assert.True(Password(window).IsFocused);
    });

    /// <summary>
    /// With nothing selected there is nothing to type into, and the field says so by being
    /// disabled rather than by swallowing keystrokes.
    /// </summary>
    [Fact]
    public Task With_no_vault_selected_the_password_field_is_disabled() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, fixture.Home, () => { });

        var window = Show(model);

        Assert.False(model.HasSelection);
        Assert.False(Password(window).IsEnabled);
    });

    [Fact]
    public Task Typing_reaches_the_buffer_and_backspace_removes() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, fixture.Home, () => { });

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

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, fixture.Home, () => { });

        var window = Show(model);

        window.KeyTextInput("s");
        window.KeyTextInput("e");
        window.KeyTextInput("c");

        var password = Password(window);

        Assert.Equal("•••", password.Display);
        Assert.DoesNotContain('s', password.Display);
    });

    private static MaskedInput Password(Window window) =>
        window.GetVisualDescendants().OfType<MaskedInput>().Single();

    private static Window Show(UnlockViewModel model)
    {
        var window = new Window { Content = new UnlockView { DataContext = model } };
        window.Show();

        // Focus is posted at Background priority, so let the dispatcher drain before asking — the
        // same ordering the real window goes through.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }
}
