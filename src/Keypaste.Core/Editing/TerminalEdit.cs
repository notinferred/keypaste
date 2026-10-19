namespace Keypaste.Core.Editing;

/// <summary>Which change a terminal asks for, one per CLI verb that edits items.</summary>
public enum EditAction
{
    /// <summary><c>keypaste add</c>.</summary>
    Add = 0,

    /// <summary><c>keypaste set</c>.</summary>
    Set = 1,

    /// <summary><c>keypaste set --field</c>.</summary>
    SetFields = 2,

    /// <summary><c>keypaste rm</c>.</summary>
    Remove = 3,

    /// <summary><c>keypaste rotate</c>.</summary>
    Rotate = 4,

    /// <summary><c>keypaste field rm</c>.</summary>
    RemoveField = 5,

    /// <summary><c>keypaste env set</c>.</summary>
    EnvSet = 6,

    /// <summary><c>keypaste env rm</c>.</summary>
    EnvRemove = 7,

    /// <summary><c>keypaste env pull</c>.</summary>
    EnvPull = 8,

    /// <summary><c>keypaste env tag</c>.</summary>
    EnvTag = 9,

    /// <summary><c>keypaste env untag</c>.</summary>
    EnvUntag = 10,
}

/// <summary>A new secret: the text a person typed or piped, or a recipe the vault's holder generates from.</summary>
/// <remarks>Its text never reaches a string a person or a log reads: <see cref="ToString"/> names only which it is.</remarks>
public sealed record NewSecret
{
    private NewSecret(string? text, SecretRecipe? recipe)
    {
        Text = text;
        Recipe = recipe;
    }

    /// <summary>The value, or null while a recipe has not been generated from.</summary>
    public string? Text { get; }

    /// <summary>What it is generated from, or null when a person gave it.</summary>
    public SecretRecipe? Recipe { get; }

    /// <summary>A value a person gave.</summary>
    /// <param name="text">The value.</param>
    /// <returns>The secret.</returns>
    public static NewSecret Of(string text) => new(text ?? throw new ArgumentNullException(nameof(text)), null);

    /// <summary>A value to generate where the edit is made, so it never crosses to the vault's holder.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <returns>The secret.</returns>
    public static NewSecret Generated(SecretRecipe recipe) => new(null, recipe);

    /// <summary>This secret with its value generated, when it is a recipe not yet generated from.</summary>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    internal NewSecret Generate()
    {
        if (Text is not null)
        {
            return this;
        }

        using var generated = new SecretBuffer();
        PasswordGenerator.Append(Recipe!.Value, generated);
        return new NewSecret(new string(generated.Value), Recipe);
    }

    /// <summary>What a person is told it is, never the value.</summary>
    /// <param name="noun">What a character secret is called here.</param>
    /// <returns>Such as <c>a generated 20-character password</c> or <c>the password the terminal sent</c>.</returns>
    public string Describe(string noun) => Recipe is { } recipe ? $"a generated {recipe.Describe(noun)}" : $"the {noun} the terminal sent";

    /// <inheritdoc/>
    public override string ToString() => Describe("value");
}

/// <summary>
/// One change a terminal makes to a vault, as the CLI runs it on a vault it opened and as the vault's
/// holder runs it when the CLI hands it over (D-0464).
/// </summary>
/// <remarks>
/// A value is optional so the same edit can be checked before anything is typed; it is applied only
/// with every value present. <see cref="ToString"/> is sealed, so no record below prints a value.
/// </remarks>
public abstract record TerminalEdit
{
    /// <summary>Which change this is.</summary>
    public abstract EditAction Action { get; }

    /// <summary>The verb, as a person types it after <c>keypaste</c>.</summary>
    public string Verb => VerbOf(Action);

    /// <summary>The verb for one action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>Such as <c>set</c> or <c>env pull</c>.</returns>
    public static string VerbOf(EditAction action) => action switch
    {
        EditAction.Add => "add",
        EditAction.Set or EditAction.SetFields => "set",
        EditAction.Remove => "rm",
        EditAction.Rotate => "rotate",
        EditAction.RemoveField => "field rm",
        EditAction.EnvSet => "env set",
        EditAction.EnvRemove => "env rm",
        EditAction.EnvPull => "env pull",
        EditAction.EnvTag => "env tag",
        EditAction.EnvUntag => "env untag",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>Whether every value the edit writes is present, which applying it needs.</summary>
    public abstract bool IsComplete { get; }

    /// <summary>The rules that need no vault, as the CLI applies them before it opens one.</summary>
    /// <param name="error">Why the edit is refused, or empty.</param>
    /// <returns>Whether the edit may be checked against a vault.</returns>
    public abstract bool TryValidate(out string error);

    /// <inheritdoc/>
    public sealed override string ToString() => $"keypaste {Verb}";

    private protected static bool TryName(EntryName name, out string error)
    {
        if (name.Title.Length == 0)
        {
            error = "the item name cannot be empty";
            return false;
        }

        if (!string.Equals(WrittenGroup.Normalize(name.GroupPath), name.GroupPath, StringComparison.Ordinal))
        {
            error = "the item's group is not spelled as the vault stores it";
            return false;
        }

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            error = $"{EntryNameSanitizer.SanitizePath(PathOf(name)).Text} is keypaste's own group; it cannot be written here";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private protected static bool TrySecret(NewSecret? secret, out string error)
    {
        if (secret?.Recipe is { } recipe && !recipe.TryValidate(out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private protected static bool TryProfile(string environment, out string error) =>
        EnvProfileNames.IsValid(environment, out error);

    /// <summary>The path a person types for a name.</summary>
    internal static string PathOf(EntryName name) => name.GroupPath.Length == 0 ? name.Title : name.GroupPath + "/" + name.Title;
}

/// <summary><c>keypaste add</c>: a new item, its password typed, piped or generated.</summary>
/// <param name="Name">Where it goes.</param>
/// <param name="Username">Its username, or empty.</param>
/// <param name="Url">Its URL, or empty.</param>
/// <param name="Notes">Its notes, or empty.</param>
/// <param name="Password">Its password, or null before it is read.</param>
public sealed record AddItem(EntryName Name, string Username, string Url, string Notes, NewSecret? Password) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.Add;

    /// <inheritdoc/>
    public override bool IsComplete => Password is not null;

    /// <inheritdoc/>
    public override bool TryValidate(out string error)
    {
        if (!TryName(Name, out error))
        {
            return false;
        }

        if (!Vault.IsCreatable(Name, out error))
        {
            return false;
        }

        return TrySecret(Password, out error);
    }
}

/// <summary><c>keypaste set</c>: creates an item, or replaces its password.</summary>
/// <param name="Name">The item.</param>
/// <param name="Password">The new password, or null before it is read.</param>
public sealed record SetPassword(EntryName Name, NewSecret? Password) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.Set;

    /// <inheritdoc/>
    public override bool IsComplete => Password is not null;

    /// <inheritdoc/>
    public override bool TryValidate(out string error) => TryName(Name, out error) && TrySecret(Password, out error);
}

/// <summary><c>keypaste set --field</c>: custom fields of an item that exists, in one revision.</summary>
/// <param name="Name">The item.</param>
/// <param name="Fields">Each field once, with its value once read; a new one is plain when <see cref="FieldWrite.Protect"/> is false.</param>
public sealed record SetFields(EntryName Name, IReadOnlyList<FieldWrite> Fields) : TerminalEdit
{
    /// <summary>The most fields one edit sets.</summary>
    public const int MaximumFields = 64;

    /// <inheritdoc/>
    public override EditAction Action => EditAction.SetFields;

    /// <inheritdoc/>
    public override bool IsComplete => Fields.All(field => field.Value is not null);

    /// <summary>Whether new fields are written plain.</summary>
    public bool Plain => Fields.Count > 0 && Fields[0].Protect == false;

    /// <inheritdoc/>
    public override bool TryValidate(out string error)
    {
        if (!TryName(Name, out error))
        {
            return false;
        }

        if (Fields.Count is 0 or > MaximumFields)
        {
            error = $"give between 1 and {MaximumFields} fields";
            return false;
        }

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (var field in Fields)
        {
            if (!FieldNameRules.IsWritable(field.Name, out error))
            {
                return false;
            }

            if (!named.Add(field.Name))
            {
                error = $"--field '{field.Name}' is given twice";
                return false;
            }

            if (field.Protect is true || field.Protect != Fields[0].Protect)
            {
                error = "a new field is protected unless --plain, which applies to every field given";
                return false;
            }
        }

        return true;
    }
}

/// <summary><c>keypaste rm</c>: an item, to the recycle bin when the vault keeps one.</summary>
/// <param name="Item">The item's path, as typed.</param>
public sealed record RemoveItem(string Item) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.Remove;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error)
    {
        error = Item.Length == 0 ? "the item name cannot be empty" : string.Empty;
        return error.Length == 0;
    }
}

/// <summary><c>keypaste rotate</c>: a new generated password, generated where the edit is applied.</summary>
/// <param name="Name">The item.</param>
/// <param name="Recipe">What to generate.</param>
public sealed record RotatePassword(EntryName Name, SecretRecipe Recipe) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.Rotate;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error) => TryName(Name, out error) && Recipe.TryValidate(out error);
}

/// <summary><c>keypaste field rm</c>: one custom field, whose value stays in history.</summary>
/// <param name="Item">The item's path, as typed.</param>
/// <param name="Field">The field's name.</param>
public sealed record RemoveField(string Item, string Field) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.RemoveField;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error) => FieldNameRules.IsWritable(Field, out error);
}

/// <summary><c>keypaste env set</c>: one variable of a project's environment.</summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Key">The variable's name.</param>
/// <param name="Entry">The path <c>--entry</c> names, or null.</param>
/// <param name="Value">The value, or null before it is read.</param>
public sealed record EnvSetKey(string Project, string Environment, string Key, string? Entry, NewSecret? Value) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.EnvSet;

    /// <inheritdoc/>
    public override bool IsComplete => Value is not null;

    /// <inheritdoc/>
    public override bool TryValidate(out string error)
    {
        if (Key.Length == 0)
        {
            error = "the variable name cannot be empty";
            return false;
        }

        return TryProfile(Environment, out error) && TrySecret(Value, out error);
    }
}

/// <summary><c>keypaste env rm</c>: one variable, whose value stays in its item's history.</summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Key">The variable's name.</param>
/// <param name="Entry">The path <c>--entry</c> names, or null.</param>
public sealed record EnvRemoveKey(string Project, string Environment, string Key, string? Entry) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.EnvRemove;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error) => TryProfile(Environment, out error);
}

/// <summary><c>keypaste env pull</c>: a <c>.env</c> file's variables, all or none.</summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Entry">The path <c>--entry</c> names, or null.</param>
/// <param name="Text">The file, decoded; parsed again wherever the edit is checked.</param>
public sealed record EnvPullFile(string Project, string Environment, string? Entry, string Text) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => EditAction.EnvPull;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error)
    {
        if (!EnvConvention.IsValidProject(Project, out error) || !TryProfile(Environment, out error))
        {
            return false;
        }

        if (!DotEnv.TryParse(Text, out var document))
        {
            error = $"the file has {document.Problems.Count} problem(s); nothing was imported";
            return false;
        }

        return true;
    }
}

/// <summary><c>keypaste env tag</c> and <c>untag</c>: an item into or out of an environment by its own tag.</summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="Item">The item's path, as typed.</param>
/// <param name="Tagging">Whether the tag is added rather than removed.</param>
public sealed record TagItem(string Project, string Environment, string Item, bool Tagging) : TerminalEdit
{
    /// <inheritdoc/>
    public override EditAction Action => Tagging ? EditAction.EnvTag : EditAction.EnvUntag;

    /// <inheritdoc/>
    public override bool IsComplete => true;

    /// <inheritdoc/>
    public override bool TryValidate(out string error) => ProjectTag.TryFor(Project, Environment, out _, out error);
}
