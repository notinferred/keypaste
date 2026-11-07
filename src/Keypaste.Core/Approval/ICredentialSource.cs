using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Approval;

/// <summary>
/// Resolves an agent's entry argument to a name, and — separately — reads one field of it.
/// </summary>
/// <remarks>
/// <para>
/// This is the second seam DECISIONS.md D-0022 required. It is deliberately not
/// <c>IEntryNameSource</c>: fusing the listing path and the credential path into one "vault access"
/// abstraction would give the listing path the ability to return a secret, which is the single
/// change most likely to turn <c>list_entry_names</c> into an exfiltration tool.
/// </para>
/// <para>
/// <b>Resolving and reading are two calls, and the order matters.</b> Resolution yields an
/// <see cref="EntryName"/>, which structurally cannot carry a secret, so the caller can re-check
/// its exposure globs and check its settings <em>before</em> anything decrypts a field. A
/// single <c>TryRead(entryArgument, field)</c> would have to read the value before the human had
/// approved anything, which puts a secret in memory for requests that are about to be denied.
/// </para>
/// </remarks>
public interface ICredentialSource
{
    /// <summary>An entry's UUID as hex, which an Always allow row names it by.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>The UUID, or null when it cannot be read, so no row covers it and none is offered.</returns>
    string? ItemId(EntryName name) => null;

    /// <summary>The name of the entry with a UUID, for a row or a confirmation to show.</summary>
    /// <param name="item">The UUID as hex.</param>
    /// <returns>Its name, or null when no live entry has it.</returns>
    EntryName? ItemNamed(string item) => null;

    /// <summary>Finds which entry an agent's <c>entry</c> argument names.</summary>
    /// <param name="entryArgument">The argument exactly as it arrived, handle or path.</param>
    /// <param name="name">The entry it names, when exactly one entry does.</param>
    /// <param name="failure">Why not, when no single entry does.</param>
    /// <returns><see langword="true"/> when exactly one entry answers to the argument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entryArgument"/> is null.</exception>
    /// <remarks>
    /// Handles are tried first and an exact path match is the fallback, because an entry may
    /// legitimately be <em>titled</em> something handle-shaped (<see cref="EntryHandle.Classify"/>).
    /// An argument matching more than one entry is <see cref="CredentialFailure.Ambiguous"/>.
    /// </remarks>
    bool TryResolve(
        string entryArgument,
        [NotNullWhen(true)] out EntryName? name,
        out CredentialFailure failure);

    /// <summary>Every item an agent's <c>entry</c> argument names, by the rule <see cref="TryResolve"/> applies, for the person to pick one.</summary>
    /// <param name="entryArgument">The argument exactly as it arrived, handle or path.</param>
    /// <param name="items">The items, each carrying its UUID, in the vault's order.</param>
    /// <param name="failure">Why none could be read.</param>
    /// <returns><see langword="true"/> when the vault could be read.</returns>
    /// <remarks>
    /// Each item's username and URL are for the person picking, who sees them on Items too, and never for the agent.
    /// Nothing here reads a field the agent asked for.
    /// </remarks>
    bool TryChoose(string entryArgument, out IReadOnlyList<AnsweringItem> items, out CredentialFailure failure)
    {
        items = [];
        failure = CredentialFailure.Ambiguous;
        return false;
    }

    /// <summary>Reads one field of one entry, with the entry's own placeholders filled in (D-0465), or computes its current TOTP code.</summary>
    /// <param name="name">The entry, as returned by <see cref="TryResolve"/>, or one of <see cref="TryChoose"/>'s, which is read only while its UUID and its name still agree.</param>
    /// <param name="field">
    /// Which field, exactly as the tool schema spells it, or <see cref="CredentialFields.Totp"/> for the code, which is
    /// released with how long it stays current and never with its seed (D-0480).
    /// </param>
    /// <param name="value">The released field, when one could be read.</param>
    /// <param name="failure">Why not, when none could.</param>
    /// <param name="unfilled">
    /// On <see cref="CredentialFailure.Unfilled"/>, why the value cannot leave, naming the placeholder and
    /// never a value, such as <c>holds the KeePass placeholder {REF:…}, which keypaste does not fill in</c>;
    /// otherwise empty.
    /// </param>
    /// <returns><see langword="true"/> when the field was read and filled in, and is not empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="field"/> is null.</exception>
    /// <remarks>
    /// Call this only after a human has approved the request, or after a setting they confirmed answers it.
    /// It is the only method in keypaste that turns an entry into a secret on the agent path.
    /// </remarks>
    bool TryRead(
        EntryName name,
        string field,
        [NotNullWhen(true)] out ReleasedField? value,
        out CredentialFailure failure,
        out string unfilled);

    /// <summary>Whether a release of this entry is asked about live every time, with no Always allow and no rule (D-0348).</summary>
    /// <param name="name">The entry, as returned by <see cref="TryResolve"/>.</param>
    /// <returns><see langword="true"/> for an entry in a protected environment.</returns>
    /// <remarks>The entry's own tags decide (D-0371); its path never does (D-0416).</remarks>
    bool RequiresLiveApproval(EntryName name);

    /// <summary>The entry's own tags, which decide what a <c>tag:</c> exposure reaches (D-0422).</summary>
    /// <param name="name">The entry, as returned by <see cref="TryResolve"/>.</param>
    /// <returns>Its tags, or none when they cannot be read, so a tag reaches nothing it cannot be shown to carry.</returns>
    IReadOnlyList<string> Tags(EntryName name);
}
