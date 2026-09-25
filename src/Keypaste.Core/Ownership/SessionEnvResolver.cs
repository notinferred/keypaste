namespace Keypaste.Core.Ownership;

/// <summary>
/// Resolves a project's env set from the vault a session's owner holds, released only while the
/// lifetime that asked is live (D-0313) and only from the vault as its file holds it (D-0317).
/// </summary>
/// <remarks>
/// <para>
/// A set is refused before anybody is asked about it. What a person confirms is its names; the set
/// is read again after they answer, so an edit saved meanwhile is what leaves, a file another
/// program saved meanwhile is refused, and a set whose names changed is refused rather than
/// released under a confirmation that did not cover it.
/// </para>
/// <para>
/// A lock while the person is being asked withdraws the question, and a lock after it commits
/// nothing: either way no value leaves.
/// </para>
/// </remarks>
public sealed class SessionEnvResolver
{
    private readonly Func<SessionLifetime?> _lifetime;
    private readonly Func<SessionLifetime, Vault?> _vaultFor;
    private readonly TimeProvider _clock;

    /// <summary>Builds the resolver for one owner.</summary>
    /// <param name="lifetime">The live lifetime a request would be answered under, or null while locked.</param>
    /// <param name="vaultFor">The vault a lifetime may read, or null once it has ended.</param>
    /// <param name="clock">What expiry is judged against.</param>
    public SessionEnvResolver(Func<SessionLifetime?> lifetime, Func<SessionLifetime, Vault?> vaultFor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(vaultFor);
        ArgumentNullException.ThrowIfNull(clock);

        _lifetime = lifetime;
        _vaultFor = vaultFor;
        _clock = clock;
    }

    /// <summary>Resolves a project's default profile, asking <paramref name="confirm"/> about its names first when given.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="confirm">Asks about the set's names and answers whether to release it, or null to ask nobody.</param>
    /// <param name="cancellationToken">Withdraws the request.</param>
    /// <returns>The set, or why nothing was released.</returns>
    public ValueTask<EnvResolved> ResolveAsync(
        string project,
        Func<EnvPreview, CancellationToken, ValueTask<bool>>? confirm,
        CancellationToken cancellationToken) =>
        ResolveAsync(project, EnvProfileNames.Default, keys: null, confirm, cancellationToken);

    /// <summary>Resolves one profile of a project, or only some of its keys, asking <paramref name="confirm"/> about its names first when given.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <param name="keys">The keys to release, or null for the whole set.</param>
    /// <param name="confirm">Asks about the set's names and answers whether to release it, or null to ask nobody.</param>
    /// <param name="cancellationToken">Withdraws the request.</param>
    /// <returns>The set, or why nothing was released.</returns>
    public ValueTask<EnvResolved> ResolveAsync(
        string project,
        string profile,
        IReadOnlyList<string>? keys,
        Func<EnvPreview, CancellationToken, ValueTask<bool>>? confirm,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);

        return ResolveAsync(vault => EnvResolution.Resolve(vault, project, profile, keys, _clock), project, profile, confirm, cancellationToken);
    }

    /// <summary>Resolves a reference document's variables, asking <paramref name="confirm"/> about their names first when given.</summary>
    /// <param name="document">A document with no problems and no literals.</param>
    /// <param name="confirm">Asks about the names and answers whether to release them, or null to ask nobody.</param>
    /// <param name="cancellationToken">Withdraws the request.</param>
    /// <returns>The variables in the document's order, or why none of them.</returns>
    /// <remarks>The same read, confirm, read again, same names, commit flow as a set.</remarks>
    public ValueTask<EnvResolved> ResolveReferencesAsync(
        EnvReferenceDocument document,
        Func<EnvPreview, CancellationToken, ValueTask<bool>>? confirm,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        return ResolveAsync(
            vault => EnvReferenceResolution.Resolve(vault, document, _clock),
            EnvReferenceResolution.MixedProject,
            EnvReferenceResolution.MixedProfile,
            confirm,
            cancellationToken);
    }

    private async ValueTask<EnvResolved> ResolveAsync(
        Func<Vault, EnvResolved> resolve,
        string project,
        string profile,
        Func<EnvPreview, CancellationToken, ValueTask<bool>>? confirm,
        CancellationToken cancellationToken)
    {
        if (_lifetime() is not { IsLive: true } lifetime)
        {
            return Locked(project, profile);
        }

        var resolved = Read(lifetime, resolve, project, profile);

        if (resolved.Outcome != EnvOutcome.Resolved)
        {
            return resolved;
        }

        if (confirm is not null)
        {
            var preview = resolved.Preview;
            using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Ended);
            bool confirmed;

            try
            {
                confirmed = await confirm(preview, withdrawn.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (withdrawn.IsCancellationRequested)
            {
                return lifetime.IsLive ? EnvResolved.Refused(preview.Project, EnvOutcome.Declined, profile: preview.Profile) : Locked(project, profile);
            }

            if (!confirmed)
            {
                return EnvResolved.Refused(preview.Project, EnvOutcome.Declined, profile: preview.Profile);
            }

            resolved = Read(lifetime, resolve, project, profile);

            if (resolved.Outcome != EnvOutcome.Resolved)
            {
                return resolved;
            }

            if (!resolved.Preview.Keys.SequenceEqual(preview.Keys, StringComparer.Ordinal))
            {
                return EnvResolved.Refused(preview.Project, EnvOutcome.ChangedWhileAsked, profile: preview.Profile);
            }
        }

        return lifetime.TryCommit() ? resolved : Locked(project, profile);
    }

    private EnvResolved Read(SessionLifetime lifetime, Func<Vault, EnvResolved> resolve, string project, string profile)
    {
        if (_vaultFor(lifetime) is not { } vault)
        {
            return Locked(project, profile);
        }

        try
        {
            return resolve(vault);
        }
        catch (ObjectDisposedException)
        {
            // The lock disposed the vault between handing it over and reading it.
            return Locked(project, profile);
        }
    }

    private static EnvResolved Locked(string project, string profile) =>
        EnvResolved.Refused(project, EnvOutcome.Locked, profile: profile);
}
