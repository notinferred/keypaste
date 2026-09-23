using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The unlock screen in the app's own window, drawn by Skia, with a remembered vault so its
/// password field takes keystrokes, and the create and restore forms one step away.
/// </summary>
internal sealed class RenderedUnlock : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session = new(new ManualClock());
    private readonly FakeVaultFilePicker _picker = new();

    internal RenderedUnlock()
    {
        _fixture.RememberSelf();
        Model = new UnlockViewModel(_session, _fixture.Home, _picker, () => { });

        Window = new MainWindow();
        Window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = Model };
        Window.Show();
        WindowInput.Drain();
    }

    internal MainWindow Window { get; }

    internal UnlockViewModel Model { get; }

    /// <summary>Opens whatever shows the field <paramref name="name"/>, and returns it.</summary>
    internal async Task<MaskedInput> OpenField(string name)
    {
        switch (name)
        {
            case "Password":
                break;

            case "BackupPassword":
                BeginRestore();
                break;

            case "NewPassword" or "ConfirmPassword":
                _picker.NewPath = Path.Combine(_fixture.Home, "created.kdbx");
                await Model.StartCreateAsync();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "no such field on the unlock screen");
        }

        WindowInput.Drain();
        return Window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == name);
    }

    public void Dispose()
    {
        Window.Close();
        Model.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    /// <summary>Saves once so the vault has a backup, then opens the restore panel on it.</summary>
    private void BeginRestore()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "saved-again", Password = "p" });
            vault.Save();
        }

        // The selection was read before the backup existed, so choose the vault again.
        Model.SelectedPath = null;
        Assert.True(Model.Offer(_fixture.Path_));

        Model.StartRestoreCommand.Execute(null);
        Assert.NotNull(Model.Restore);
    }
}
