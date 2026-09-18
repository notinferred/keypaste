using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Keypaste.App.Session;

namespace Keypaste.App.Views;

/// <summary>The real pickers, over the window's storage provider.</summary>
/// <remarks>
/// It reads a local path off the chosen item and never opens it for writing. Asking Avalonia for a
/// stream would create the file, and an empty file at the chosen path is exactly what
/// <see cref="Keypaste.Core.VaultCreation"/> refuses — so the picker would make every create fail
/// on the rule that exists to protect a vault.
/// </remarks>
internal sealed class StorageProviderPicker(TopLevel top) : IVaultFilePicker
{
    private static readonly FilePickerFileType _vault =
        new("KeePass vault") { Patterns = ["*.kdbx"] };

    public async Task<string?> PickExistingAsync()
    {
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a keypaste vault",
            AllowMultiple = false,
            FileTypeFilter = [_vault],
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewAsync()
    {
        // The overwrite prompt is off because keypaste refuses an occupied path itself, and a
        // platform dialog that asked "replace it?" would be offering something keypaste will not do.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create a keypaste vault",
            SuggestedFileName = "vault.kdbx",
            DefaultExtension = "kdbx",
            ShowOverwritePrompt = false,
            FileTypeChoices = [_vault],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }
}
