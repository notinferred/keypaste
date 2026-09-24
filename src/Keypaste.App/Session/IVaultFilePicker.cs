namespace Keypaste.App.Session;

/// <summary>
/// Where the person says a vault should be opened from, or made.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a direct <c>TopLevel.StorageProvider</c> call, for one reason: a cancelled
/// picker has to be a behaviour the view model answers for. With the picker in the view, cancelling
/// meant the view model was never called at all, and "a cancelled picker writes nothing" could only
/// be asserted by not calling anything — which would pass against code that wrote the vault before
/// ever opening the dialog.
/// </para>
/// <para>
/// It hands back a plain path, so no view model names an Avalonia type and the create flow can be
/// driven where there is no display at all.
/// </para>
/// </remarks>
internal interface IVaultFilePicker
{
    /// <summary>Asks which existing vault to open. Null when the person cancelled.</summary>
    Task<string?> PickExistingAsync();

    /// <summary>Asks where a new vault should go. Null when the person cancelled.</summary>
    Task<string?> PickNewAsync();

    /// <summary>Asks where an encrypted copy of the open vault should go. Null when the person cancelled.</summary>
    /// <param name="suggestedName">
    /// The file name the dialog starts with. A convenience and nothing more: what may not be written
    /// where is <see cref="Keypaste.Core.Vault.ExportTo"/>'s to refuse, whatever the file is called.
    /// </param>
    Task<string?> PickExportDestinationAsync(string suggestedName);

    /// <summary>Asks which existing keyfile to use. Null when the person cancelled.</summary>
    /// <remarks>
    /// Read, never written: keypaste attaches a keyfile somebody already has and makes none (D-0287).
    /// What the file turns out to be is <see cref="Keypaste.Core.VaultKeyfile.Inspect"/>'s to say.
    /// </remarks>
    Task<string?> PickKeyfileAsync();

    /// <summary>Asks which <c>.env</c> file to import. Null when the person cancelled.</summary>
    /// <remarks>Read, never written or deleted: the app leaves the file where it was.</remarks>
    Task<string?> PickDotEnvAsync();

    /// <summary>Asks which directory a project runs in. Null when the person cancelled.</summary>
    Task<string?> PickFolderAsync();
}
