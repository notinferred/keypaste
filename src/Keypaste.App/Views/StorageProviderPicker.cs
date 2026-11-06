using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Keypaste.App.Resources;
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

    private static readonly FilePickerFileType _importable =
        new("KeePass database or export") { Patterns = ["*.kdbx", "*.json", "*.1pux", "*.csv"] };

    private static readonly FilePickerFileType _keyfile =
        new("KeePass key file") { Patterns = ["*.keyx"] };

    public async Task<string?> PickExistingAsync()
    {
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.OpenAKeypasteVault,
            AllowMultiple = false,
            FileTypeFilter = [_vault],
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickImportAsync()
    {
        // All files as well: an export keeps whatever name its app or the person gave it.
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ImportFromAnotherApp,
            AllowMultiple = false,
            FileTypeFilter = [_importable, FilePickerFileTypes.All],
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewAsync()
    {
        // The overwrite prompt is off because keypaste refuses an occupied path itself, and a
        // platform dialog that asked "replace it?" would be offering something keypaste will not do.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.CreateAKeypasteVault,
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
            Title = Strings.ExportAnEncryptedCopyOfThisVault,
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
            Title = Strings.ChooseAKeyFile,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewKeyfileAsync(string suggestedName)
    {
        // No overwrite prompt, for PickNewAsync's reason: a new keyfile never replaces a file.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveANewKeyFile,
            SuggestedFileName = suggestedName,
            DefaultExtension = "keyx",
            ShowOverwritePrompt = false,
            FileTypeChoices = [_keyfile],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickDotEnvAsync()
    {
        // No type filter: .env, .env.local and env.production are all the same kind of file.
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ImportADotEnvFile,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickReferenceFileAsync(string suggestedName, string? directory)
    {
        var start = directory is null ? null : await top.StorageProvider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ExportEnvKeypaste,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = start,
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickAttachmentAsync()
    {
        // No type filter: an attachment can be any file.
        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ChooseAFileToAttach,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickAttachmentDestinationAsync(string suggestedName)
    {
        // No overwrite prompt, for PickNewAsync's reason: a saved attachment never replaces a file, and where a
        // platform's dialog asks anyway, the screen explains why nothing was replaced.
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveACopyOfTheAttachment,
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = false,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.ChooseTheProjectsDirectory,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
