using System.Globalization;
using Keypaste.Core.Ownership;

namespace Keypaste.Core.Audit;

/// <summary>The key a vault signs its owners' audit checkpoints with, and the vault it belongs to.</summary>
/// <param name="Key">The 32 random bytes kept in the vault's <see cref="ReservedGroups.Keys"/>.</param>
/// <param name="Vault">The vault, as <see cref="VaultIdentity.Key"/>.</param>
public sealed record AuditCheckpointKey(byte[] Key, string Vault)
{
    /// <summary>The checkpoint key an open vault holds.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="identity">The vault as this home names it.</param>
    /// <returns>The key, or null when the vault holds none or cannot be read.</returns>
    public static AuditCheckpointKey? Read(Vault? vault, VaultIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return AuditCheckpoint.KeyOf(vault) is { } key ? new AuditCheckpointKey(key, identity.Key) : null;
    }
}

/// <summary>
/// A line the vault's owner writes into the log, carrying an HMAC-SHA256 over its own position and <c>prev</c>,
/// so a chain recomputed without the vault's key no longer verifies under it.
/// </summary>
/// <remarks>
/// The vault and the HMAC sit at a fixed width just before the chain footer, so the verifier reads them from the
/// line's bytes as it reads the chain, without a JSON parser.
/// </remarks>
internal static class AuditCheckpoint
{
    internal const int Every = 64;

    internal const string Method = "checkpoint";

    internal const string KeyTitle = "audit-checkpoints";

    internal const string KeyField = "key";

    private const int _keyBytes = 32;

    private static ReadOnlySpan<byte> VaultPrefix => ",\"vault\":\""u8;

    private static ReadOnlySpan<byte> MacPrefix => "\",\"checkpoint\":\""u8;

    private static int Width => VaultPrefix.Length + VaultIdentity.KeyLength + MacPrefix.Length + AuditChain.HashHexLength + 1;

    internal static AuditRecord Record(AuditCheckpointKey key, string session, long sequence, string previous)
    {
        if (key.Vault.Length != VaultIdentity.KeyLength || !IsHex(key.Vault))
        {
            throw new ArgumentException("a checkpoint names its vault by its 16-character key", nameof(key));
        }

        return new AuditRecord
        {
            Tool = Method,
            Client = AuditClient.Unknown,
            Decision = AuditDecision.Denied,
            Method = AuditMethod.Checkpoint,
            Reason = "the vault's owner signed the log up to here",
            Session = session,
            Vault = key.Vault,
            Checkpoint = Mac(key, sequence, previous),
        };
    }

    internal static bool Verifies(AuditCheckpointKey key, long sequence, string previous, string mac) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(key, sequence, previous)), Encoding.ASCII.GetBytes(mac));

    internal static bool TryRead(ReadOnlySpan<byte> line, out string vault, out string mac)
    {
        vault = string.Empty;
        mac = string.Empty;

        if (line.Length < Width + AuditChain.ChainFooterBytes)
        {
            return false;
        }

        var field = line[^(Width + AuditChain.ChainFooterBytes)..^AuditChain.ChainFooterBytes];

        if (!field.StartsWith(VaultPrefix) || !field[(VaultPrefix.Length + VaultIdentity.KeyLength)..].StartsWith(MacPrefix) || field[^1] != (byte)'"')
        {
            return false;
        }

        var named = Encoding.ASCII.GetString(field.Slice(VaultPrefix.Length, VaultIdentity.KeyLength));
        var signed = Encoding.ASCII.GetString(field.Slice(VaultPrefix.Length + VaultIdentity.KeyLength + MacPrefix.Length, AuditChain.HashHexLength));

        if (!IsHex(named) || !AuditChain.IsChainValue(signed))
        {
            return false;
        }

        vault = named;
        mac = signed;
        return true;
    }

    internal static byte[]? KeyOf(Vault? vault)
    {
        try
        {
            return KeyIn(vault?.ReadRecords(ReservedGroups.Keys, KeyField));
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The key the vault's file holds, read only while the open vault holds exactly what its file does.</summary>
    internal static byte[]? SavedKeyOf(Vault vault) =>
        vault.ReadSavedRecords(ReservedGroups.Keys, KeyField, out var records) == SavedRead.Current ? KeyIn(records) : null;

    /// <summary>Writes a new random key into the vault, unless it already holds one; set as <see cref="Vault.BeforeEachSave"/>, a save already being made writes it.</summary>
    internal static bool WriteNewKey(Vault vault) =>
        KeyOf(vault) is null
        && vault.WriteRecord(new EntryName(ReservedGroups.Keys, KeyTitle), Agents.AcafIds.Agent, KeyField, Convert.ToBase64String(RandomNumberGenerator.GetBytes(_keyBytes)));

    private static byte[]? KeyIn(IEnumerable<ReservedRecord>? records)
    {
        var text = records?.FirstOrDefault(record => string.Equals(record.Title, KeyTitle, StringComparison.Ordinal))?.Value;
        var key = new byte[_keyBytes];

        return text is not null && Convert.TryFromBase64String(text, key, out var length) && length == _keyBytes ? key : null;
    }

    private static string Mac(AuditCheckpointKey key, long sequence, string previous) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            key.Key,
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"keypaste audit checkpoint 1\n{key.Vault}\n{sequence}\n{previous}"))));

    private static bool IsHex(string text) => text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
