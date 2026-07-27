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
/// its exposure globs and look in the grant cache <em>before</em> anything decrypts a field. A
/// single <c>TryRead(entryArgument, field)</c> would have to read the value before the human had
/// approved anything, which puts a secret in memory for requests that are about to be denied.
/// </para>
/// </remarks>
public interface ICredentialSource
{
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

    /// <summary>Reads one field of one entry.</summary>
    /// <param name="name">The entry, as returned by <see cref="TryResolve"/>.</param>
    /// <param name="field">Which field, lower-case and exactly as the tool schema spells it.</param>
    /// <param name="value">The released field, when one could be read.</param>
    /// <param name="failure">Why not, when none could.</param>
    /// <returns><see langword="true"/> when the field was read and is not empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="field"/> is null.</exception>
    /// <remarks>
    /// Call this only after a human has approved the request, or after a live grant has been found.
    /// It is the only method in keypaste that turns an entry into a secret on the agent path.
    /// </remarks>
    bool TryRead(
        EntryName name,
        string field,
        [NotNullWhen(true)] out ReleasedField? value,
        out CredentialFailure failure);
}
