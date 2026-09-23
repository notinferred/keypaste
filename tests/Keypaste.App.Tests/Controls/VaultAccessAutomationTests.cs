using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// D-0099 on the three master-password fields V.1b adds to Settings: the current password, the new
/// one and its confirmation.
/// </summary>
/// <remarks>
/// Each field is a <see cref="MaskedInput"/>, which inherits the control and not its evidence, so
/// each is held to the same differential the unlock field is, with a guard that the sweep reached a
/// drawn mask, and to the same refused paste.
/// </remarks>
public sealed class VaultAccessAutomationTests
{
    private const string _fixture = MaskedInputAutomationTests.Fixture;
    private const string _alpha = MaskedInputAutomationTests.Alpha;
    private const string _beta = MaskedInputAutomationTests.Beta;

    [Theory]
    [InlineData("CurrentPassword")]
    [InlineData("AccessNewPassword")]
    [InlineData("AccessConfirmPassword")]
    public Task Each_access_field_surface_depends_on_the_length_and_not_the_characters(string name) =>
        HeadlessSession.On(() =>
        {
            using var screen = new SettingsScreen();
            var field = screen.Field(name);

            field.Focus();
            screen.Window.KeyTextInput(_alpha);
            Assert.Equal(_alpha.Length, field.MaskedLength);
            var first = AutomationSurface.Of(field);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, field.MaskedLength);

            screen.Window.KeyTextInput(_beta);
            var second = AutomationSurface.Of(field);

            Assert.Equal(first, second);
        });

    [Theory]
    [InlineData("CurrentPassword", "Current master password")]
    [InlineData("AccessNewPassword", "New master password")]
    [InlineData("AccessConfirmPassword", "Confirm new master password")]
    public Task The_mask_reaches_the_automation_tree_for_each_access_field(string name, string placeholder) =>
        HeadlessSession.On(() =>
        {
            using var screen = new SettingsScreen();
            var field = screen.Field(name);

            field.Focus();
            screen.Window.KeyTextInput(_fixture);

            var surface = AutomationSurface.Of(field).Select(entry => entry.Text).ToList();
            Assert.Contains(new string('•', _fixture.Length), surface, StringComparer.Ordinal);
            Assert.Contains(placeholder, surface, StringComparer.Ordinal);
        });

    [Fact]
    public Task Nothing_on_the_settings_screen_exposes_any_of_the_three() => HeadlessSession.On(() =>
    {
        using var screen = new SettingsScreen();

        foreach (var name in new[] { "CurrentPassword", "AccessNewPassword", "AccessConfirmPassword" })
        {
            screen.Field(name).Focus();
            screen.Window.KeyTextInput(_fixture);
        }

        Assert.Equal(_fixture.Length, screen.Model.Access.CurrentMaskedLength);
        Assert.Equal(_fixture.Length, screen.Model.Access.NewMaskedLength);
        Assert.Equal(_fixture.Length, screen.Model.Access.ConfirmMaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Window, _fixture);
    });

    [Theory]
    [InlineData("CurrentPassword")]
    [InlineData("AccessNewPassword")]
    [InlineData("AccessConfirmPassword")]
    public Task Each_access_field_ignores_the_paste_gesture(string name) => HeadlessSession.On(async () =>
    {
        using var screen = new SettingsScreen();
        var field = screen.Field(name);

        field.Focus();
        screen.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        screen.Window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        await field.PasteCompleted;

        Assert.False(field.AllowsPaste);
        Assert.Null(field.Sink);
        Assert.Equal(0, field.MaskedLength);

        screen.Window.KeyTextInput(_alpha);
        Assert.Equal(_alpha.Length, field.MaskedLength);
    });

    /// <summary>Settings over an unlocked vault, with the new-password fields showing.</summary>
    private sealed class SettingsScreen : IDisposable
    {
        private readonly TempVault _fixture = new();
        private readonly AppVaultSession _session = new(new ManualClock());

        internal SettingsScreen()
        {
            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
            }

            Model = new SettingsViewModel(_session, _fixture.Home, new DesktopPreferences(_fixture.Home), _ => { });
            Model.Access.SetPassword = true;

            Window = new Window { Content = new SettingsView { DataContext = Model } };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        internal Window Window { get; }

        internal SettingsViewModel Model { get; }

        internal MaskedInput Field(string name) =>
            Window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == name);

        public void Dispose()
        {
            Window.Close();
            Model.Dispose();
            _session.Dispose();
            _fixture.Dispose();
        }
    }
}
