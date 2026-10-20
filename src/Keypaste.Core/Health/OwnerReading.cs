using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Core.Health;

/// <summary>What asking the vault's pipe found.</summary>
public enum OwnerAnswer
{
    /// <summary>No vault is named or chosen, so nothing was asked.</summary>
    NoVault,

    /// <summary><c>KEYPASTE_APPROVER</c> is not a pipe name keypaste accepts.</summary>
    InvalidPipe,

    /// <summary>Nothing listens on the vault's pipe.</summary>
    NothingListening,

    /// <summary>The owner attached the connection: it holds the vault unlocked.</summary>
    Unlocked,

    /// <summary>The owner holds the vault locked.</summary>
    Locked,

    /// <summary>The process that answered holds another vault.</summary>
    OtherVault,

    /// <summary>Something listens, and gave no answer this keypaste reads.</summary>
    NoAnswer,
}

/// <summary>Whether the keypaste app or <c>keypaste agent</c> holds the vault and answers, and its version.</summary>
/// <param name="Answer">What the pipe found.</param>
/// <param name="Kind">Who holds the vault, as its claim names it, for display only; null when none is named.</param>
/// <param name="Version">The version the owner gave, or null when it gave none or nothing answered.</param>
/// <param name="Finding">What doctor says of it.</param>
/// <remarks>
/// The owner is attached to as <c>grants</c> and <c>lock</c> do, with no AI tool's name, so it counts no agent; nothing
/// else is asked. The pipe a locked app listens on for agents is never reached.
/// </remarks>
public sealed record OwnerReading(OwnerAnswer Answer, OwnerKind? Kind, string? Version, HealthFinding Finding)
{
    private const string _subject = "owner";

    private static readonly TimeSpan _connectWait = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan _answerWait = TimeSpan.FromSeconds(10);

    /// <summary>Asks the process holding <paramref name="vault"/> whether it answers, attaching and nothing more.</summary>
    /// <param name="scope">What to look at.</param>
    /// <param name="vault">The vault's full path, or null when none is named or chosen.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>What the pipe found.</returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public static async Task<OwnerReading> AskAsync(HealthScope scope, string? vault, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (vault is null)
        {
            return new OwnerReading(OwnerAnswer.NoVault, Kind: null, Version: null, Said(HealthStatus.Note, "no vault to ask about", fix: null));
        }

        var home = KeypasteHome.Resolve(scope.Home);
        var kind = VaultClaim.HolderOf(home, vault)?.Kind;
        var named = scope.Environment(ApproverEndpoint.EnvironmentVariable);
        var elsewhere = $"unset {ApproverEndpoint.EnvironmentVariable}, or point it at the process holding {vault}";

        if (named is { Length: > 0 } && !ApproverEndpoint.IsValidName(named, out var error))
        {
            return Found(OwnerAnswer.InvalidPipe, version: null, HealthStatus.Fix, $"{ApproverEndpoint.EnvironmentVariable} is not a pipe name keypaste accepts: {error}", elsewhere);
        }

        var pipe = ApproverEndpoint.Resolve(null, named, VaultIdentity.Of(home, vault))!;
        await using var client = await ApproverClient.TryConnectAsync(pipe, _connectWait, cancellationToken).ConfigureAwait(false);

        if (client is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Found(OwnerAnswer.NothingListening, version: null, HealthStatus.Note, "nothing holds the vault unlocked; agents are refused", "unlock it in the keypaste app, or run `keypaste agent`");
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_answerWait);

        var reply = await client.AttachAsync(new AttachRequest(vault), bounded.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return reply switch
        {
            { Attached: true } => Found(OwnerAnswer.Unlocked, reply.OwnerVersion, HealthStatus.Ok, $"{Holder(kind)} holds the vault and answers", fix: null),
            { Refusal: AuditMethod.VaultLocked } => Found(OwnerAnswer.Locked, reply.OwnerVersion, HealthStatus.Note, $"{Holder(kind)} holds the vault locked; agents are refused", "unlock it in the keypaste app"),
            { Refusal: AuditMethod.NoSession } => Found(OwnerAnswer.OtherVault, reply.OwnerVersion, HealthStatus.Fix, "the keypaste process that answered holds a different vault", elsewhere),
            _ => Found(OwnerAnswer.NoAnswer, version: null, HealthStatus.Fix, $"{Holding(kind)} did not answer; it may be older than this keypaste", Restart(kind)),
        };

        OwnerReading Found(OwnerAnswer answer, string? version, HealthStatus status, string says, string? fix) =>
            new(answer, kind, version, Said(status, says, fix));
    }

    /// <summary>The process holding the vault, as a sentence names it.</summary>
    internal static string Holding(OwnerKind? kind) => kind switch
    {
        OwnerKind.DesktopApp => "the keypaste app holding the vault",
        OwnerKind.TerminalAgent => "keypaste agent holding the vault",
        _ => "the keypaste process holding the vault",
    };

    /// <summary>How to start the process holding the vault again.</summary>
    internal static string Restart(OwnerKind? kind) => kind switch
    {
        OwnerKind.DesktopApp => "quit the keypaste app and open it again",
        OwnerKind.TerminalAgent => "stop keypaste agent and start it again",
        _ => "quit the keypaste app or stop `keypaste agent`, and start it again",
    };

    private static string Holder(OwnerKind? kind) => kind switch
    {
        OwnerKind.TerminalAgent => "keypaste agent",
        OwnerKind.DesktopApp => "the keypaste app",
        _ => "a keypaste process",
    };

    private static HealthFinding Said(HealthStatus status, string says, string? fix) =>
        new(HealthArea.Owner, _subject, status, says) { Fix = fix };
}
