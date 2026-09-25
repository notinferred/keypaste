namespace Keypaste.Core.Tokens;

/// <summary>What a vault records about one scoped token: never the token, and never its verifier.</summary>
/// <param name="Id">The token's id.</param>
/// <param name="Name">The name a person gave it.</param>
/// <param name="Scopes">What it may read.</param>
/// <param name="Created">When it was minted.</param>
/// <param name="Expires">When it stops working.</param>
/// <param name="AllowProd">Whether a scope naming a protected profile may ask the person live.</param>
public sealed record TokenInfo(
    string Id,
    string Name,
    IReadOnlyList<TokenScope> Scopes,
    DateTimeOffset Created,
    DateTimeOffset Expires,
    bool AllowProd)
{
    /// <summary>How the token is shown: its id and never its secret.</summary>
    public string Prefix => TokenSecret.Display(Id);

    /// <summary>What every token can do with what it reads.</summary>
    public const string Mode = "inject-only";

    /// <summary>Whether the token has stopped working.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns><see langword="true"/> from <see cref="Expires"/> on.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= Expires;

    /// <summary>Whether any scope reaches one profile of one project.</summary>
    /// <param name="project">The project.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>Whether the token may read some or all of that set.</returns>
    public bool Covers(string project, string profile) =>
        Scopes.Any(scope => Names(scope, project, profile));

    /// <summary>The keys the token may read from one set.</summary>
    /// <param name="project">The project.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>Null for the whole set; otherwise every key its scopes name, which is empty when none reaches it.</returns>
    public IReadOnlyList<string>? KeysFor(string project, string profile)
    {
        var reaching = Scopes.Where(scope => Names(scope, project, profile)).ToList();

        return reaching.Any(scope => scope.Key is null)
            ? null
            : [.. reaching.Select(scope => scope.Key!).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Every set the token reaches, once each, in the order its scopes name them.</summary>
    public IReadOnlyList<(string Project, string Profile)> Pairs =>
        [.. Scopes.Select(scope => (scope.Project, scope.Profile)).Distinct()];

    private static bool Names(TokenScope scope, string project, string profile) =>
        string.Equals(scope.Project, project, StringComparison.Ordinal)
        && string.Equals(scope.Profile, profile, StringComparison.Ordinal);
}
