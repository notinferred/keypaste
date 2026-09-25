using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Cli;

/// <summary>Reaches the process holding a vault unlocked, for the verbs that act on its session: <c>grants</c> and <c>lock</c>.</summary>
/// <remarks>
/// The endpoint is found exactly as <c>keypaste run --session</c> finds it, over the per-user pipe the
/// operating system authenticates (THREATS.md T-10). Nothing here opens the vault or reads a password.
/// </remarks>
internal static class SessionPipe
{
    internal const string ApproverOption = "approver";

    private static readonly TimeSpan _connectTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The pipe the owner of <paramref name="vaultPath"/> listens on.</summary>
    internal static bool TryResolve(CommandLine line, string vaultPath, CliContext context, out string pipe, out string error)
    {
        try
        {
            var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));
            pipe = ApproverEndpoint.Resolve(
                line.Value(ApproverOption),
                context.Environment.Get(ApproverEndpoint.EnvironmentVariable),
                VaultIdentity.Of(home, vaultPath))!;
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            pipe = string.Empty;
            error = $"--{ApproverOption}: {ex.Message}";
            return false;
        }
    }

    /// <summary>Connects and attaches to the session holding the vault.</summary>
    /// <returns>
    /// The attachment; <see cref="SessionAttachment.Nobody"/> when nothing listens or the vault is
    /// locked, which both mean nothing holds it unlocked.
    /// </returns>
    internal static async Task<SessionAttachment> AttachAsync(string pipe, string vaultPath, CancellationToken cancellationToken)
    {
        var client = await ApproverClient.TryConnectAsync(pipe, _connectTimeout, cancellationToken);

        if (client is null)
        {
            return SessionAttachment.Nobody;
        }

        var attached = await client.AttachAsync(new AttachRequest(vaultPath), cancellationToken);

        if (attached is { Attached: true, Session: { } session })
        {
            return new SessionAttachment(client, session, Refusal: null);
        }

        await client.DisposeAsync();

        return attached?.Refusal == AuditMethod.VaultLocked
            ? SessionAttachment.Nobody
            : new SessionAttachment(null, null, attached?.Reason ?? "the keypaste process holding the vault did not answer");
    }
}

/// <summary>A connection attached to the session holding a vault, or why there is none.</summary>
/// <param name="Client">The attached connection, or null.</param>
/// <param name="Session">The session it attached to, or null.</param>
/// <param name="Refusal">Why an owner refused to attach, or null when it attached or nothing holds the vault.</param>
internal sealed record SessionAttachment(ApproverClient? Client, string? Session, string? Refusal) : IAsyncDisposable
{
    /// <summary>Nothing holds the vault unlocked.</summary>
    internal static SessionAttachment Nobody { get; } = new(null, null, null);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Client?.DisposeAsync() ?? ValueTask.CompletedTask;
}
