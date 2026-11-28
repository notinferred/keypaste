using Keypaste.Core.Ipc;

namespace Keypaste.Core.Browser;

/// <summary>The parts of a login a fill releases.</summary>
[Flags]
internal enum LoginParts
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The username.</summary>
    Username = 1,

    /// <summary>The password.</summary>
    Password = 2,

    /// <summary>The current one-time code, never its seed.</summary>
    Totp = 4,

    /// <summary>Every part.</summary>
    All = Username | Password | Totp,
}

/// <summary>What the next click or fill shortcut in the same tab may still fill of a login without a pick: one fill's item, where it was picked, the site and the parts left, until when.</summary>
/// <param name="Pairing">The pairing it was filled for.</param>
/// <param name="Uuid">The item's UUID.</param>
/// <param name="PickedOn">The origin of the page the person picked it on.</param>
/// <param name="Site">The registrable domain a continuing page must belong to, on the same scheme.</param>
/// <param name="Left">The parts not filled yet.</param>
/// <param name="Until">When it lapses.</param>
internal sealed record ContinuedFill(string Pairing, string Uuid, WebOrigin PickedOn, string Site, LoginParts Left, DateTimeOffset Until)
{
    /// <summary>How long a fill may be continued after it, or after the last part that continued it.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>The parts a request names.</summary>
    internal static LoginParts PartsOf(BrowserFill fill) => fill switch
    {
        BrowserFill.Username => LoginParts.Username,
        BrowserFill.Password => LoginParts.Password,
        BrowserFill.Totp => LoginParts.Totp,
        _ => LoginParts.Username | LoginParts.Password,
    };

    /// <summary>The parts in keypaste's words, as <c>username and password</c>.</summary>
    internal static string Described(LoginParts parts)
    {
        List<string> named = [];

        if (parts.HasFlag(LoginParts.Username))
        {
            named.Add("username");
        }

        if (parts.HasFlag(LoginParts.Password))
        {
            named.Add("password");
        }

        if (parts.HasFlag(LoginParts.Totp))
        {
            named.Add("one-time code");
        }

        return string.Join(" and ", named);
    }
}
