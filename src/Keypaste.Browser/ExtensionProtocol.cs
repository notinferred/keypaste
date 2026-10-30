using System.Collections.Frozen;

namespace Keypaste.Browser;

/// <summary>What KeePassXC-Browser's protocol names, as KeePassXC 2.8.0's <c>src/browser</c> answers it.</summary>
internal static class ExtensionProtocol
{
    /// <summary>
    /// The KeePassXC version the relay reports: the lowest KeePassXC-Browser accepts, so it offers neither its password
    /// generator nor passkeys, which keypaste refuses (D-0625).
    /// </summary>
    internal const string ReportedVersion = "2.6.0";

    internal const string ChangePublicKeys = "change-public-keys";
    internal const string GetDatabaseHash = "get-databasehash";
    internal const string Associate = "associate";
    internal const string TestAssociate = "test-associate";
    internal const string GetLogins = "get-logins";
    internal const string LockDatabase = "lock-database";
    internal const string DatabaseLocked = "database-locked";
    internal const string DatabaseUnlocked = "database-unlocked";

    /// <summary>KeePassXC's word for true, which the protocol writes as a string.</summary>
    internal const string True = "true";

    /// <summary>
    /// Each action D-0548 refuses, answered by the relay itself and never sent to the vault's owner, with the code KeePassXC
    /// answers its own refusal of that action with (D-0626).
    /// </summary>
    internal static FrozenDictionary<string, ExtensionError> Refused { get; } = new Dictionary<string, ExtensionError>(StringComparer.Ordinal)
    {
        ["get-totp"] = ExtensionError.ActionCancelledOrDenied,
        ["generate-password"] = ExtensionError.ActionCancelledOrDenied,
        ["set-login"] = ExtensionError.ActionCancelledOrDenied,
        ["delete-entry"] = ExtensionError.ActionCancelledOrDenied,
        ["request-autotype"] = ExtensionError.ActionCancelledOrDenied,
        ["create-new-group"] = ExtensionError.CannotCreateNewGroup,
        ["get-database-groups"] = ExtensionError.NoGroupsFound,
        ["get-database-entries"] = ExtensionError.AccessToAllEntriesDenied,
        ["passkeys-get"] = ExtensionError.PasskeysRequestCanceled,
        ["passkeys-register"] = ExtensionError.PasskeysRequestCanceled,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>KeePassXC's words for an error, which the extension shows as they are sent.</summary>
    internal static string Words(ExtensionError error) => error switch
    {
        ExtensionError.DatabaseNotOpened => "Database not opened",
        ExtensionError.DatabaseHashNotReceived => "Database hash not available",
        ExtensionError.ClientPublicKeyNotReceived => "Client public key not received",
        ExtensionError.CannotDecryptMessage => "Cannot decrypt message",
        ExtensionError.ActionCancelledOrDenied => "Action cancelled or denied",
        ExtensionError.CannotEncryptMessage => "Message encryption failed.",
        ExtensionError.AssociationFailed => "KeePassXC association failed, try again",
        ExtensionError.IncorrectAction => "Incorrect action",
        ExtensionError.EmptyMessageReceived => "Empty message received",
        ExtensionError.NoUrlProvided => "No URL provided",
        ExtensionError.NoLoginsFound => "No logins found",
        ExtensionError.NoGroupsFound => "No groups found",
        ExtensionError.CannotCreateNewGroup => "Cannot create new group",
        ExtensionError.AccessToAllEntriesDenied => "Access to all entries is denied",
        ExtensionError.PasskeysRequestCanceled => "Passkeys request canceled",
        _ => "Unknown error",
    };
}

/// <summary>The error codes of KeePassXC-Browser's protocol the relay answers with.</summary>
internal enum ExtensionError
{
    DatabaseNotOpened = 1,
    DatabaseHashNotReceived = 2,
    ClientPublicKeyNotReceived = 3,
    CannotDecryptMessage = 4,
    ActionCancelledOrDenied = 6,
    CannotEncryptMessage = 7,
    AssociationFailed = 8,
    KeyChangeFailed = 9,
    IncorrectAction = 12,
    EmptyMessageReceived = 13,
    NoUrlProvided = 14,
    NoLoginsFound = 15,
    NoGroupsFound = 16,
    CannotCreateNewGroup = 17,
    AccessToAllEntriesDenied = 19,
    PasskeysRequestCanceled = 22,
}
