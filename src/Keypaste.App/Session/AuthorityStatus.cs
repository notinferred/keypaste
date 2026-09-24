using Keypaste.Core.Ownership;

namespace Keypaste.App.Session;

/// <summary>What agents configured for a vault meet in this app, as its session authority says.</summary>
internal abstract record AuthorityStatus
{
    private AuthorityStatus()
    {
    }

    /// <summary>No vault is unlocked here, so nothing here answers agents.</summary>
    internal sealed record Locked : AuthorityStatus;

    /// <summary>The last unlock was refused because another keypaste process holds the vault.</summary>
    /// <param name="Owner">That process, as its claim names it.</param>
    internal sealed record HeldBy(VaultOwner Owner) : AuthorityStatus
    {
        /// <summary>The owner as the app says it, on the unlock screen and in Agent Activity.</summary>
        internal string Sentence
        {
            get
            {
                var owner = Owner.Describe();
                return $"{char.ToUpperInvariant(owner[0])}{owner[1..]} holds this vault, and agents reach it there.";
            }
        }
    }

    /// <summary>This process holds the vault and answers agents on its endpoint.</summary>
    /// <param name="Session">The session a request is answered under.</param>
    /// <param name="Endpoint">The pipe agents reach it on.</param>
    /// <param name="Owner">This process, as its claim names it.</param>
    internal sealed record Serving(string Session, string Endpoint, VaultOwner Owner) : AuthorityStatus;

    /// <summary>The vault is unlocked here and agents cannot reach it.</summary>
    /// <param name="Reason">Why, as a sentence.</param>
    internal sealed record NotServing(string Reason) : AuthorityStatus;
}
