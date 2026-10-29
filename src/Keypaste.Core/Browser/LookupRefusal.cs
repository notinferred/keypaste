namespace Keypaste.Core.Browser;

/// <summary>Why a request for logins gets none, whatever the vault holds (D-0617).</summary>
public enum LookupRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>
    /// Not an absolute <c>http</c> or <c>https</c> URL keypaste can name, such as <c>file://</c>, KeePassXC's
    /// <c>keepassxc://by-uuid</c> and <c>keepassxc://by-path</c>, or a host or port no browser writes.
    /// </summary>
    NotAWebPage,

    /// <summary>A user named before the host, such as <c>https://github.com@evil.io</c>.</summary>
    UserInfo,

    /// <summary>A host that is not ASCII, which a browser writes as A-labels.</summary>
    NotAscii,

    /// <summary>A public suffix, such as <c>github.io</c>, which has no registrable domain.</summary>
    PublicSuffix,

    /// <summary>HTTP authentication, which browser fill does not answer.</summary>
    HttpAuthentication,
}
