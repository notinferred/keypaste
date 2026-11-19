using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The one unlock form, as the whole-window lock and a vault locked in the sidebar both draw it.</summary>
public sealed class UnlockFormTests
{
    [Fact]
    public Task The_password_field_keeps_a_fields_height_with_the_keyboard_in_it_and_Unlock_is_a_large_button_under_it() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = Show(model);

        var password = Named<MaskedInput>(window, "Password");
        var unlock = Named<Button>(window, "Unlock");
        Assert.True(password.IsFocused);
        Assert.Equal(32, password.Bounds.Height);
        Assert.Equal(34, unlock.Bounds.Height);
        Assert.True(unlock.TranslatePoint(default, window)!.Value.Y > password.TranslatePoint(default, window)!.Value.Y);

        Named<Button>(window, "ChooseKeyfile").Focus();
        WindowInput.Drain();
        Assert.False(password.IsFocused);
        Assert.Equal(32, password.Bounds.Height);
    });

    [Fact]
    public Task Opening_the_create_form_drops_the_refusal_the_screen_reader_last_heard() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();
        var picker = new FakeVaultFilePicker { NewPath = Path.Combine(fixture.Home, "created.kdbx") };
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var model = new UnlockViewModel(session, fixture.Home, picker, () => { });
        var window = Show(model);
        var live = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("live"));

        model.Type('x');
        await model.UnlockAsync();
        WindowInput.Drain();
        Assert.Equal("Wrong password or key file.", live.Text);

        await model.StartCreateAsync();
        WindowInput.Drain();
        Assert.True(string.IsNullOrEmpty(live.Text));
    });

    /// <summary>A ghost class drawn after primary's wins, so a button carrying both shows no fill; the two are swapped, never combined.</summary>
    [Fact]
    public void No_button_carries_both_ghost_and_primary()
    {
        var views = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Keypaste.App", "Views"), "*.axaml");
        var both = views
            .SelectMany(view => XDocument.Load(view).Descendants().Select(element => (View: Path.GetFileName(view), Element: element)))
            .Where(found => Classes(found.Element) is var classes && classes.Contains("ghost") && classes.Contains("primary"))
            .Select(found => $"{found.View}: {found.Element.Attribute("Name")?.Value ?? found.Element.Name.LocalName}")
            .ToList();

        Assert.Empty(both);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "keypaste.app.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("the repository is not above the tests");
    }

    private static HashSet<string> Classes(XElement element)
    {
        var classes = (element.Attribute("Classes")?.Value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        classes.UnionWith(element.Attributes().Select(attribute => attribute.Name.LocalName).Where(name => name.StartsWith("Classes.", StringComparison.Ordinal)).Select(name => name["Classes.".Length..]));
        return classes;
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static MainWindow Show(UnlockViewModel model)
    {
        var window = new MainWindow();
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        WindowInput.Drain();
        return window;
    }
}
