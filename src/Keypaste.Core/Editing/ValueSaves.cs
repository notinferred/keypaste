using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Editing;

/// <summary>What saving a value at the place an agent named would do, decided without the value (F.79).</summary>
public sealed record SaveCheck
{
    /// <summary>Why it cannot be saved there, or null.</summary>
    public EditRefusal? Refusal { get; init; }

    /// <summary>The item that holds the value once saved: the one named, or the project's home or tagged item.</summary>
    public EntryName? Holder { get; init; }

    /// <summary>The holder's tags once saved, which decide what the agent can see of it.</summary>
    public IReadOnlyList<string> HolderTags { get; init; } = [];

    /// <summary>Whether saving creates the holder.</summary>
    public bool Creates { get; init; }

    /// <summary>Whether saving replaces a value the place already holds.</summary>
    public bool Replaces { get; init; }

    /// <summary>Whether two checks describe the same change, so one a person saw still holds.</summary>
    internal bool Matches(SaveCheck other) =>
        Refusal is null && other.Refusal is null && Creates == other.Creates && Replaces == other.Replaces
        && Holder is { } holder && other.Holder is { } held && holder.Equals(held);
}

/// <summary>
/// Saves a value a person entered at the place an agent named, through the edits a terminal makes, so the two decide alike:
/// an item's password or custom field, creating the item when there is none, or a project's variable.
/// </summary>
/// <remarks>Nothing here saves the vault or reads a value it holds; the caller saves under its claim.</remarks>
public static class ValueSaves
{
    // Stands in for the value a person has not entered yet, where checking a project's variable needs one.
    private const string _standIn = "keypaste-value-to-save";

    /// <summary>Decides what saving at the request's place would do, changing nothing.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="request">A request <see cref="SaveRequestRules.Check"/> accepts.</param>
    /// <param name="scene">This computer's settings and the time.</param>
    /// <returns>A refusal, or what saving would create or replace.</returns>
    /// <exception cref="VaultException">More than one item answers to the path.</exception>
    public static SaveCheck Check(Vault vault, SaveRequest request, EditScene scene)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(request);

        foreach (var edit in Edits(request, _standIn, creates: false).Concat(Edits(request, _standIn, creates: true)))
        {
            if (!edit.TryValidate(out var invalid))
            {
                return new SaveCheck { Refusal = new EditRefusal(EditRefusalKind.Invalid, invalid) };
            }
        }

        if (request.ForProject)
        {
            var check = TerminalEdits.Check(vault, Edits(request, _standIn, creates: false)[0], scene);

            if (check.Refusal is { } refusal)
            {
                return new SaveCheck { Refusal = refusal };
            }

            if (check.Preview is not { Plan: { } plan, Entry: { } home })
            {
                return new SaveCheck { Holder = check.Unchanged?.Entry, Replaces = true, HolderTags = check.Unchanged?.Entry is { } held ? vault.Tags(held) ?? [] : [] };
            }

            return new SaveCheck
            {
                Holder = home,
                Creates = plan.CreatesHome,
                Replaces = plan.Keys[0].Change != EnvWriteChange.New,
                HolderTags = plan.CreatesHome ? [plan.Tag] : vault.Tags(home) ?? [],
            };
        }

        var name = EntryName.Parse(request.Item);

        if (vault.Find(name) is null)
        {
            return new SaveCheck { Holder = name, Creates = true };
        }

        var replaces = string.Equals(request.Field, SaveRequestRules.Password, StringComparison.Ordinal)
            || vault.Fields(name)?.Any(field => string.Equals(field.Name, request.Field, StringComparison.Ordinal)) == true;

        return new SaveCheck { Holder = name, Replaces = replaces, HolderTags = vault.Tags(name) ?? [] };
    }

    /// <summary>Saves the value in the vault, checking first that the place is still as a person saw it. Does not save the vault.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="request">The request.</param>
    /// <param name="shown">The check the person saw.</param>
    /// <param name="value">The value the person entered.</param>
    /// <param name="scene">This computer's settings and the time.</param>
    /// <returns>What was changed, or why nothing was.</returns>
    /// <exception cref="VaultException">The vault refused the write, or more than one item answers to the path.</exception>
    public static EditResult Apply(Vault vault, SaveRequest request, SaveCheck shown, string value, EditScene scene)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentException.ThrowIfNullOrEmpty(value);

        var now = Check(vault, request, scene);

        if (now.Refusal is { } refusal)
        {
            return new EditResult { Refusal = refusal };
        }

        if (!now.Matches(shown))
        {
            return new EditResult { Refusal = new EditRefusal(EditRefusalKind.Conflict, "the vault changed while you were asked; nothing was saved") };
        }

        EditResult result = new() { Refusal = new EditRefusal(EditRefusalKind.Invalid, "nothing to save") };

        foreach (var edit in Edits(request, value, now.Creates))
        {
            result = TerminalEdits.Apply(vault, edit, scene, shown: null);

            if (result.Refusal is not null)
            {
                return result;
            }
        }

        return result;
    }

    /// <summary>The edits that save a value at the request's place, in order.</summary>
    private static List<TerminalEdit> Edits(SaveRequest request, string value, bool creates)
    {
        if (request.ForProject)
        {
            return [new EnvSetKey(request.Project, request.Environment, request.Key, null, NewSecret.Of(value))];
        }

        var name = EntryName.Parse(request.Item);

        if (string.Equals(request.Field, SaveRequestRules.Password, StringComparison.Ordinal))
        {
            return [new SetPassword(name, NewSecret.Of(value))];
        }

        // No protection given, so a new field is protected and one being replaced keeps its own flag.
        SetFields fields = new(name, [new FieldWrite(request.Field, value)]);

        // A new item for a custom field holds that field alone, its password empty.
        return creates ? [new AddItem(name, string.Empty, string.Empty, string.Empty, NewSecret.Of(string.Empty)), fields] : [fields];
    }
}
