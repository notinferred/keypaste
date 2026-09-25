namespace Keypaste.Core;

/// <summary>What rotating an entry came to.</summary>
public enum RotateOutcome
{
    /// <summary>The password was replaced; the old one is a history revision.</summary>
    Rotated = 0,

    /// <summary>No entry answers to that name.</summary>
    NotFound = 1,

    /// <summary>The entry is in a group keypaste keeps for itself.</summary>
    Reserved = 2,
}

/// <summary>
/// Replaces an entry's password with a generated one, keeping the old value as a history revision
/// (D-0014), and says when the current password was set. Never returns or prints a value.
/// </summary>
/// <remarks>
/// It writes a new value into the vault only: a key a provider issued still has to be rotated at the
/// provider and stored with <c>keypaste set</c>. The edit is an ordinary <see cref="Vault.UpdateEntry"/>,
/// so every grant naming the entry ends (D-0318).
/// </remarks>
public static class EntryRotation
{
    /// <summary>Replaces the password. Does not save.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="name">The entry.</param>
    /// <param name="recipe">What to generate.</param>
    /// <returns>Whether it was rotated, and why not.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    public static RotateOutcome Rotate(Vault vault, EntryName name, SecretRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(name);

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            return RotateOutcome.Reserved;
        }

        if (vault.Find(name) is not { } found)
        {
            return RotateOutcome.NotFound;
        }

        using var generated = new SecretBuffer();
        PasswordGenerator.Append(recipe, generated);

        return vault.UpdateEntry(found with { Password = new string(generated.Value) })
            ? RotateOutcome.Rotated
            : RotateOutcome.NotFound;
    }

    /// <summary>When the entry's current password was set.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="name">The entry.</param>
    /// <returns>The time, or null when no entry answers to that name.</returns>
    /// <remarks>
    /// Walks history newest first while each revision holds the same password, compared in memory and
    /// discarded; an edit to another field does not move it. With no change in history it is the
    /// entry's creation time.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static DateTimeOffset? LastRotated(Vault vault, EntryName name)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(name);

        if (vault.ReadTimes(name) is not { } times
            || vault.Find(name) is not { } current
            || vault.ReadHistory(name) is not { } history)
        {
            return null;
        }

        var since = times.Modified;

        foreach (var revision in history)
        {
            if (!string.Equals(revision.Fields.Password, current.Password, StringComparison.Ordinal))
            {
                return since;
            }

            since = new DateTimeOffset(DateTime.SpecifyKind(revision.ModifiedUtc, DateTimeKind.Utc));
        }

        return times.Created;
    }
}
