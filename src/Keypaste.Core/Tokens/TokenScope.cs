using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Tokens;

/// <summary>
/// What one scope lets a token read: <c>read:&lt;project&gt;/&lt;profile&gt;/&lt;key or *&gt;</c>.
/// </summary>
/// <remarks>
/// <c>read</c> is the only verb, because a token only ever feeds <c>keypaste run</c>. A project is
/// one path segment, so <c>acme/api</c> is not a project; the profile is never a wildcard, so a
/// token names every set it can reach.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Profile">The profile.</param>
/// <param name="Key">One variable, or null for the whole set.</param>
public sealed record TokenScope(string Project, string Profile, string? Key)
{
    /// <summary>The most scopes one token carries.</summary>
    public const int MaximumScopes = 16;

    private const string _verb = "read:";

    /// <summary>Parses a comma-separated list of scopes.</summary>
    /// <param name="text">The list, as <c>--scope</c> takes it.</param>
    /// <param name="scopes">The scopes, in the order given and without repeats.</param>
    /// <param name="error">What is wrong, or empty.</param>
    /// <returns>Whether every scope parsed.</returns>
    public static bool TryParseList(string text, [NotNullWhen(true)] out IReadOnlyList<TokenScope>? scopes, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        scopes = null;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length > MaximumScopes)
        {
            error = $"a token carries at most {MaximumScopes} scopes";
            return false;
        }

        List<TokenScope> parsed = [];

        foreach (var part in parts)
        {
            if (!TryParse(part, out var scope, out error))
            {
                return false;
            }

            if (!parsed.Contains(scope))
            {
                parsed.Add(scope);
            }
        }

        scopes = parsed;
        error = string.Empty;
        return true;
    }

    /// <summary>Parses one scope.</summary>
    /// <param name="text">One scope, such as <c>read:acme-api/staging/*</c>.</param>
    /// <param name="scope">The scope, when it parsed.</param>
    /// <param name="error">What is wrong, or empty.</param>
    /// <returns>Whether it parsed.</returns>
    public static bool TryParse(string text, [NotNullWhen(true)] out TokenScope? scope, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        scope = null;

        if (!text.StartsWith(_verb, StringComparison.Ordinal))
        {
            error = $"'{Shown(text)}' is not a scope: write read:<project>/<profile>/<key or *>";
            return false;
        }

        var parts = text[_verb.Length..].Split('/');

        if (parts.Length != 3)
        {
            error = $"'{Shown(text)}' is not a scope: write read:<project>/<profile>/<key or *>";
            return false;
        }

        var (project, profile, key) = (parts[0], parts[1], parts[2]);

        if (!EnvConvention.IsValidProject(project, out error) || project.Contains(',') || project.Contains(':') || project == "*")
        {
            error = $"'{Shown(text)}' does not name a project: {(error.Length > 0 ? error : "a project is one name, without ',', ':' or a wildcard")}";
            return false;
        }

        if (!EnvProfileNames.IsValid(profile, out error))
        {
            error = $"'{Shown(text)}' does not name a profile: {error}";
            return false;
        }

        if (key != "*" && !EnvConvention.IsValidKey(key, out error))
        {
            error = $"'{Shown(text)}' does not name a variable: {error}";
            return false;
        }

        scope = new TokenScope(project, profile, key == "*" ? null : key);
        error = string.Empty;
        return true;
    }

    /// <summary>Whether this scope reaches a profile every release of which must be asked about live.</summary>
    public bool IsProtected => EnvProfileNames.IsProtected(Profile);

    /// <summary>The scope as it is written.</summary>
    /// <returns><c>read:&lt;project&gt;/&lt;profile&gt;/&lt;key or *&gt;</c>.</returns>
    public override string ToString() => $"{_verb}{Project}/{Profile}/{Key ?? "*"}";

    private static string Shown(string text) => EntryNameSanitizer.SanitizeProse(text, 128).Text;
}
