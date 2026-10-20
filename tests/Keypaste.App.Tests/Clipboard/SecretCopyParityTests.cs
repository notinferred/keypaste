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
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// Every surface that holds a secret copies it the same way and loses it the same way (V.10,
/// D-0300): the current password, an env value and a history revision. Each copy, and the opening
/// of a web address, fills in the value's KeePass placeholders from its own item, and one keypaste
/// cannot fill leaves the clipboard as it was (D-0465).
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
    private const string _fieldValue = "SENTINEL-FIELD-VALUE-3a9c51";
    private const string _plainFieldValue = "SENTINEL-PLAIN-FIELD-6b2e40";
    private const string _deployKey = "SENTINEL-DEPLOY-KEY-8c3f17";
    private const string _dbPassword = "SENTINEL-DB-PASSWORD-2e94b6";
    private const string _before = "SENTINEL-ON-THE-CLIPBOARD-BEFORE-91d4a0";

    public static TheoryData<string> Surfaces => ["current", "env", "revision", "field", "plain-field"];

    public static TheoryData<string> Fills => ["current", "username", "url", "field", "revision", "env"];

    public static TheoryData<string, string> Refusals => new()
    {
        { "current", "holds the KeePass placeholder {REF:…}, which keypaste does not fill in" },
        { "username", "holds the KeePass placeholder {TOTP}, which keypaste does not fill in" },
        { "url", "holds the KeePass placeholder {TOTP}, which keypaste does not fill in" },
        { "field", "holds the KeePass placeholder {S:…}, which names no field of its item" },
        { "revision", "holds the KeePass placeholder {UUID}, which keypaste does not fill in" },
        { "env", "holds the KeePass placeholder {REF:…}, which keypaste does not fill in" },
    };

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
    /// Each copy hands on its value with its placeholders filled in from its own item, a revision
    /// from that revision's own fields, and counts it down, since what it filled in may be a secret.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fills))]
    public Task Each_copy_fills_in_its_own_items_placeholders_and_counts_it_down(string surface) =>
        HeadlessSession.On(async () =>
        {
            using var screen = new ShellScreen();
            var value = await screen.CopyFilled(surface);

            Assert.Equal(Hash(value), await screen.Clipboard.TryReadHashAsync());
            Assert.True(screen.Shell.Clipboard.IsCounting);
        });

    [Theory]
    [MemberData(nameof(Refusals))]
    public Task A_value_keypaste_cannot_fill_in_leaves_the_clipboard_as_it_was_and_says_why(string surface, string reason) =>
        HeadlessSession.On(async () =>
        {
            using var screen = new ShellScreen();
            Assert.True(await screen.Clipboard.TrySetPlainAsync(_before));

            var subject = await screen.CopyRefused(surface);

            Assert.False(screen.Shell.Clipboard.IsCounting);
            Assert.Equal(Hash(_before), await screen.Clipboard.TryReadHashAsync());
            Assert.Equal($"{subject} {reason}, so it was not copied.", screen.Error);
        });

    [Fact]
    public Task Opening_a_web_address_fills_it_in() => HeadlessSession.On(() =>
    {
        using var screen = new ShellScreen();

        screen.Press(screen.Named(screen.OpenEntry("ci"), "OpenUrl"));

        Assert.Equal(["https://example.test/ci-bot"], screen.Launcher.Opened.Select(uri => uri.AbsoluteUri));
    });

    [Fact]
    public Task A_web_address_keypaste_cannot_fill_in_is_not_opened() => HeadlessSession.On(() =>
    {
        using var screen = new ShellScreen();
        var entries = screen.OpenEntry("legacy");

        screen.Press(screen.Named(entries, "OpenUrl"));

        Assert.Empty(screen.Launcher.Opened);
        Assert.Equal(
            "The web address holds the KeePass placeholder {TOTP}, which keypaste does not fill in, so it was not opened.",
            entries.Error);
    });

    /// <summary>
    /// A revision list read before an edit names a different revision at the same index, and
    /// copying from it puts nothing on the clipboard.
    /// </summary>
    [Fact]
    public Task A_stale_revision_is_refused_and_the_list_read_again() => HeadlessSession.On(async () =>
    {
        using var screen = new ShellScreen();
        var entries = screen.OpenRevision("github");
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

    /// <summary>
    /// A rendered shell over a vault with an entry that has a history and a project, an item and a
    /// project whose values fill in from their own fields, and an item and a project whose values
    /// hold placeholders keypaste does not fill in.
    /// </summary>
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
                vault.SetFields(new EntryName(string.Empty, "github"), [new FieldWrite("API_TOKEN", _fieldValue), new FieldWrite("Region", _plainFieldValue, Protect: false)]);
                vault.UpdateEntry(new VaultEntry { Title = "github", Username = "me", Password = _current });
                ProjectVariables.Set(vault, "billing", "STRIPE_KEY", _envValue);

                var ci = new EntryName(string.Empty, "ci");
                vault.AddEntry(new VaultEntry { Title = "ci", Username = "ci-old", Password = "{USERNAME}-was" });
                vault.SetFields(ci, [new FieldWrite("DEPLOY_KEY", _deployKey), new FieldWrite("CONN", "u:{PASSWORD}@db")]);
                vault.UpdateEntry(new VaultEntry { Title = "ci", Username = "{TITLE}-bot", Password = "{S:DEPLOY_KEY}:{USERNAME}", Url = "https://example.test/{USERNAME}" });
                ProjectVariables.Set(vault, "deploy", "DB_PASSWORD", _dbPassword);
                ProjectVariables.Set(vault, "deploy", "DATABASE_URL", "postgres://u:{S:DB_PASSWORD}@db");

                var legacy = new EntryName(string.Empty, "legacy");
                vault.AddEntry(new VaultEntry { Title = "legacy", Username = "{TOTP}", Password = "{UUID}" });
                vault.SetFields(legacy, [new FieldWrite("TOKEN", "{S:MISSING}")]);
                vault.UpdateEntry(new VaultEntry { Title = "legacy", Username = "{TOTP}", Password = "{REF:P@I:0123}", Url = "https://example.test/{TOTP}" });
                ProjectVariables.Set(vault, "relay", "API_KEY", "{REF:P@I:0123}");

                vault.Save();
            }

            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
            }

            Window = new Window();
            Clipboard = new AvaloniaClipboard(Window);
            Shell = new ShellViewModel(Session, _directory, null, clipboard: Clipboard, clock: Clock, web: Launcher);
            Window.Content = new ShellView { DataContext = Shell };
            Window.Show();
            Drain();
        }

        internal AppVaultSession Session { get; }

        internal ManualClock Clock { get; } = new(AppClock.Start);

        internal Window Window { get; }

        internal AvaloniaClipboard Clipboard { get; }

        internal ShellViewModel Shell { get; }

        internal RecordingLauncher Launcher { get; } = new();

        /// <summary>What the screen showing reports.</summary>
        internal string? Error => Shell.Content switch
        {
            EntriesViewModel entries => entries.Error,
            EnvSetsViewModel env => env.Error,
            _ => null,
        };

        internal static void Drain() => Dispatcher.UIThread.RunJobs();

        /// <summary>Presses the Copy button the surface draws, and returns what it should have copied.</summary>
        internal async Task<string> Copy(string surface)
        {
            (string Value, Button Button) target = surface switch
            {
                "current" => (_current, Named(OpenEntry("github"), "CopyPassword")),
                "revision" => (_superseded, Named(OpenRevision("github"), "CopyRevisionPassword")),
                "env" => (_envValue, EnvCopy("billing", "STRIPE_KEY")),
                "field" => (_fieldValue, FieldCopy("github", "API_TOKEN")),
                "plain-field" => (_plainFieldValue, FieldCopy("github", "Region")),
                _ => throw new ArgumentOutOfRangeException(nameof(surface)),
            };

            Press(target.Button);
            await Settle();

            return target.Value;
        }

        /// <summary>Presses the Copy button the surface draws for a value that fills in from its own item, and returns what it should have copied.</summary>
        internal async Task<string> CopyFilled(string surface)
        {
            (string Value, Button Button) target = surface switch
            {
                "current" => ($"{_deployKey}:ci-bot", Named(OpenEntry("ci"), "CopyPassword")),
                "username" => ("ci-bot", Bound(OpenEntry("ci").Detail!.CopyUsernameCommand)),
                "url" => ("https://example.test/ci-bot", Named(OpenEntry("ci"), "CopyUrl")),
                "field" => ($"u:{_deployKey}:ci-bot@db", FieldCopy("ci", "CONN")),
                "revision" => ("ci-old-was", Named(OpenRevision("ci"), "CopyRevisionPassword")),
                "env" => ($"postgres://u:{_dbPassword}@db", EnvCopy("deploy", "DATABASE_URL")),
                _ => throw new ArgumentOutOfRangeException(nameof(surface)),
            };

            Press(target.Button);
            await Settle();

            return target.Value;
        }

        /// <summary>Presses the Copy button the surface draws for a value holding a placeholder keypaste does not fill in, and returns how its refusal should name the value.</summary>
        internal async Task<string> CopyRefused(string surface)
        {
            (string Subject, Button Button) target = surface switch
            {
                "current" => ("The password", Named(OpenEntry("legacy"), "CopyPassword")),
                "username" => ("The username", Bound(OpenEntry("legacy").Detail!.CopyUsernameCommand)),
                "url" => ("The web address", Named(OpenEntry("legacy"), "CopyUrl")),
                "field" => ("TOKEN", FieldCopy("legacy", "TOKEN")),
                "revision" => RevisionCopy("legacy"),
                "env" => ("API_KEY", EnvCopy("relay", "API_KEY")),
                _ => throw new ArgumentOutOfRangeException(nameof(surface)),
            };

            Press(target.Button);
            await Settle();

            return target.Subject;
        }

        internal EntriesViewModel OpenEntry(string title)
        {
            Shell.Current = Destinations.All.Single(d => d.Kind == DestinationKind.Entries);
            Drain();

            var entries = Assert.IsType<EntriesViewModel>(Shell.Content);
            entries.Selected = entries.Rows.Single(row => row.Title == title);
            Drain();

            return entries;
        }

        internal EntriesViewModel OpenRevision(string title)
        {
            var entries = OpenEntry(title);
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

        internal Button Named(EntriesViewModel entries, string name)
        {
            Assert.NotNull(entries.Detail);
            return Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
        }

        private Button EnvCopy(string project, string key)
        {
            Shell.Current = Destinations.All.Single(d => d.Kind == DestinationKind.EnvSets);
            Drain();

            var env = Assert.IsType<EnvSetsViewModel>(Shell.Content);
            env.OpenCommand.Execute(project);
            Drain();

            return Window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is EnvVariableRow row && row.Key == key && Equals(button.Content, "Copy"));
        }

        private Button FieldCopy(string title, string field)
        {
            OpenEntry(title);

            return Window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is EntryFieldRow row && row.Name == field && Equals(button.Content, "Copy"));
        }

        private (string Subject, Button Button) RevisionCopy(string title)
        {
            var entries = OpenRevision(title);

            return ($"The password from {entries.Detail!.History.Selected!.When}", Named(entries, "CopyRevisionPassword"));
        }

        private Button Bound(object command) =>
            Window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
    }

    /// <summary>Records each address the item pane asks the platform to open.</summary>
    private sealed class RecordingLauncher : IWebLauncher
    {
        internal List<Uri> Opened { get; } = [];

        public Task<bool> OpenAsync(Uri address)
        {
            Opened.Add(address);
            return Task.FromResult(true);
        }
    }
}
