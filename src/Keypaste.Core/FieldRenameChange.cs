using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>
/// What renaming one custom field leaves naming nothing, shown to the person before it is written, as
/// D-0415 shows a tag change: the <c>kp://</c> references that name the field, and the item's own fields
/// whose <c>{S:…}</c> names it. Names only, never a value.
/// </summary>
/// <param name="Entry">The entry.</param>
/// <param name="Field">The field to be renamed.</param>
/// <param name="References">
/// For a field a project releases, <c>kp://&lt;project&gt;/&lt;environment&gt;/&lt;field&gt;</c> for each
/// environment the entry's own tags put it in, then the entry's own <c>kp:///</c> reference to the field;
/// empty for any other field, which no reference can name.
/// </param>
/// <param name="Placeholders">The item's other fields, standard or custom, whose value holds a <c>{S:…}</c> naming it, in ordinal order.</param>
public sealed record FieldRenameChange(EntryName Entry, string Field, IReadOnlyList<string> References, IReadOnlyList<string> Placeholders)
{
    /// <summary>Whether no reference and no placeholder names the field, so the rename needs no question.</summary>
    public bool BreaksNothing => References.Count == 0 && Placeholders.Count == 0;

    /// <summary>What renaming one of an entry's custom fields would leave naming nothing.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="field">The field's name, matched exactly.</param>
    /// <returns>The change, or null when there is no such entry or it has no such field.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static FieldRenameChange? Preview(Vault vault, EntryName entry, string field)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);

        if (vault.Fields(entry) is not { } fields
            || !fields.Any(held => string.Equals(held.Name, field, StringComparison.Ordinal))
            || vault.FieldsNaming(entry, field) is not { } placeholders)
        {
            return null;
        }

        List<string> references = [];

        if (CredentialFields.IsCustom(field))
        {
            references.AddRange((vault.Tags(entry) ?? [])
                .Select(ProjectTag.Read)
                .Where(tag => tag.Kind == ProjectTagKind.Member)
                .Select(tag => KpReferences.For(tag.Project!, tag.Environment!, field))
                .Distinct(StringComparer.Ordinal));
            references.Add(KpReferences.For(entry, field));
        }

        return new FieldRenameChange(entry, field, references, placeholders);
    }
}
