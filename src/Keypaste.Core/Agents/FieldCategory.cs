namespace Keypaste.Core.Agents;

/// <summary>What kind of value an agent asks for, which decides how its request is answered.</summary>
public enum FieldCategory
{
    /// <summary>An item's URL, which agents always see, as they see its title, group and tags.</summary>
    Url = 8,

    /// <summary>An item's username, which agents see unless Settings hides it.</summary>
    Username = 0,

    /// <summary>An item's password.</summary>
    Password = 1,

    /// <summary>A custom field of an item no project tag puts in a project, protected or not.</summary>
    CustomField = 2,

    /// <summary>A custom field of an item a project tag puts in a project, the person's own flag, whatever its name.</summary>
    ProjectVariable = 3,

    /// <summary>An item's current TOTP code.</summary>
    Totp = 4,

    /// <summary>An item's notes.</summary>
    Notes = 5,

    /// <summary>An item's attachments.</summary>
    Attachment = 6,

    /// <summary>A passkey's <c>KPEX_PASSKEY_*</c> field, whose private key signs in wherever it is registered: always Blocked.</summary>
    Passkey = 7,

    /// <summary>A protected custom field of an item tagged <see cref="FieldCategories.CardTag"/>, such as a card's number or security code.</summary>
    Card = 9,

    /// <summary>A protected custom field of an item tagged <see cref="FieldCategories.IdentityTag"/>, such as a Social Security or passport number.</summary>
    Identity = 10,
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

    /// <summary>The tag that makes an item a card, as 1Password's import and keypaste's Bitwarden import write it; matched in any case.</summary>
    public const string CardTag = "Credit Card";

    /// <summary>The tag that makes an item an identity, as 1Password's import and keypaste's Bitwarden import write it; matched in any case.</summary>
    public const string IdentityTag = "Identity";

    /// <summary>The categories Settings sets a rule for, in the order it lists them.</summary>
    public static IReadOnlyList<FieldCategory> Ruled { get; } =
    [
        FieldCategory.Password,
        FieldCategory.CustomField,
        FieldCategory.ProjectVariable,
        FieldCategory.Card,
        FieldCategory.Identity,
        FieldCategory.Totp,
        FieldCategory.Notes,
        FieldCategory.Attachment,
    ];

    /// <summary>The category of a field an agent named.</summary>
    /// <param name="field">The field as the request names it: <c>password</c>, <c>username</c>, <c>url</c>, <c>notes</c>, <c>totp</c> or a custom field.</param>
    /// <param name="tags">The item's own tags.</param>
    /// <param name="isProtected">Whether the field is a protected custom field, which a card's or an identity's tag makes one of theirs.</param>
    /// <returns>
    /// Its category: the KeePass standard fields by their place, a passkey by KeePassXC's format, and any other custom field
    /// by its item's tags alone: a project tag's variable, then a card's or an identity's protected field, otherwise a custom field.
    /// </returns>
    public static FieldCategory Of(string field, IReadOnlyList<string> tags, bool isProtected = false)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(tags);

        return field switch
        {
            _ when IsPasskey(field) => FieldCategory.Passkey,
            "password" => FieldCategory.Password,
            "username" => FieldCategory.Username,
            "url" => FieldCategory.Url,
            "notes" => FieldCategory.Notes,
            Approval.CredentialFields.Totp => FieldCategory.Totp,
            Approval.CredentialFields.Attachment => FieldCategory.Attachment,
            _ when tags.Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member) => FieldCategory.ProjectVariable,
            _ when isProtected && Tagged(tags, CardTag) => FieldCategory.Card,
            _ when isProtected && Tagged(tags, IdentityTag) => FieldCategory.Identity,
            _ => FieldCategory.CustomField,
        };
    }

    private static bool Tagged(IReadOnlyList<string> tags, string tag) => tags.Any(held => string.Equals(held, tag, StringComparison.OrdinalIgnoreCase));

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
        FieldCategory.Url => "URLs",
        FieldCategory.Username => "usernames",
        FieldCategory.Password => "passwords",
        FieldCategory.CustomField => "custom fields",
        FieldCategory.ProjectVariable => "project variables",
        FieldCategory.Card => "card values",
        FieldCategory.Identity => "identity values",
        FieldCategory.Totp => "TOTP codes",
        FieldCategory.Notes => "notes",
        FieldCategory.Attachment => "attachments",
        _ => "passkeys",
    };

    /// <summary>One value of the category, as a sentence about each of them names it.</summary>
    /// <param name="category">The category.</param>
    /// <returns>Its name in the singular, lower-case but for TOTP.</returns>
    public static string One(FieldCategory category) => category switch
    {
        FieldCategory.Url => "URL",
        FieldCategory.Username => "username",
        FieldCategory.Password => "password",
        FieldCategory.CustomField => "custom field",
        FieldCategory.ProjectVariable => "project variable",
        FieldCategory.Card => "card value",
        FieldCategory.Identity => "identity value",
        FieldCategory.Totp => "TOTP code",
        FieldCategory.Notes => "notes",
        FieldCategory.Attachment => "attachment",
        _ => "passkey",
    };

    /// <summary>The ruled category a key names, as <c>agents.toml</c> and <c>keypaste clients --set</c> spell it.</summary>
    /// <param name="key">The key: <c>password</c>, <c>custom</c>, <c>variable</c>, <c>totp</c>, <c>notes</c> or <c>attachment</c>.</param>
    /// <param name="category">The category, when the key names one.</param>
    /// <returns>Whether it does.</returns>
    public static bool TryParseKey(string key, out FieldCategory category)
    {
        ArgumentNullException.ThrowIfNull(key);

        category = Ruled.FirstOrDefault(ruled => string.Equals(Key(ruled), key, StringComparison.Ordinal), FieldCategory.Username);
        return category != FieldCategory.Username;
    }

    /// <summary>The key a category's rule is kept under in <c>agents.toml</c>.</summary>
    /// <param name="category">One of <see cref="Ruled"/>.</param>
    /// <returns>Its key.</returns>
    public static string Key(FieldCategory category) => category switch
    {
        FieldCategory.Password => "password",
        FieldCategory.CustomField => "custom",
        FieldCategory.ProjectVariable => "variable",
        FieldCategory.Card => "card",
        FieldCategory.Identity => "identity",
        FieldCategory.Totp => "totp",
        FieldCategory.Notes => "notes",
        FieldCategory.Attachment => "attachment",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "only a ruled category has a key"),
    };
}
