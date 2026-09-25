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

    public async Task<string?> PickExportDestinationAsync(string suggestedName)
    {
        // No overwrite prompt, for PickNewAsync's reason: an export refuses an occupied path itself.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export an encrypted copy of this vault",
            SuggestedFileName = suggestedName,
            DefaultExtension = "kdbx",
            ShowOverwritePrompt = false,
            FileTypeChoices = [_vault],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickKeyfileAsync()
    {
        // No type filter: KeePassXC's keyfiles are .keyx or .key, and older ones are any file at all.
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a keyfile",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickDotEnvAsync()
    {
        // No type filter: .env, .env.local and env.production are all the same kind of file.
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a .env file",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickReferenceFileAsync(string suggestedName, string? directory)
    {
        var start = directory is null ? null : await top.StorageProvider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export .env.keypaste",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = start,
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the project's directory",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
