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
    /// <summary>The most names one listing will walk out of a vault.</summary>
    /// <remarks>
    /// <para>
    /// <b>This bounds the work, not the answer.</b> What an agent actually sees is bounded by what
    /// one frame can carry, decided in <see cref="Ipc.ApproverProtocol"/> — the only place that
    /// knows what a name costs once it is escaped. An unbounded walk is still a cost problem, so a
    /// ceiling stays here; it is no longer the thing standing between a large vault and a context
    /// window (THREATS.md T-1).
    /// </para>
    /// <para>
    /// <b>It sits deliberately above the most names any frame could hold.</b> The smallest possible
    /// element is twenty-three bytes, so no frame carries more than about two thousand seven
    /// hundred. A cap below that would drop names the encoder never sees, and the encoder would then
    /// report as complete a listing that was not — which is the defect this replaced, moved one
    /// layer up. <c>ApproverProtocolTests.NoFrameCanHoldMoreNamesThanTheListersCap</c> keeps the two
    /// numbers apart.
    /// </para>
    /// </remarks>
    public const int MaximumNames = 4096;

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
            // Anything at all. A narrower filter lets an IOException or a cryptographic failure out
            // of the vault escape the approver and reach the bridge as an unlogged failure; failing
            // closed here is what makes the caller's refusal a decision (law 3.7).
            failure = CredentialFailure.Failed;
            return false;
        }

        failure = CredentialFailure.None;
        return true;
    }
}
