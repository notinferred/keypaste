using System.Text;
using Avalonia.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The macOS File menu's Import .kdbx… (N.1a1): it acts on the shell of the moment, and is off with
/// no shell or while an import is open. Launch attaches it only on macOS, where the macOS leg runs it.
/// </summary>
public sealed class AppMenuTests
{
    [Fact]
    public Task File_import_opens_the_import_dialog_on_the_open_vault_and_is_off_without_one() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var foreign = Path.Combine(fixture.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(foreign, Encoding.UTF8.GetBytes("foreign-source"), null, "Argon2id", "ChaCha20");

        ShellViewModel? shell = null;
        var bar = AppMenu.Build(() => shell);
        var file = Assert.Single(bar.Items.OfType<NativeMenuItem>());
        Assert.Equal("File", file.Header);
        var import = Assert.Single(file.Menu!.Items.OfType<NativeMenuItem>());
        Assert.Equal(AppMenu.ImportHeader, import.Header);
        Assert.False(import.IsEnabled, "Import is on with no vault open");

        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        shell = new ShellViewModel(session, fixture.Home, null, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker { ExistingPath = foreign });

        using (shell)
        {
            Assert.True(AppMenu.CanImport(shell));

            import.Command!.Execute(null);

            Assert.True(shell.HasImport);
            Assert.False(AppMenu.CanImport(shell), "Import is on while an import is open");
        }
    });
}
