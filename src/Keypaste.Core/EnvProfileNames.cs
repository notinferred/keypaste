namespace Keypaste.Core;

/// <summary>
/// Where a project's profiles live. The default profile is the project group itself,
/// <c>env/&lt;project&gt;</c>, exactly as every vault before profiles held it; any other profile is a
/// subgroup, <c>env/&lt;project&gt;/&lt;profile&gt;</c>.
/// </summary>
public static class EnvProfileNames
{
    /// <summary>The profile a command uses when none is named, stored flat in the project group.</summary>
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

    /// <summary>The group holding one profile of one project.</summary>
    public static string GroupPath(string project, string profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return string.Equals(profile, Default, StringComparison.Ordinal)
            ? EnvConvention.GroupPath(project)
            : EnvConvention.GroupPath(project) + "/" + profile;
    }

    /// <summary>Whether releasing this entry to an agent must be asked about live every time.</summary>
    /// <remarks>Any group segment below the project that names a protected profile counts, in any case, so a subgroup KeePassXC created as <c>Prod</c> is not a way around it.</remarks>
    public static bool RequiresLiveApproval(EntryName entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var prefix = EnvConvention.RootGroup + "/";
        if (!entry.GroupPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = entry.GroupPath[prefix.Length..].Split('/');
        return segments.Skip(1).Any(IsProtected);
    }
}
