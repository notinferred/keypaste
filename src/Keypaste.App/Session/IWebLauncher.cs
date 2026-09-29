namespace Keypaste.App.Session;

/// <summary>Opens a web address in the person's default browser.</summary>
/// <remarks>
/// A seam for the reason <see cref="IVaultFilePicker"/> is one: which address reaches the platform is
/// a behaviour the item pane answers for, and no view model names an Avalonia type.
/// </remarks>
internal interface IWebLauncher
{
    /// <summary>Asks the platform to open <paramref name="address"/>.</summary>
    /// <param name="address">An absolute <c>http</c> or <c>https</c> address.</param>
    /// <returns>Whether the platform said it opened it.</returns>
    Task<bool> OpenAsync(Uri address);
}
