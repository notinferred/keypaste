namespace Keypaste.Core;

/// <summary>
/// What happened when an entry was renamed, moved or copied.
/// </summary>
/// <remarks>
/// <para>
/// Renaming and moving share this vocabulary because they are one write. An entry's identity is
/// its group path and its title (DECISIONS.md D-0091); a rename varies one half and a move varies
/// the other, and every way either can be refused is a way the other can be refused too. The three
/// success members say which halves actually changed, so a caller that varied both is not told a
/// half-truth by a result that can only say <see cref="Renamed"/> or <see cref="Moved"/>.
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

    /// <summary>The entry is in its new group, under its new title. One write changed both.</summary>
    RenamedAndMoved = 10,

    /// <summary>A copy of the entry is beside it under a new title; the entry itself is unchanged.</summary>
    Copied = 11,
}

/// <summary>
/// What happened when a group was created, renamed, moved or deleted.
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

    /// <summary>
    /// The name means something keypaste assigns, and is not an ordinary group's to take; or the group is
    /// one keypaste assigns meaning to and is not moved or deleted, or holds the recycle bin and is not deleted.
    /// </summary>
    NameReserved = 4,

    /// <summary>There is no such group to create this one inside or move it into.</summary>
    ParentMissing = 5,

    /// <summary>A sibling already has that name, or the rename or move would collide underneath.</summary>
    DestinationOccupied = 6,

    /// <summary>The group is already called that, or already there.</summary>
    DestinationUnchanged = 7,

    /// <summary>The rename or move would leave two entries answering to one path.</summary>
    DestinationAmbiguous = 8,

    /// <summary>The group is in its new parent, under the name it had, with everything under it.</summary>
    Moved = 9,

    /// <summary>The group is in the recycle bin with everything under it, and each item in it can be restored.</summary>
    Recycled = 10,

    /// <summary>The group held nothing and the vault has no recycle bin, so it is gone for good.</summary>
    DeletedPermanently = 11,

    /// <summary>The vault has no recycle bin and the group holds items or groups, which a delete would destroy for good.</summary>
    NotEmpty = 12,

    /// <summary>The destination is the group itself or a group inside it.</summary>
    DestinationInside = 13,
}
