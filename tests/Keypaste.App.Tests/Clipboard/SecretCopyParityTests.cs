using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Clipboard;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// Every surface that holds a secret copies it the same way and loses it the same way (V.10,
/// D-0300): the current password, an env value and a history revision.
/// </summary>
/// <remarks>
/// <para>
/// Driven through the rendered shell: the drawn Copy button is pressed with the keyboard, the
/// shell's own countdown puts the value on the clipboard through <see cref="AvaloniaClipboard"/>,
/// and the headless platform's clipboard is what is read back. The countdown's rules have their own
/// tests; what these hold is that each surface reaches them.
/// </para>
/// <para>
/// The lock is performed as <c>App.ShowUnlock</c> performs it: the session locks and the shell is
/// disposed, which is what gives the clipboard back.
/// </para>
/// </remarks>
public sealed class SecretCopyParityTests
{
    private const string _master = "correct horse battery staple";
    private const string _current = "SENTINEL-CURRENT-PASSWORD-5e1d7a";
    private const string _superseded = "SENTINEL-OLD-PASSWORD-4b8e21";
    private const string _envValue = "SENTINEL-ENV-VALUE-7d5e08";

    public static TheoryData<string> Surfaces => ["current", "env", "revision"];

    /// <summary>
    /// The preflight the rest depend on: the headless platform's clipboard holds what the adapter
    /// wrote, so the assertions below are about a clipboard and not about a stub that drops writes.
    /// </summary>
    [Fact]
    public Task The_headless_clipboard_round_trips_a_secret() => HeadlessSession.On(async () =>
    {
        var window = new Window();
        window.Show();
        var clipboard = new AvaloniaClipboard(window);

        Assert.True(await clipboard.TrySetSecretAsync(_current));
        Assert.Equal(Hash(_current), await clipboard.TryReadHashAsync());

        Assert.True(await clipboard.TryClearAsync());
        Assert.NotEqual(Hash(_current), await clipboard.TryReadHashAsync());

        window.Close();
    });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Each_surface_copies_with_the_countdown_and_the_countdown_clears_it(string surface) =>
        HeadlessSession.On(async () =>
        {
            using var screen = new ShellScreen();
            var value = await screen.Copy(surface);

            Assert.Equal(Hash(value), await screen.Clipboard.TryReadHashAsync());
            Assert.True(screen.Shell.Clipboard.IsCounting);

            screen.Clock.Advance(ClipboardClear.DefaultWindow);
            await screen.Settle();

            Assert.False(screen.Shell.Clipboard.IsCounting);
            Assert.NotEqual(Hash(value), await screen.Clipboard.TryReadHashAsync());
        });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Each_surface_is_cleared_by_clear_now(string surface) => HeadlessSession.On(async () =>
    {
        using var screen = new ShellScreen();
        var value = await screen.Copy(surface);
        Assert.Equal(Hash(value), await screen.Clipboard.TryReadHashAsync());

        screen.Press(screen.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, "Clear now")));
        await screen.Settle();

        Assert.NotEqual(Hash(value), await screen.Clipboard.TryReadHashAsync());
    });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Each_surface_is_cleared_by_the_lock(string surface) => HeadlessSession.On(async () =>
    {
        using var screen = new ShellScreen();
        var value = await screen.Copy(surface);
        Assert.Equal(Hash(value), await screen.Clipboard.TryReadHashAsync());

        screen.Session.Lock(VaultLockReason.Manual);
        screen.Window.Content = null;
        screen.Shell.Dispose();
        await screen.Settle();

        Assert.NotEqual(Hash(value), await screen.Clipboard.TryReadHashAsync());
    });

    /// <summary>
    /// A revision list read before an edit names a different revision at the same index, and
    /// copying from it puts nothing on the clipboard.
    /// </summary>
    [Fact]
    public Task A_stale_revision_is_refused_and_the_list_read_again() => HeadlessSession.On(async () =>
    {
        using var screen = new ShellScreen();
        var entries = screen.OpenRevision();
        var stale = entries.Detail!.History.Selected!;

        entries.Detail.EditCommand.Execute(null);
        entries.Detail.DraftUsername = "someone else";
        entries.Detail.SaveCommand.Execute(null);
        ShellScreen.Drain();

        await stale.CopyPasswordCommand.ExecuteAsync();
        await screen.Settle();

        Assert.False(screen.Shell.Clipboard.IsCounting);
        var held = await screen.Clipboard.TryReadHashAsync();
        Assert.NotEqual(Hash(_superseded), held);
        Assert.NotEqual(Hash(_current), held);
        Assert.Contains("changed since its history was read", entries.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.NotSame(stale, entries.Detail.History.Rows[0]);
    });

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>A rendered shell over a vault with an entry that has a history and a project.</summary>
    private sealed class ShellScreen : IDisposable
    {
        private readonly string _directory;

        internal ShellScreen()
        {
            _directory = Directory.CreateTempSubdirectory("keypaste-copy-parity-").FullName;
            var path = Path.Combine(_directory, "vault.kdbx");

            using (var vault = Vault.Create(path, _master))
            {
                vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = _superseded });
                vault.UpdateEntry(new VaultEntry { Title = "github", Username = "me", Password = _current });
                vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = _envValue, GroupPath = "env/billing" });
                vault.Save();
            }

            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
            }

            Window = new Window();
            Clipboard = new AvaloniaClipboard(Window);
            Shell = new ShellViewModel(Session, _directory, null, clipboard: Clipboard, clock: Clock);
            Window.Content = new ShellView { DataContext = Shell };
            Window.Show();
            Drain();
        }

        internal AppVaultSession Session { get; }

        internal ManualClock Clock { get; } = new();

        internal Window Window { get; }

        internal AvaloniaClipboard Clipboard { get; }

        internal ShellViewModel Shell { get; }

        internal static void Drain() => Dispatcher.UIThread.RunJobs();

        /// <summary>Presses the Copy button the surface draws, and returns what it should have copied.</summary>
        internal async Task<string> Copy(string surface)
        {
            (string Value, Button Button) target = surface switch
            {
                "current" => (_current, Named(OpenEntry(), "CopyPassword")),
                "revision" => (_superseded, Named(OpenRevision(), "CopyRevisionPassword")),
                "env" => (_envValue, EnvCopy()),
                _ => throw new ArgumentOutOfRangeException(nameof(surface)),
            };

            Press(target.Button);
            await Settle();

            return target.Value;
        }

        internal EntriesViewModel OpenEntry()
        {
            Shell.Current = Destinations.All.Single(d => d.Kind == DestinationKind.Entries);
            Drain();

            var entries = Assert.IsType<EntriesViewModel>(Shell.Content);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            Drain();

            return entries;
        }

        internal EntriesViewModel OpenRevision()
        {
            var entries = OpenEntry();
            entries.Detail!.History.ToggleCommand.Execute(null);
            Drain();
            entries.Detail.History.Selected = entries.Detail.History.Rows[0];
            Drain();

            return entries;
        }

        internal void Press(Button button)
        {
            Assert.True(button.IsEffectivelyEnabled);
            button.Focus();
            Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Drain();
        }

        internal async Task Settle()
        {
            Drain();
            await Shell.Clipboard.SettledAsync();
            Drain();
        }

        public void Dispose()
        {
            Shell.Dispose();
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

        private Button EnvCopy()
        {
            Shell.Current = Destinations.All.Single(d => d.Kind == DestinationKind.EnvSets);
            Drain();

            var env = Assert.IsType<EnvSetsViewModel>(Shell.Content);
            env.OpenCommand.Execute("billing");
            Drain();

            return Window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is EnvVariableRow && Equals(button.Content, "Copy"));
        }

        private Button Named(EntriesViewModel entries, string name)
        {
            Assert.NotNull(entries.Detail);
            return Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
        }
    }
}
