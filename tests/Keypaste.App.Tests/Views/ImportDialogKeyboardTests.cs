using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The import dialog keeps the keyboard: after unlock Enter imports, and Esc cancels wherever focus is.</summary>
public sealed class ImportDialogKeyboardTests
{
    private const string _sourcePassword = "import-source";

    [Fact]
    public Task After_unlock_focus_moves_to_the_import_button_and_Enter_imports() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        var announced = new List<string>();
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _, _) => { }, announced.Add);
        var window = Show(import);

        foreach (var c in _sourcePassword)
        {
            import.TypePassword(c);
        }

        Drain(import.UnlockAsync());

        var confirm = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ImportConfirm");
        Assert.True(confirm.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(["Imported 3 items from foreign.kdbx"], announced);
    });

    [Fact]
    public Task Escape_cancels_with_focus_outside_the_dialog() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _, _) => { }, _ => { });
        var closed = false;
        import.Closed += (_, _) => closed = true;
        var outside = new Button { Content = "Behind" };
        var window = Show(import, outside);

        outside.Focus();
        Assert.True(outside.IsFocused);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.True(closed);
    });

    [Fact]
    public Task Escape_in_the_password_field_cancels_the_dialog() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _, _) => { }, _ => { });
        var closed = false;
        import.Closed += (_, _) => closed = true;
        var window = Show(import);

        Assert.Equal("ImportPassword", (window.FocusManager?.GetFocusedElement() as Control)?.Name);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.True(closed);
    });

    /// <summary>
    /// The imported file's password field answers the paste gesture as the master-password fields do (D-0495):
    /// one trailing line break goes, nothing of it is on the screen, and what was pasted opens the file.
    /// </summary>
    [Fact]
    public Task The_password_field_takes_a_paste_that_opens_the_file() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        var clipboard = new FakeClipboard();
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _, _) => { }, _ => { }, paste: clipboard.TryPasteIntoAsync);
        var window = Show(import);
        var field = PasswordField(window);
        clipboard.Plant(_sourcePassword + "\n");

        await Paste(window, field);

        Assert.True(field.AllowsPaste);
        Assert.Equal(1, clipboard.ReadCount);
        Assert.Equal(_sourcePassword.Length, field.MaskedLength);
        AutomationSurface.AssertNothingExposes(window, _sourcePassword);

        Drain(import.UnlockAsync());

        Assert.True(import.IsDecrypted, import.Message);
        Assert.Equal(0, import.PasswordLength);
    });

    /// <summary>What no keyboard can type is refused whole, and the dialog says so.</summary>
    [Fact]
    public Task The_password_field_refuses_a_paste_no_keyboard_could_type() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        var clipboard = new FakeClipboard();
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _, _) => { }, _ => { }, paste: clipboard.TryPasteIntoAsync);
        var window = Show(import);
        clipboard.Plant("import\tsource");

        await Paste(window, PasswordField(window));

        Assert.Equal(0, import.PasswordLength);
        Assert.Equal(SecretField.NoteFor(PasteOutcome.Refused), import.Message);
    });

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    public Task Closing_the_dialog_gives_the_keyboard_back_to_the_new_menu(string how) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        var picker = new FakeVaultFilePicker { ExistingPath = Source(fixture) };
        using var shell = new ShellViewModel(session, fixture.Home, null, clock: new ManualClock(AppClock.Start), picker: picker);
        var window = new Window { Width = 1280, Height = 800, Content = new ShellView { DataContext = shell } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "AddEntry");
        button.Focus();
        Drain(shell.ImportCommand.ExecuteAsync());
        Assert.NotNull(shell.Import);
        Assert.False(button.IsFocused);

        if (how == "cancel")
        {
            shell.Import!.CancelCommand.Execute(null);
        }
        else
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(shell.HasImport);
        Assert.Same(button, window.FocusManager?.GetFocusedElement());
        window.Close();
    });

    /// <summary>
    /// The export dialog keeps the keyboard as the KeePass database one does: once read, its import button has focus and is
    /// the one amber element, Enter imports, the report's Done takes focus and the amber, and Esc closes it, saying what it did.
    /// </summary>
    [Fact]
    public Task The_export_dialog_imports_on_Enter_moves_to_Done_and_closes_on_Escape() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        var announced = new List<string>();
        var path = Path.Combine(fixture.Home, "keepassxc.csv");
        File.WriteAllText(path, "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\"\n\"Root\",\"site\",\"maya\",\"kpxc-pw-site\",\"\",\"\"\n");
        using var import = new ExportImportViewModel(session, path, announced.Add);
        var window = Show(new ExportImportView { DataContext = import });

        Drain(import.ReadAsync());

        var confirm = Named(window, "ExportConfirm");
        Assert.True(confirm.IsFocused);
        AmberElements.AssertOne(window, confirm, "the export dialog's preview");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(import.IsImported, import.Message);
        var done = Named(window, "ExportDone");
        Assert.True(done.IsFocused);
        AmberElements.AssertOne(window, done, "the export dialog's report");
        Assert.Empty(announced);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(["Imported 1 item from keepassxc.csv"], announced);
        window.Close();
    });

    private static Button Named(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static AppVaultSession Open(TempVault fixture)
    {
        var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        return session;
    }

    private static string Source(TempVault fixture)
    {
        var source = Path.Combine(fixture.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(source, Encoding.UTF8.GetBytes(_sourcePassword), null, "Argon2id", "ChaCha20");
        return source;
    }

    private static Window Show(KdbxImportViewModel import, Control? behind = null)
    {
        var panel = new StackPanel();

        if (behind is not null)
        {
            panel.Children.Add(behind);
        }

        panel.Children.Add(new KdbxImportView { DataContext = import });
        var window = new Window { Content = panel };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Tall enough for the export dialog's report, so its Done is drawn.</summary>
    private static Window Show(ExportImportView view)
    {
        var window = new Window { Width = 900, Height = 1000, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static MaskedInput PasswordField(Window window) =>
        window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == "ImportPassword");

    private static async Task Paste(Window window, MaskedInput field)
    {
        field.Focus();
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        await field.PasteCompleted;
    }

    private static void Drain(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
