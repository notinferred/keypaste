using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// A TOTP code, a generated password and a minted token, each through every way a reveal of it ends, read from the
/// frames Skia drew and from what the control and its view model still hold afterwards.
/// </summary>
/// <remarks>
/// Each row is a way the surface truly has. The generator is a modal dialog with one cell and has no other place or
/// other value to reach; a minted token is alone on its screen and never changes.
/// </remarks>
public sealed class DrawnRevealWaysOutTests
{
    private const string _otpItem = "aws";
    private const string _otherItem = "gitlab";
    private const string _otpSeed = "12345678901234567890";

    private static readonly IReadOnlyDictionary<string, string[]> _waysOut = new Dictionary<string, string[]>
    {
        ["totp"] = ["hide", "time", "window focus", "minimize", "window hidden", "lock", "another item", "search", "group", "another place", "edit form", "saved change", "another value", "menu"],
        ["generated"] = ["hide", "time", "window focus", "minimize", "window hidden", "lock", "cancel", "escape"],
        ["token"] = ["hide", "time", "window focus", "minimize", "window hidden", "lock", "done", "another place", "menu"],
    };

    public static TheoryData<string, string> WaysOut()
    {
        var rows = new TheoryData<string, string>();

        foreach (var (surface, ways) in _waysOut)
        {
            foreach (var way in ways)
            {
                rows.Add(surface, way);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(WaysOut))]
    public Task Each_way_out_takes_the_value_off_the_frame_and_out_of_what_holds_it(string surface, string way) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-ways-out-", seed: SeedItems);
        var clock = new ManualClock();
        shell.Window.SetValue(RevealedValue.ClockProperty, clock);
        var opened = Open(shell, surface);
        var style = TextStyle.Of(opened.Cell);

        shell.Tap(shell.ToggleOf(opened.Cell));
        var shown = shell.Frame();
        Assert.True(shown.Shows(opened.Drawn, style, shown.Of(opened.Cell, shell.Window)), "the value is not drawn before leaving");
        Assert.Equal(opened.Value, opened.Cell.Rendered);

        Leave(shell, clock, opened, way);
        RenderedShell.Drain();

        var after = shell.Frame();
        Assert.False(after.Shows(opened.Drawn, style, after.Everywhere), "the value is still in the frame");
        Assert.False(opened.Cell.IsRevealed);
        Assert.DoesNotContain(opened.Value, opened.Cell.Rendered, StringComparison.Ordinal);
        AssertInNoStyledProperty(opened.Cell, opened.Value);
        AutomationSurface.AssertNothingExposes(shell.Window, opened.Value);

        if (way == "another value")
        {
            Assert.NotSame(opened.Cell, RevealedValue.Shown);
            RevealedValue.HideShown();
        }
        else
        {
            Assert.Null(RevealedValue.Shown);
            Assert.DoesNotContain(shell.Window.GetVisualDescendants().OfType<RevealButton>(), button => (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("Hide", StringComparison.Ordinal));
        }

        opened.AssertForgotten?.Invoke(way);
    });

    [Fact]
    public Task A_code_shown_as_its_period_ends_is_drawn_again_as_the_next_code() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-ways-out-", seed: SeedItems);
        var opened = Open(shell, "totp");
        var style = TextStyle.Of(opened.Cell);
        var row = Assert.IsType<EntriesViewModel>(shell.Shell.Content).Detail!.Totp!;

        shell.Tap(shell.ToggleOf(opened.Cell));
        var shown = shell.Frame();
        Assert.True(shown.Shows(opened.Value, style, shown.Of(opened.Cell, shell.Window)), "the code is not drawn before its period ends");

        ((ManualClock)shell.Session.Clock).Advance(TimeSpan.FromSeconds(row.SecondsLeft));
        RenderedShell.Drain();

        var next = row.Reveal()!;
        Assert.NotEqual(opened.Value, next);
        var redrawn = shell.Frame();
        Assert.True(redrawn.Shows(next, style, redrawn.Of(opened.Cell, shell.Window)), "the next code is not drawn");
        Assert.False(redrawn.Shows(opened.Value, style, redrawn.Everywhere), "the old code is still in the frame");
        opened.Cell.EndReveal();
    });

    [Fact]
    public Task A_generated_value_shown_while_regenerated_is_drawn_again_as_the_new_value() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-ways-out-", seed: SeedItems);
        var opened = Open(shell, "generated");
        var style = TextStyle.Of(opened.Cell);
        var dialog = shell.Shell.GeneratorDialog!;

        shell.Tap(shell.ToggleOf(opened.Cell));
        var shown = shell.Frame();
        Assert.True(shown.Shows(opened.Value, style, shown.Of(opened.Cell, shell.Window)), "the value is not drawn before regenerating");

        shell.Tap(shell.Named<Button>("Regenerate"));

        var next = dialog.Reveal()!;
        Assert.NotEqual(opened.Value, next);
        var redrawn = shell.Frame();
        Assert.True(redrawn.Shows(next, style, redrawn.Of(opened.Cell, shell.Window)), "the new value is not drawn");
        Assert.False(redrawn.Shows(opened.Value, style, redrawn.Everywhere), "the old value is still in the frame");
        opened.Cell.EndReveal();
    });

    private static void SeedItems(Vault vault)
    {
        var name = new EntryName(string.Empty, _otpItem);
        vault.AddEntry(new VaultEntry { Title = _otpItem, Username = "me", Password = RenderedShell.Current });
        Assert.True(vault.SetFields(name, [new FieldWrite("TimeOtp-Secret", _otpSeed), new FieldWrite("TimeOtp-Length", "8")]));
        vault.AddEntry(new VaultEntry { GroupPath = "work", Title = _otherItem, Username = "me", Password = RenderedShell.Superseded });
    }

    private static Opened Open(RenderedShell shell, string surface)
    {
        switch (surface)
        {
            case "totp":
                var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
                entries.Selected = entries.Rows.Single(row => row.Title == _otpItem);
                RenderedShell.Drain();
                var code = entries.Detail!.Totp!.Reveal()!;
                return new Opened(shell.Named<RevealedValue>("CurrentTotp"), code, code, null);

            case "generated":
                shell.Shell.OpenGeneratorCommand.Press();
                RenderedShell.Drain();
                var dialog = shell.Shell.GeneratorDialog!;
                var generated = dialog.Reveal()!;
                return new Opened(shell.Named<RevealedValue>("GeneratedValue"), generated, generated, way =>
                {
                    if (way is "cancel" or "escape" or "lock")
                    {
                        Assert.Null(dialog.Reveal());
                        Assert.True(dialog.Value.IsZeroed);
                    }
                });

            case "token":
                var tokens = shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
                tokens.OpenFormCommand.Press();
                tokens.Name = "ci-staging";
                tokens.Scope = "read:billing/dev/*";
                tokens.CreateCommand.Press();
                RenderedShell.Drain();
                var minted = tokens.Minted!;
                var token = minted.Reveal()!;

                // A whole token is wider than its cell at this window's width and is cut with an ellipsis; its first 40 characters, past the id and into the secret, fit.
                return new Opened(shell.Named<RevealedValue>("MintedToken"), token, token[..40], way =>
                {
                    if (way is "done" or "another place" or "lock")
                    {
                        Assert.Null(minted.Reveal());
                    }
                });

            default:
                throw new ArgumentOutOfRangeException(nameof(surface));
        }
    }

    private static void Leave(RenderedShell shell, ManualClock clock, Opened opened, string way)
    {
        switch (way)
        {
            case "hide":
                shell.Tap(shell.ToggleOf(opened.Cell));
                break;
            case "time":
                clock.Advance(RevealedValue.ShownFor);
                break;
            case "window focus":
                WindowInput.Deactivate(shell.Window);
                break;
            case "minimize":
                shell.Window.WindowState = WindowState.Minimized;
                RenderedShell.Drain();
                shell.Window.WindowState = WindowState.Normal;
                break;
            case "window hidden":
                shell.Window.Hide();
                RenderedShell.Drain();
                shell.Window.Show();
                break;
            case "lock":
                shell.PressLock();
                Assert.True(shell.IsLocked);
                break;
            case "cancel":
                shell.Tap(shell.Named<Button>("CloseGenerator"));
                break;
            case "escape":
                shell.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                shell.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                break;
            case "done":
                shell.Tap(shell.Named<Button>("DoneToken"));
                break;
            case "another place" when shell.Shell.Content is ScopedTokensViewModel:
                shell.Shell.Current = Destinations.All.Single(d => d.Kind == DestinationKind.Settings);
                shell.Shell.ConfirmDiscardCommand.Press();
                break;
            case "another place":
                shell.Show<SettingsViewModel>(DestinationKind.Settings);
                break;
            case "another item":
                Items(shell).Selected = Items(shell).Rows.Single(row => row.Title == _otherItem);
                break;
            case "search":
                Items(shell).Search = "no item is called this";
                break;
            case "group":
                Items(shell).SelectedGroup = Items(shell).Groups.Single(group => group.Path == "work");
                break;
            case "edit form":
                Items(shell).Detail!.EditCommand.Press();
                break;
            case "saved change":
                Items(shell).Detail!.Reload();
                break;
            case "another value":
                shell.Tap(shell.ToggleOf(shell.Named<RevealedValue>("CurrentPassword")));
                break;
            case "menu" when shell.Shell.Content is EntriesViewModel:
                shell.Tap(shell.Named<ToggleButton>("AddEntry"));
                break;
            case "menu" when shell.Shell.Content is ScopedTokensViewModel tokens:
                // A right-click chooses the row, which an uncopied token would hold off.
                shell.Tap(shell.Named<Button>("CopyToken"));
                RenderedShell.Drain();
                Assert.Null(tokens.UnsavedDraft);
                OpenSidebarMenu(shell);
                break;
            case "menu":
                OpenSidebarMenu(shell);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(way));
        }
    }

    private static EntriesViewModel Items(RenderedShell shell) => Assert.IsType<EntriesViewModel>(shell.Shell.Content);

    private static void OpenSidebarMenu(RenderedShell shell)
    {
        var nav = shell.Named<ListBox>("Nav");
        var billing = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");
        var container = Assert.IsAssignableFrom<Control>(nav.ContainerFromItem(billing));
        container.BringIntoView();
        RenderedShell.Drain();
        shell.Window.UpdateLayout();

        var centre = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), shell.Window)!.Value;
        shell.Window.MouseDown(centre, MouseButton.Right);
        shell.Window.MouseUp(centre, MouseButton.Right);
        RenderedShell.Drain();

        var menu = container.GetSelfAndVisualAncestors().Concat(container.GetVisualDescendants()).OfType<Control>().Select(FlyoutBase.GetAttachedFlyout).OfType<MenuFlyout>().First();
        Assert.True(menu.IsOpen, "the sidebar's menu did not open");
        menu.Hide();
    }

    private static void AssertInNoStyledProperty(RevealedValue cell, string value)
    {
        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(cell))
        {
            Assert.DoesNotContain(value, cell.GetValue(property)?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private sealed record Opened(RevealedValue Cell, string Value, string Drawn, Action<string>? AssertForgotten);
}
