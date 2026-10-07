using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>What exporting a profile as a reference file did.</summary>
public enum EnvReferenceExportOutcome
{
    /// <summary>The file was made, and written when a path was given.</summary>
    Written = 0,

    /// <summary>The vault has no such project.</summary>
    NoProject = 1,

    /// <summary>The project has no such profile.</summary>
    NoProfile = 2,

    /// <summary>The profile cannot be written as references; <see cref="EnvReferenceExport.Problem"/> says why.</summary>
    Refused = 3,

    /// <summary>The path is a vault; <see cref="EnvReferenceExport.Overwrite"/> says which.</summary>
    OverVault = 4,

    /// <summary>The file could not be written; <see cref="EnvReferenceExport.Problem"/> is the system's reason.</summary>
    Unwritable = 5,
}

/// <summary>What exporting a profile as a reference file did.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Count">How many references the file holds.</param>
/// <param name="Text">The file, when it was made; otherwise empty.</param>
/// <param name="Problem">Why it was refused or not written; otherwise empty.</param>
/// <param name="Overwrite">The vault the path is, on <see cref="EnvReferenceExportOutcome.OverVault"/>.</param>
public sealed record EnvReferenceExport(EnvReferenceExportOutcome Outcome, int Count, string Text, string Problem, VaultOverwrite Overwrite)
{
    /// <summary>
    /// Writes a <c>kp://</c> reference for every key of a profile, and never a value: refused when a key is on
    /// two entries or the names collide, and never written over a vault (<see cref="VaultOverwriteRule"/>).
    /// </summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="targetPath">The full path to write, or null to only make the text.</param>
    /// <param name="replace">Whether a file already at the path is replaced.</param>
    /// <returns>What happened, with the text when it was made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vault"/>, <paramref name="project"/> or <paramref name="profile"/> is null.</exception>
    public static EnvReferenceExport Run(Vault vault, string project, string profile, string? targetPath, bool replace)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);

        var listing = EnvResolution.List(vault, project, profile);

        switch (listing.Outcome)
        {
            case EnvOutcome.NoProject:
                return Failed(EnvReferenceExportOutcome.NoProject, string.Empty);

            case EnvOutcome.NoProfile:
                return Failed(EnvReferenceExportOutcome.NoProfile, string.Empty);
        }

        if (RepeatedKey(listing) is { } repeated)
        {
            return Failed(EnvReferenceExportOutcome.Refused, repeated);
        }

        var variables = listing.Variables;

        if (!EnvNameRules.TryCheck(variables, out var names))
        {
            return Failed(EnvReferenceExportOutcome.Refused, $"'{project}/{profile}' {names}");
        }

        var text = EnvReferenceFile.Format(
            project,
            profile,
            [.. variables.Select(variable => variable.Key)],
            targetPath is null ? EnvReferenceFile.FileName : Path.GetFileName(targetPath));

        if (targetPath is not null)
        {
            if (VaultOverwriteRule.Check(vault.Path, targetPath) is not VaultOverwrite.None and var overwrite)
            {
                return new(EnvReferenceExportOutcome.OverVault, 0, string.Empty, string.Empty, overwrite);
            }

            if (!DotEnvFile.TryWrite(targetPath, Encoding.UTF8.GetBytes(text), replace, out var error))
            {
                return Failed(EnvReferenceExportOutcome.Unwritable, error);
            }
        }

        return new(EnvReferenceExportOutcome.Written, variables.Count, text, string.Empty, VaultOverwrite.None);
    }

    /// <summary>A key two entries of the set hold, named with those entries, or null when every key has one.</summary>
    /// <param name="listing">The set.</param>
    /// <returns>The sentence that refuses it, or null.</returns>
    public static string? RepeatedKey(EnvListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        if (listing.Sources.GroupBy(source => source.Key, StringComparer.Ordinal).FirstOrDefault(key => key.Count() > 1) is not { } repeated)
        {
            return null;
        }

        var entries = string.Join(", ", repeated.Select(source => ApprovalPrompt.Shown(source.Entry)));
        return $"{EntryNameSanitizer.Sanitize(repeated.Key).Text} is on more than one entry ({entries})";
    }

    private static EnvReferenceExport Failed(EnvReferenceExportOutcome outcome, string problem) =>
        new(outcome, 0, string.Empty, problem, VaultOverwrite.None);
}
