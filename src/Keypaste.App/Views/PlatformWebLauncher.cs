using Avalonia.Controls;
using Keypaste.App.Session;

namespace Keypaste.App.Views;

/// <summary>The real launcher, the window's own: the default browser on each platform.</summary>
internal sealed class PlatformWebLauncher(TopLevel top) : IWebLauncher
{
    private readonly OpenerProcess? _opener = OpenerProcess.ForCurrentPlatform();

    public Task<bool> OpenAsync(Uri address) => _opener?.OpenAsync(address) ?? top.Launcher.LaunchUriAsync(address);
}
