namespace Keypaste.Core.Agents;

/// <summary>What kind of value an agent asks for, which decides how its request is answered.</summary>
public enum FieldCategory
{
    /// <summary>An item's username, URL or unprotected custom field, which agents see unless Settings hides item details.</summary>
    Detail = 0,

    /// <summary>An item's password.</summary>
    Password = 1,

    /// <summary>A protected custom field of an item in no project.</summary>
    ProtectedField = 2,

    /// <summary>A custom field named like an environment variable on an item a project tag puts in a project.</summary>
    ProjectVariable = 3,

    /// <summary>An item's current TOTP code.</summary>
    Totp = 4,

    /// <summary>An item's notes.</summary>
    Notes = 5,

    /// <summary>An item's attachments.</summary>
    Attachment = 6,

    /// <summary>A passkey's <c>KPEX_PASSKEY_*</c> field, whose private key signs in wherever it is registered: always Blocked.</summary>
    Passkey = 7,
}

/// <summary>How every agent's requests for one category are answered.</summary>
public enum CategoryRule
{
    /// <summary>A person is asked each time, Deny or Allow once.</summary>
    Ask = 0,

    /// <summary>Released without asking, once the person confirmed that every value in the category will be.</summary>
    Allow = 1,

    /// <summary>Refused without asking.</summary>
    Block = 2,
}

/// <summary>Which category a field an agent names falls in.</summary>
public static class FieldCategories
{
    /// <summary>The prefix of KeePassXC's passkey fields.</summary>
    public const string PasskeyPrefix = "KPEX_PASSKEY_";

    /// <summary>The categories Settings sets a rule for, in the order it lists them.</summary>
    public static IReadOnlyList<FieldCategory> Ruled { get; } =
    [
        FieldCategory.Password,
        FieldCategory.ProtectedField,
        FieldCategory.ProjectVariable,
        FieldCategory.Totp,
        FieldCategory.Notes,
        FieldCategory.Attachment,
    ];

    /// <summary>The category of a field an agent named.</summary>
    /// <param name="field">The field as the request names it: <c>password</c>, <c>username</c>, <c>url</c>, <c>notes</c>, <c>totp</c> or a custom field.</param>
    /// <param name="isProtected">Whether the item's custom field of that name is protected; ignored for a standard field.</param>
    /// <param name="tags">The item's own tags.</param>
    /// <returns>Its category.</returns>
    public static FieldCategory Of(string field, bool isProtected, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(tags);

        return field switch
        {
            _ when IsPasskey(field) => FieldCategory.Passkey,
            "password" => FieldCategory.Password,
            "username" or "url" => FieldCategory.Detail,
            "notes" => FieldCategory.Notes,
            Approval.CredentialFields.Totp => FieldCategory.Totp,
            _ when tags.Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member) => FieldCategory.ProjectVariable,
            _ => isProtected ? FieldCategory.ProtectedField : FieldCategory.Detail,
        };
    }

    /// <summary>Whether a field is one of a passkey's.</summary>
    /// <param name="field">The field's name.</param>
    /// <returns>Whether it starts with <see cref="PasskeyPrefix"/>, in any case.</returns>
    public static bool IsPasskey(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.StartsWith(PasskeyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The category as Settings and a refusal name it.</summary>
    /// <param name="category">The category.</param>
    /// <returns>Its name, lower-case, plural where it reads so.</returns>
    public static string Shown(FieldCategory category) => category switch
    {
        FieldCategory.Detail => "item details",
        FieldCategory.Password => "passwords",
        FieldCategory.ProtectedField => "protected custom fields",
        FieldCategory.ProjectVariable => "project variables",
        FieldCategory.Totp => "TOTP codes",
        FieldCategory.Notes => "notes",
        FieldCategory.Attachment => "attachments",
        _ => "passkeys",
    };

    /// <summary>The key a category's rule is kept under in <c>agents.toml</c>.</summary>
    /// <param name="category">One of <see cref="Ruled"/>.</param>
    /// <returns>Its key.</returns>
    internal static string Key(FieldCategory category) => category switch
    {
        FieldCategory.Password => "password",
        FieldCategory.ProtectedField => "protected",
        FieldCategory.ProjectVariable => "variable",
        FieldCategory.Totp => "totp",
        FieldCategory.Notes => "notes",
        FieldCategory.Attachment => "attachment",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "only a ruled category has a key"),
    };
}
