namespace Keypaste.Core;

/// <summary>One custom field of an entry, named and never valued.</summary>
/// <param name="Name">The field's name, as the file holds it.</param>
/// <param name="IsProtected">Whether KeePass protects it in memory and in the file's inner stream.</param>
/// <param name="IsReadOnly">
/// Whether keypaste leaves it to KeePassXC: its name is one <see cref="FieldNameRules.IsWritable"/>
/// refuses, such as <c>otp</c>.
/// </param>
public sealed record EntryField(string Name, bool IsProtected, bool IsReadOnly);

/// <summary>One change to one custom field, for <see cref="Vault.SetFields"/>.</summary>
/// <param name="Name">The field's name.</param>
/// <param name="Value">The new value, or null to keep the value the field already has.</param>
/// <param name="Protect">
/// Whether to protect it. Null keeps an existing field's flag and protects a new field.
/// </param>
public sealed record FieldWrite(string Name, string? Value, bool? Protect = null)
{
    /// <summary>The field's name only, so a log line or a debugger never shows the value.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
