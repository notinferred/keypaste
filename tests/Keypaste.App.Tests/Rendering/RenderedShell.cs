using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The unlocked shell in the app's own window, drawn by Skia, with the chords launch binds and the
/// lock launch performs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lock is <c>App.ShowUnlock</c>'s, performed here.</b> <c>Ctrl/Cmd+L</c> reaches the
/// session through <see cref="App.Bind"/>, and the session's <c>Locked</c> event is answered as the
/// app answers it: posted to the UI thread, the shell disposed and the window's root swapped for
/// the unlock screen. <c>ShowUnlock</c> itself is private to the running app, which the headless
/// session does not compose, so this is a copy of it rather than a call to it.
/// </para>
/// <para>
/// The window is taller than <see cref="MainWindow"/>'s default so the entry pane's history, the
/// lowest surface checked, is inside the frame; <see cref="DrawnFrame.Of"/> fails if it is not.
/// </para>
/// </remarks>
internal sealed class RenderedShell : IDisposable
{
    internal const string Master = "correct horse battery staple";
    internal const string Current = "SENTINEL-CURRENT-PASSWORD-5e1d7a";
    internal const string Superseded = "SENTINEL-OLD-PASSWORD-4b8e21";
    internal const string EnvValue = "SENTINEL-ENV-VALUE-7d5e08";

    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private readonly string _directory;
    private readonly Shortcuts _shortcuts;
    private ShellViewModel? _shell;
    private UnlockViewModel? _unlock;

    internal RenderedShell(string directoryPrefix = "keypaste-drawn-")
    {
        _directory = Directory.CreateTempSubdirectory(directoryPrefix).FullName;
        var path = Path.Combine(_directory, "vault.kdbx");

        using (var vault = Vault.Create(path, Master))
        {
            vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = Superseded });
            vault.UpdateEntry(new VaultEntry { Title = "github", Username = "me", Password = Current });
            vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = EnvValue, GroupPath = "env/billing" });
            vault.Save();
        }

        // The unlock screen this vault was opened from remembers it, so the lock screen offers it again.
        Core.Recent.RecentVaults.Save(Core.Audit.KeypasteHome.RecentPath(_directory), [new Core.Recent.RecentVault(path, DateTimeOffset.UtcNow)]);

        Session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(Master))
        {
            Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
        }

        Window = new MainWindow { Height = 1000 };
        _shell = new ShellViewModel(Session, _directory, null, clipboard: new FakeClipboard(), clock: new ManualClock());
        Root.Content = new ShellView { DataContext = _shell };
        _shortcuts = App.Bind(Window, Session, () => _unlock, () => _shell);
        Session.Locked += OnLocked;

        Window.Show();
        Drain();
    }

    /// <summary>Every secret in the vault, for a frame that should hold none of them.</summary>
    internal static IReadOnlyList<string> Secrets { get; } = [Current, Superseded, EnvValue];

    internal AppVaultSession Session { get; }

    internal MainWindow Window { get; }

    internal ShellViewModel Shell => _shell ?? throw new InvalidOperationException("the shell has locked");

    internal ContentControl Root => Window.FindControl<ContentControl>("Root")!;

    internal bool IsLocked => Root.Content is UnlockView;

    internal static void Drain() => WindowInput.Drain();

    internal DrawnFrame Frame() => DrawnFrame.Capture(Window);

    /// <summary>The cell a surface draws its secret in, with the value it draws there.</summary>
    internal (RevealedValue Cell, string Value) Open(string surface) => surface switch
    {
        "current" => (OpenEntry("CurrentPassword"), Current),
        "revision" => (OpenRevision(), Superseded),
        "env" => (OpenEnv(), EnvValue),
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Navigates to where the masked field <paramref name="name"/> is showing, and returns it.</summary>
    internal MaskedInput OpenField(string name)
    {
        switch (name)
        {
            case "CurrentPassword" or "AccessNewPassword" or "AccessConfirmPassword":
                Show<SettingsViewModel>(DestinationKind.Settings).Access.SetPassword = true;
                break;

            case "NewEntryPassword":
                var adding = Show<EntriesViewModel>(DestinationKind.Entries);
                adding.BeginAddCommand.Execute(null);
                adding.GeneratePassword = false;
                break;

            case "ReplacementPassword":
                var editing = Show<EntriesViewModel>(DestinationKind.Entries);
                editing.Selected = editing.Rows.Single(row => row.Title == "github");
                Drain();
                editing.Detail!.EditCommand.Execute(null);
                break;

            case "NewEnvValue":
                var project = OpenProject();
                project.BeginAddCommand.Execute(null);
                project.GenerateValue = false;
                break;

            case "ReplacementEnvValue":
                var replacing = OpenProject();
                replacing.BeginReplace(replacing.Variables.Single(row => row.Key == "STRIPE_KEY"));
                break;

            case "SharePassphrase":
                Show<SharingViewModel>(DestinationKind.Sharing).RequirePassphrase = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "no such field in the shell");
        }

        Drain();
        return Named<MaskedInput>(name);
    }

    internal T Show<T>(DestinationKind kind)
    {
        Shell.Current = Destinations.All.Single(d => d.Kind == kind);
        Drain();
        return Assert.IsType<T>(Shell.Content);
    }

    internal void Hover(Visual control) => WindowInput.Hover(Window, control);

    /// <summary>A mouse press on the window at the centre of <paramref name="control"/>, hit-tested by the platform.</summary>
    internal void Press(Visual control) => WindowInput.Press(Window, control);

    internal void Release(Visual control) => WindowInput.Release(Window, control);

    internal void PressLock()
    {
        Window.KeyPressQwerty(PhysicalKey.L, _command);
        Window.KeyReleaseQwerty(PhysicalKey.L, _command);
        Drain();
    }

    internal T Named<T>(string name)
        where T : Control =>
        Window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    public void Dispose()
    {
        Session.Locked -= OnLocked;
        _shortcuts.Dispose();
        _shell?.Dispose();
        _unlock?.Dispose();
        Session.Dispose();
        Window.Close();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private RevealedValue OpenEntry(string cell)
    {
        var entries = Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        Drain();

        return Named<RevealedValue>(cell);
    }

    private RevealedValue OpenRevision()
    {
        OpenEntry("CurrentPassword");
        var history = Assert.IsType<EntriesViewModel>(Shell.Content).Detail!.History;
        history.ToggleCommand.Execute(null);
        Drain();
        history.Selected = history.Rows[0];
        Drain();

        return Named<RevealedValue>("RevisionPassword");
    }

    private EnvProjectViewModel OpenProject()
    {
        var env = Show<EnvSetsViewModel>(DestinationKind.EnvSets);
        env.OpenCommand.Execute("billing");
        Drain();

        return env.OpenProject!;
    }

    private RevealedValue OpenEnv()
    {
        OpenProject();

        return Window.GetVisualDescendants().OfType<RevealedValue>().Single(cell => cell.DataContext is EnvVariableRow);
    }

    private void OnLocked(object? sender, VaultLockReason reason) =>
        Dispatcher.UIThread.Post(() =>
        {
            _shell?.Dispose();
            _shell = null;

            _unlock?.Dispose();
            _unlock = new UnlockViewModel(Session, _directory, new FakeVaultFilePicker(), () => { });
            Root.Content = new UnlockView { DataContext = _unlock };
        });
}
