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
}
