namespace Keypaste.Core;

/// <summary>
/// A project's profiles, which its entries' tags name (D-0370): which names keypaste resolves, which
/// are protected, and how a set is named in audit lines and grants.
/// </summary>
public static class EnvProfileNames
{
    /// <summary>The profile a command uses when none is named, the one the tag <c>env:&lt;project&gt;</c> names.</summary>
    public const string Default = "dev";

    /// <summary>The longest profile name keypaste creates.</summary>
    public const int MaximumLength = 32;

    /// <summary>Whether a profile name is one keypaste creates and resolves: <c>[a-z0-9][a-z0-9-]{0,31}</c>.</summary>
    public static bool IsValid(string profile, out string error)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Length is 0 or > MaximumLength)
        {
            error = $"a profile name has 1 to {MaximumLength} characters";
            return false;
        }

        for (var i = 0; i < profile.Length; i++)
        {
            var c = profile[i];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || (c == '-' && i > 0)))
            {
                error = $"'{profile}' is not a profile name: use lowercase letters, digits and '-'";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Whether every release of this profile through a session is asked about live, with no grant and no policy.</summary>
    public static bool IsProtected(string profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = profile.ToLowerInvariant();
        return name is "prod" or "production"
            || name.StartsWith("prod-", StringComparison.Ordinal)
            || name.StartsWith("production-", StringComparison.Ordinal);
    }

    /// <summary>How audit lines and grants name one profile's set: <c>env/&lt;project&gt;</c> for <c>dev</c>, else <c>env/&lt;project&gt;/&lt;profile&gt;</c>; it names no group (D-0416).</summary>
    public static string SetName(string project, string profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return string.Equals(profile, Default, StringComparison.Ordinal)
            ? EnvConvention.GroupPath(project)
            : EnvConvention.GroupPath(project) + "/" + profile;
    }

    /// <summary>Whether releasing an entry must be asked about live every time, by any of its own tags (D-0371, D-0416).</summary>
    /// <param name="tags">The entry's own tags; a group's are never passed.</param>
    /// <returns><see langword="true"/> when a tag names a protected environment (<see cref="ProjectTag.Protects"/>).</returns>
    public static bool RequiresLiveApproval(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return tags.Any(tag => ProjectTag.Read(tag).Protects);
    }
}
