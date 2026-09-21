namespace Keypaste.Core;

/// <summary>
/// What happened when an entry was renamed or moved.
/// </summary>
/// <remarks>
/// <para>
/// Renaming and moving share this vocabulary because they are one write. An entry's identity is
/// its group path and its title (DECISIONS.md D-0091); a rename varies one half and a move varies
/// the other, and every way either can be refused is a way the other can be refused too. The two
/// success members are what keeps a caller honest about which it asked for.
/// </para>
/// <para>
/// A refusal is a value rather than an exception because a person is going to read it: the front
/// end words each one. An ambiguous <em>source</em> is the exception to that and still throws, as
/// it does for every other mutation, because there is no entry to talk about.
/// </para>
/// </remarks>
public enum OrganizeOutcome
{
    /// <summary>No live entry answers to that name. A recycled one is not live.</summary>
    NothingMatched = 0,

    /// <summary>The entry is where it was, under its new title.</summary>
    Renamed = 1,

    /// <summary>The entry is in its new group, under the title it had.</summary>
    Moved = 2,

    /// <summary>The title is one no vault could address. See <see cref="VaultNameRules"/>.</summary>
    NameRefused = 3,

    /// <summary>There is no such group to move into. The recycle bin is not one.</summary>
    DestinationMissing = 4,

    /// <summary>The entry is already called that, or already there.</summary>
    DestinationUnchanged = 5,

    /// <summary>Another entry already answers to the name this would produce.</summary>
    DestinationOccupied = 6,

    /// <summary>The path this would produce would name two entries rather than one.</summary>
    DestinationAmbiguous = 7,

    /// <summary>
    /// The result would sit under <c>env/</c> with a name no environment could carry: an
    /// unexportable variable name, a project name nothing could resolve, or the <c>env</c> group
    /// itself, where a variable is a write to nowhere.
    /// </summary>
    EnvNameRefused = 8,

    /// <summary>
    /// The result would leave one project holding two variables differing only in case — two on
    /// Linux and one on Windows.
    /// </summary>
    EnvNameCollides = 9,
}

/// <summary>
/// What happened when a group was created or renamed.
/// </summary>
/// <remarks>
/// Separate from <see cref="OrganizeOutcome"/> rather than shared with it: a group cannot be
/// refused for an env variable's case collision, and an entry cannot be refused for a reserved name
/// or a missing parent. An enum member no operation can return is a lie the type tells its caller.
/// </remarks>
public enum GroupOutcome
{
    /// <summary>No group answers to that path. The recycle bin is not one of them.</summary>
    NothingMatched = 0,

    /// <summary>The group is there, empty.</summary>
    Created = 1,

    /// <summary>The group is where it was, under its new name, with everything under it.</summary>
    Renamed = 2,

    /// <summary>The name is one no vault could address. See <see cref="VaultNameRules"/>.</summary>
    NameRefused = 3,

    /// <summary>The name means something keypaste assigns, and is not an ordinary group's to take.</summary>
    NameReserved = 4,

    /// <summary>There is no such group to create this one inside.</summary>
    ParentMissing = 5,

    /// <summary>A sibling already has that name, or the rename would collide underneath.</summary>
    DestinationOccupied = 6,

    /// <summary>The group is already called that.</summary>
    DestinationUnchanged = 7,

    /// <summary>The rename would leave two entries answering to one path.</summary>
    DestinationAmbiguous = 8,

    /// <summary>The result would be an env project under a name nothing could resolve.</summary>
    EnvNameRefused = 9,
}
