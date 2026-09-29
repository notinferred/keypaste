using Avalonia.Controls;
using Keypaste.App.Session;

namespace Keypaste.App.Views;

/// <summary>The real launcher, the window's own: the default browser on each platform.</summary>
internal sealed class PlatformWebLauncher(TopLevel top) : IWebLauncher
{
    public Task<bool> OpenAsync(Uri address) => top.Launcher.LaunchUriAsync(address);
}
