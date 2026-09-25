using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Sharing;

/// <summary>Where shares are uploaded, and so which host serves the page that reads a link's key.</summary>
/// <remarks>
/// The environment can name only a loopback development server. It is where an agent-influenced
/// shell (direnv, a repository's <c>.envrc</c>) can point, and a hostile host would serve a viewer
/// that sends the key elsewhere. Another host takes a deliberate <c>--endpoint</c>, and is named on
/// every link made there.
/// </remarks>
public static class ShareEndpoint
{
    /// <summary>The variable naming a local development server.</summary>
    public const string EnvironmentVariable = "KEYPASTE_SHARE_URL";

    /// <summary>keypaste.com.</summary>
    public static readonly Uri Default = new("https://keypaste.com");

    /// <summary>Picks the endpoint: the option, else the environment, else <see cref="Default"/>.</summary>
    /// <param name="fromOption"><c>--endpoint</c>: any <c>https</c> origin.</param>
    /// <param name="fromEnvironment"><see cref="EnvironmentVariable"/>: a loopback origin only.</param>
    /// <param name="endpoint">The origin, on success.</param>
    /// <param name="error">Why neither was usable, or empty.</param>
    public static bool TryResolve(string? fromOption, string? fromEnvironment, [NotNullWhen(true)] out Uri? endpoint, out string error)
    {
        if (fromOption is not null)
        {
            if (TryOrigin(fromOption, out endpoint) && endpoint.Scheme == Uri.UriSchemeHttps)
            {
                error = string.Empty;
                return true;
            }

            endpoint = null;
            error = "--endpoint must be an https origin with no path, such as https://share.example.org";
            return false;
        }

        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            if (TryOrigin(fromEnvironment, out endpoint) && IsLoopback(endpoint))
            {
                error = string.Empty;
                return true;
            }

            endpoint = null;
            error = $"{EnvironmentVariable} may only name a local development server; pass --endpoint to use another host";
            return false;
        }

        endpoint = Default;
        error = string.Empty;
        return true;
    }

    /// <summary>Reads the endpoint a share record names, accepting only what could have made one.</summary>
    /// <remarks>The record is vault data anyone who can edit the vault could have written, so its host gets no more trust than a command line would have given it.</remarks>
    public static bool TryParseRecorded(string recorded, [NotNullWhen(true)] out Uri? endpoint)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        if (TryOrigin(recorded, out endpoint) && (endpoint.Scheme == Uri.UriSchemeHttps || IsLoopback(endpoint)))
        {
            return true;
        }

        endpoint = null;
        return false;
    }

    /// <summary>Whether this is keypaste.com.</summary>
    public static bool IsDefault(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return string.Equals(endpoint.GetLeftPart(UriPartial.Authority), Default.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryOrigin(string text, [NotNullWhen(true)] out Uri? origin)
    {
        origin = null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || text.Contains('?', StringComparison.Ordinal)
            || text.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
        return true;
    }

    private static bool IsLoopback(Uri origin) => origin.Host is "localhost" or "127.0.0.1" or "[::1]";
}
