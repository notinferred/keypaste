namespace Keypaste.Core;

/// <summary>
/// What an item is for, kept as the standard KeePass icon each kind sets, so KeePassXC shows the same icon.
/// </summary>
public enum ItemKind
{
    /// <summary>No kind is kept: the entry has KeePass's default key icon, another standard icon or a custom one.</summary>
    None = 0,

    /// <summary>A website sign-in, kept as KeePass's world icon.</summary>
    Login,

    /// <summary>An API key or token, kept as KeePass's multiple keys icon.</summary>
    ApiKey,

    /// <summary>A database's credentials, kept as KeePass's disk icon.</summary>
    Database,

    /// <summary>A server's credentials, kept as KeePass's network server icon.</summary>
    Server,

    /// <summary>A note with no password of its own, kept as KeePass's notepad icon.</summary>
    SecureNote,

    /// <summary>A payment card, kept as KeePass's money icon.</summary>
    Card,

    /// <summary>A person's identity details, kept as KeePass's identity icon.</summary>
    Identity,

    /// <summary>An SSH key pair, kept as KeePass's encrypted terminal icon.</summary>
    SshKey,

    /// <summary>A Wi-Fi network's name and password, kept as KeePass's wireless icon.</summary>
    Wifi,

    /// <summary>A project's .env variables, kept as KeePass's console icon.</summary>
    Environment,
}
