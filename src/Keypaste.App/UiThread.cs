using Avalonia;

namespace Keypaste.App;

/// <summary>
/// The running app's UI thread, for code on any thread: <c>Dispatcher.UIThread</c>, read while no
/// app is set up, makes its reader the UI thread, and the running app's own dispatcher cannot.
/// </summary>
/// <remarks>
/// The shipped app is set up before anything here runs and stays set up. Only a headless session
/// has no app, between its dispatches, and what is posted there has no window to reach.
/// </remarks>
internal static class UiThread
{
    /// <summary>Whether the caller is on the running app's UI thread.</summary>
    internal static bool IsCurrent => Application.Current?.CheckAccess() == true;

    /// <summary>Runs <paramref name="action"/> on the running app's UI thread, later.</summary>
    internal static void Post(Action action) => Application.Current?.Dispatcher.Post(action);

    /// <summary>Throws off the running app's UI thread.</summary>
    internal static void VerifyAccess() => Application.Current?.VerifyAccess();
}
