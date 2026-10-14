namespace Keypaste.Core.Agents;

/// <summary>
/// The UUIDs ACAF gives the entries and groups that keep agents' records, derived from their names, so
/// two copies of a vault that each create one create the same object and a merge settles it by time.
/// </summary>
public static class AcafIds
{
    /// <summary>ACAF v1's namespace: the version 5 UUID of <c>https://keypaste.com/acaf/v1/</c> in RFC 9562's URL namespace.</summary>
    public static readonly Guid Namespace = new("919d7b74-6e77-5fb0-99eb-74758c119d7d");

    /// <summary>The UUID of the entry that keeps an agent's record: the version 5 UUID of its name in <see cref="Namespace"/>.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>, exactly as the entry is titled.</param>
    /// <returns>The UUID, whose 16 bytes in RFC 9562's order are the entry's KeePass UUID.</returns>
    public static Guid Agent(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Version5(name);
    }

    /// <summary>The UUID of one of keypaste's own groups: the version 5 UUID of its path after a slash, such as <c>/.keypaste/agents</c>, in <see cref="Namespace"/>.</summary>
    /// <param name="groupPath">The group's path, as <see cref="ReservedGroups"/> spells it.</param>
    /// <returns>The UUID, whose 16 bytes in RFC 9562's order are the group's KeePass UUID; no agent's name holds a slash, so no entry shares it.</returns>
    public static Guid Group(string groupPath)
    {
        ArgumentNullException.ThrowIfNull(groupPath);

        return Version5("/" + groupPath);
    }

    /// <summary>RFC 9562's version 5 UUID of a name's UTF-8 bytes in <see cref="Namespace"/>.</summary>
    private static Guid Version5(string name)
    {
        byte[] input = [.. Namespace.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes(name)];

#pragma warning disable CA5350 // RFC 9562's version 5 is SHA-1 by definition; the UUID names an object and protects nothing.
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
