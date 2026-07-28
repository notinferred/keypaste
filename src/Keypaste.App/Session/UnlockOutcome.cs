namespace Keypaste.App.Session;

/// <summary>What happened when the user tried to open a vault.</summary>
/// <remarks>
/// <para>
/// A closed set rather than an exception, because every one of these is an ordinary thing a person
/// does and none of them is an error the app should present as one. The unlock screen maps each to
/// one calm sentence; ideas.md names "red scary warnings for normal actions" as an anti-pattern and
/// mistyping a password is the most normal action there is.
/// </para>
/// <para>
/// <see cref="NotAKdbx"/> is distinct from <see cref="Failed"/> on purpose. It is answered by
/// <see cref="Core.KdbxHeader.Read"/> before a password is asked for at all, so a person who
/// dropped the wrong file learns that immediately rather than after typing.
/// </para>
/// </remarks>
internal enum UnlockOutcome
{
    /// <summary>The vault is open.</summary>
    Opened = 0,

    /// <summary>The master password was wrong.</summary>
    WrongPassword = 1,

    /// <summary>There is no file at that path.</summary>
    NotFound = 2,

    /// <summary>There is a file, and it is not a KDBX vault.</summary>
    NotAKdbx = 3,

    /// <summary>The file is a vault and something else went wrong reading it.</summary>
    Failed = 4,
}
