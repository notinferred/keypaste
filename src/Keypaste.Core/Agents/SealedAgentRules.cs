using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>
/// The rules a vault's owner decides by: <c>agents.toml</c> only while its seal holds, an HMAC-SHA256 under a key the
/// vault keeps in <see cref="ReservedGroups.Keys"/>, over this home's identity, the vault's count of sealed writes and the rules;
/// otherwise the defaults.
/// </summary>
/// <remarks>
/// <para>
/// Any program the person runs can write the file, so nothing it says gives an agent more unless the owner wrote it:
/// a missing or wrong seal, a vault without the key, or a home without its identity leaves every category Ask and every
/// agent's run off, and the file as it is. The identity is random per home and covered by the seal, so a file and the
/// vault's key synced or copied from another computer do not make that computer's rules valid here.
/// </para>
/// <para>
/// Each sealed write counts up the vault's count first and writes the file after, so a file sealed before the latest
/// write, put back, is ignored too, and a write whose file never lands leaves the defaults.
/// </para>
/// </remarks>
/// <param name="file">This home's <c>agents.toml</c>.</param>
/// <param name="vault">The owner's unlocked vault, or null while it is locked.</param>
/// <param name="save">Saves an edit to that vault under the owner's claim, which the key is created by when there is none.</param>
public sealed class SealedAgentRules(AgentRulesFile file, Func<Vault?> vault, Func<Func<Vault, bool>, AgentSave> save) : IAgentRules
{
    /// <summary>What the app says while the file's rules are ignored.</summary>
    public const string IgnoredText = "agent rules were changed outside keypaste and are ignored until you set them again";

    /// <summary>The title of the vault's record that holds the key.</summary>
    internal const string KeyTitle = "agent-rules";

    /// <summary>The record's field that holds the key, as base64.</summary>
    internal const string KeyField = "key";

    /// <summary>The record's field that counts the sealed writes, as a decimal number.</summary>
    internal const string CountField = "count";

    private readonly AgentRulesFile _file = file ?? throw new ArgumentNullException(nameof(file));
    private readonly Func<Vault?> _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private readonly Func<Func<Vault, bool>, AgentSave> _save = save ?? throw new ArgumentNullException(nameof(save));
    private volatile string? _ignored;

    /// <inheritdoc/>
    public string? Ignored => _ignored;

    /// <inheritdoc/>
    public bool TryRead([NotNullWhen(true)] out AgentRules? rules, out string problem)
    {
        if (!_file.TryReadFile(out var read, out problem))
        {
            rules = null;
            return false;
        }

        var open = _vault();
        var holds = !read.Present
            || (read.Seal is { } seal && Key(open) is { } key && Count(open) is { } count && _file.HomeId() is { } home
                && Matches(Seal(key, home, count, read.Rules), seal));
        _ignored = holds ? null : IgnoredText;
        rules = holds ? read.Rules : AgentRules.Defaults;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>Applied to the rules that hold now, the defaults while the file's are ignored, so a change made outside keypaste is replaced, never carried on.</remarks>
    public bool TryChange(Func<AgentRules, AgentRules> change, out string problem)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!TryRead(out var current, out problem))
        {
            problem = $"{problem}; keypaste left it as it is";
            return false;
        }

        if (!TryKey(out var key, out problem) || !_file.TryEnsureHomeId(out var home, out problem))
        {
            return false;
        }

        var next = change(current);

        if (Count(_vault()) is not { } count || count == ulong.MaxValue)
        {
            problem = "the vault's count of agent rule changes cannot be read";
            return false;
        }

        var counted = (count + 1).ToString(CultureInfo.InvariantCulture);
        var saved = _save(vault => vault.WriteRecord(new EntryName(ReservedGroups.Keys, KeyTitle), AcafIds.Agent, CountField, counted));

        if (!saved.Holds)
        {
            problem = $"the change could not be saved in the vault: {saved.Reason}";
            return false;
        }

        if (!_file.TryWrite(next, Seal(key, home, count + 1, next), out problem))
        {
            return false;
        }

        _ignored = null;
        return true;
    }

    /// <summary>The seal of rules for a home under a key.</summary>
    /// <param name="key">The vault's key.</param>
    /// <param name="home">This home's identity.</param>
    /// <param name="count">The vault's count of sealed writes, this one's included.</param>
    /// <param name="rules">The rules.</param>
    /// <returns>The HMAC-SHA256, as lower-case hex.</returns>
    internal static string Seal(byte[] key, string home, ulong count, AgentRules rules) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            key,
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"keypaste agents.toml 2\n{home}\n{count}\n{AgentRulesFile.Canonical(rules)}"))));

    private static bool Matches(string expected, string seal) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(seal));

    private static byte[]? Key(Vault? vault)
    {
        try
        {
            return vault?.ReadRecords(ReservedGroups.Keys, KeyField).FirstOrDefault(record => string.Equals(record.Title, KeyTitle, StringComparison.Ordinal))?.Value is { } text
                && Convert.TryFromBase64String(text, new byte[32], out var length) && length == 32
                ? Convert.FromBase64String(text)
                : null;
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The vault's count of sealed writes: none before the first, or null when the vault is locked or the count unreadable.</summary>
    private static ulong? Count(Vault? vault)
    {
        try
        {
            if (vault?.ReadRecords(ReservedGroups.Keys, CountField) is not { } records)
            {
                return null;
            }

            var text = records.FirstOrDefault(record => string.Equals(record.Title, KeyTitle, StringComparison.Ordinal))?.Value;

            return text is not { Length: > 0 }
                ? 0
                : ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The vault's key, made and saved in the vault when it holds none.</summary>
    private bool TryKey([NotNullWhen(true)] out byte[]? key, out string problem)
    {
        if (_vault() is not { } open)
        {
            key = null;
            problem = "the vault is locked";
            return false;
        }

        key = Key(open);

        if (key is not null)
        {
            problem = string.Empty;
            return true;
        }

        var made = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var saved = _save(vault => vault.WriteRecord(new EntryName(ReservedGroups.Keys, KeyTitle), AcafIds.Agent, KeyField, made));

        key = saved.Holds ? Key(_vault()) : null;
        problem = key is not null ? string.Empty : $"the key could not be saved in the vault: {saved.Reason}";
        return key is not null;
    }
}
