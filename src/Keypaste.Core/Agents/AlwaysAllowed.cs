
namespace Keypaste.Core.Agents;

/// <summary>One variable of an always-allowed command: its name, and the item and field its value comes from.</summary>
/// <param name="Name">The variable the command sees.</param>
/// <param name="Item">The item's UUID as hex, as <see cref="Vault.EntryUuid"/> writes it.</param>
/// <param name="Field">The field of that item.</param>
public sealed record AlwaysVariable(string Name, string Item, string Field);

/// <summary>A command a person chose Always allow on: its identity, which is exact and fails closed.</summary>
/// <remarks>
/// The program, every argument in order, the directory, and the set of variable names with where each
/// comes from. Anything different is another command, so it asks. A reason is no part of it.
/// </remarks>
public sealed record AlwaysCommand
{
    /// <summary>Builds a command's identity.</summary>
    /// <param name="program">The resolved program path.</param>
    /// <param name="command">Every argument, in order.</param>
    /// <param name="directory">The directory it starts in, with every link resolved.</param>
    /// <param name="variables">Its variables, in any order.</param>
    public AlwaysCommand(string program, IReadOnlyList<string> command, string directory, IReadOnlyList<AlwaysVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(variables);

        Program = program;
        Command = [.. command];
        Directory = directory;
        Variables =
        [
            .. variables
                .OrderBy(variable => variable.Name, StringComparer.Ordinal)
                .ThenBy(variable => variable.Item, StringComparer.Ordinal)
                .ThenBy(variable => variable.Field, StringComparer.Ordinal),
        ];
    }

    /// <summary>The resolved program path.</summary>
    public string Program { get; }

    /// <summary>Every argument, in order.</summary>
    public IReadOnlyList<string> Command { get; }

    /// <summary>The directory it starts in.</summary>
    public string Directory { get; }

    /// <summary>Its variables, ordered by name so that the set compares whole.</summary>
    public IReadOnlyList<AlwaysVariable> Variables { get; }

    /// <summary>The short name a change removes it by, derived from all of its identity.</summary>
    public string Id => AlwaysAllowed.IdOf(string.Join('\0', [
        "command", Program, .. Command, "\u0001", Directory, "\u0001",
        .. Variables.SelectMany(variable => new[] { variable.Name, variable.Item, variable.Field })]));

    /// <summary>Whether another is exactly this command.</summary>
    /// <param name="other">The other command.</param>
    /// <returns><see langword="true"/> when every part of its identity matches.</returns>
    public bool Equals(AlwaysCommand? other) =>
        other is not null
        && string.Equals(Program, other.Program, StringComparison.Ordinal)
        && string.Equals(Directory, other.Directory, StringComparison.Ordinal)
        && Command.SequenceEqual(other.Command, StringComparer.Ordinal)
        && Variables.SequenceEqual(other.Variables);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(Program), StringComparer.Ordinal.GetHashCode(Directory), Command.Count, Variables.Count);
}

/// <summary>What a person chose Always allow on for one agent: items by UUID and exact commands.</summary>
/// <remarks>
/// A row is a policy the person wrote, and only their confirmation of the widening writes one. It never
/// reaches a protected item, an agent turned off from values or commands, or past what the agent sees:
/// those are checked before a row is read.
/// </remarks>
public sealed record AlwaysAllowed
{
    /// <summary>The most items a record keeps: a longer list is not understood, and no change makes one.</summary>
    public const int MaximumItems = 256;

    /// <summary>The most commands a record keeps: a longer list is not understood, and no change makes one.</summary>
    public const int MaximumCommands = 64;

    /// <summary>Nothing is always allowed.</summary>
    public static AlwaysAllowed None { get; } = new();

    /// <summary>Each item, by its UUID as hex.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Each command, exact.</summary>
    public IReadOnlyList<AlwaysCommand> Commands { get; init; } = [];

    /// <summary>Whether nothing is always allowed.</summary>
    public bool IsEmpty => Items.Count == 0 && Commands.Count == 0;

    /// <summary>The short name a change removes an item by.</summary>
    /// <param name="item">The item's UUID as hex.</param>
    /// <returns>Eight lowercase hex characters.</returns>
    public static string ItemId(string item) => IdOf($"item\0{item}");

    /// <summary>Whether text has the shape of a row's short name.</summary>
    /// <param name="text">What was given.</param>
    /// <returns><see langword="true"/> for exactly eight lowercase hex characters.</returns>
    public static bool IsId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == 8 && text.All(char.IsAsciiHexDigitLower);
    }

    /// <summary>Whether a row has this short name.</summary>
    /// <param name="id">The short name.</param>
    /// <returns><see langword="true"/> when an item or a command has it.</returns>
    public bool Holds(string id) =>
        Items.Any(item => string.Equals(ItemId(item), id, StringComparison.Ordinal))
        || Commands.Any(command => string.Equals(command.Id, id, StringComparison.Ordinal));

    /// <summary>Whether another holds the same rows, in the same order.</summary>
    /// <param name="other">The other list.</param>
    /// <returns><see langword="true"/> when every row matches.</returns>
    public bool Equals(AlwaysAllowed? other) =>
        other is not null
        && Items.SequenceEqual(other.Items, StringComparer.Ordinal)
        && Commands.SequenceEqual(other.Commands);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Items.Count, Commands.Count);

    /// <summary>The list with the row a short name names gone.</summary>
    /// <param name="id">The short name.</param>
    /// <returns>The list, the same when no row has that name.</returns>
    internal AlwaysAllowed Without(string id) => this with
    {
        Items = [.. Items.Where(item => !string.Equals(ItemId(item), id, StringComparison.Ordinal))],
        Commands = [.. Commands.Where(command => !string.Equals(command.Id, id, StringComparison.Ordinal))],
    };

    internal static string IdOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
