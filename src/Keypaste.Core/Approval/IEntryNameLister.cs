using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Approval;

/// <summary>
/// The names an agent may be shown. Yields <see cref="EntryName"/> and has nowhere to put anything
/// else.
/// </summary>
/// <remarks>
/// <para>
/// A separate interface from <see cref="ICredentialSource"/>, and that separation is the whole
/// point (DECISIONS.md D-0022). Everything reachable through here is a group path and a title, so
/// no implementation — including this one — can return a password through the listing path even by
/// mistake. Fusing the two into a single "vault access" abstraction would give the listing path the
/// ability to return a secret, which is the single change most likely to turn
/// <c>list_entry_names</c> into an exfiltration tool (THREATS.md T-8).
/// </para>
/// <para>
/// The exposure is applied <em>here</em> rather than by the caller, so there is no arrangement of
/// callers in which a name outside it is ever produced.
/// </para>
/// </remarks>
public interface IEntryNameLister
{
    /// <summary>The names inside an exposure.</summary>
    /// <param name="exposure">What may be named.</param>
    /// <param name="names">The raw, unsanitized names. Sanitizing belongs to whoever renders them.</param>
    /// <param name="failure">Why there are none, when there are none.</param>
    /// <returns><see langword="true"/> when the vault could be read, even if nothing matched.</returns>
    bool TryList(
        EntryExposure exposure,
        [NotNullWhen(true)] out IReadOnlyList<EntryName>? names,
        out CredentialFailure failure);
}

/// <summary>Lists the names in an unlocked vault that lie inside an exposure.</summary>
/// <param name="unlockedVault">
/// The vault currently unlocked in this process, or <see langword="null"/> when none is.
/// </param>
public sealed class VaultEntryNameLister(Func<Vault?> unlockedVault) : IEntryNameLister
{
    /// <summary>The most names one listing will produce.</summary>
    /// <remarks>
    /// An unbounded listing is a cost problem and an injection amplifier both: enough entries will
    /// push a system prompt out of a context window as effectively as any jailbreak (THREATS.md
    /// T-1). The bridge caps again on its own side, because two caps are cheaper than one that has
    /// to be right.
    /// </remarks>
    public const int MaximumNames = 1000;

    private readonly Func<Vault?> _unlockedVault =
        unlockedVault ?? throw new ArgumentNullException(nameof(unlockedVault));

    /// <inheritdoc/>
    public bool TryList(
        EntryExposure exposure,
        [NotNullWhen(true)] out IReadOnlyList<EntryName>? names,
        out CredentialFailure failure)
    {
        ArgumentNullException.ThrowIfNull(exposure);

        names = null;

        var vault = _unlockedVault();

        if (vault is null)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        try
        {
            var matched = new List<EntryName>();

            foreach (var entry in vault.ReadEntries())
            {
                var name = EntryName.Of(entry);

                if (!exposure.Allows(name))
                {
                    continue;
                }

                matched.Add(name);

                if (matched.Count == MaximumNames)
                {
                    break;
                }
            }

            names = matched;
        }
        catch (Exception)
        {
            // Anything at all. The narrower filter this replaces named the two it expected, and an
            // IOException or a cryptographic failure out of the vault is neither - so it escaped the
            // approver entirely and reached the bridge as an unlogged failure. Failing closed here
            // is what makes the caller's refusal a decision rather than an accident (law 3.7).
            failure = CredentialFailure.Failed;
            return false;
        }

        failure = CredentialFailure.None;
        return true;
    }
}
