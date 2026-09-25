using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Internal;
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
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _) => { }, announced.Add);
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

        Assert.Equal(["Imported 3 entries from foreign.kdbx"], announced);
    });

    [Fact]
    public Task Escape_cancels_with_focus_outside_the_dialog() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var session = Open(fixture);
        using var import = new KdbxImportViewModel(session, Source(fixture), (_, _) => { }, _ => { });
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

    private static AppVaultSession Open(TempVault fixture)
    {
        var session = new AppVaultSession(new ManualClock(), home: fixture.Home);
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
