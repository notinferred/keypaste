using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Keypaste.Core.Linking;

namespace Keypaste.App.Session;

/// <summary>The browsers' hosts Settings turns on and off and each start keeps current, or why there are none.</summary>
/// <param name="Hosts">The hosts, naming the <c>keypaste</c> a terminal finds; null when there is none to name.</param>
/// <param name="Unavailable">Why there are none; empty when there are.</param>
internal sealed record BrowserHostSetup(BrowserHosts? Hosts, string Unavailable)
{
    /// <summary>The hosts of the user this app runs as, naming the <c>keypaste</c> beside it, or on Linux its link.</summary>
    internal static BrowserHostSetup ForThisProcess()
    {
        var linuxLink = OperatingSystem.IsLinux() ? CliLinks.ForThisProcess() : null;

        if (!HostProgram.TryForTheApp(linuxLink, AppContext.BaseDirectory, McpAppImage.ForThisProcess(), Environment.GetEnvironmentVariable("PATH"), out var program, out var error))
        {
            return new BrowserHostSetup(null, error);
        }

        return new BrowserHostSetup(new BrowserHosts(HostPlaces.ForThisUser(), OperatingSystem.IsWindows() ? new RegistryHostKeys() : null, program), string.Empty);
    }
}
