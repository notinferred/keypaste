using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;
using Keypaste.Core.Policy;

namespace Keypaste.Core.Tests;

/// <summary>
/// The approver's five seams, with fakes for the vault and the human and nothing faked in between.
/// </summary>
/// <remarks>
/// Shared by the handler's two test classes. One
/// set of fakes rather than two, because the claim both classes make is about the <em>order</em>
/// the handler does things in, and two subtly different vaults would let a change look fine in one
/// file while breaking the ordering asserted in the other.
/// </remarks>
internal sealed class ApproverFixture : IDisposable
{
    internal const string Sentinel = "sk_live_handler_sentinel";

    internal FakeSource Source { get; } = new();

    internal FakeChannel Channel { get; } = new();

    internal ManualClock Clock { get; } = new();

    internal GrantCache Grants { get; }

    internal ApprovalGate Gate { get; }

    internal ApproverHandler Handler { get; }

    /// <summary>What is pre-authorized, on the same clock as everything else.</summary>
    internal PolicyGate Policy { get; }

    /// <summary>Builds a fixture.</summary>
    /// <param name="policy">
    /// The rules in force. Defaults to none, so every test written before this feature existed
    /// keeps asking a person exactly as it did.
    /// </param>
    /// <param name="limits">The operator's own ceilings, when a test needs them narrower.</param>
    /// <remarks>
    /// The policy gate is built here rather than passed in so that it shares <see cref="Clock"/>.
    /// An hourly allowance measured on a second clock would silently ignore
    /// <c>ManualClock.Advance</c>, and the test asserting that an allowance comes back would pass
    /// for the wrong reason.
    /// </remarks>
    internal ApproverFixture(PolicyDocument? policy = null, ApprovalLimits? limits = null)
    {
        Grants = new GrantCache(Clock);
        Gate = new ApprovalGate(Channel, Clock, limits ?? ApprovalLimits.Default);
        Policy = new PolicyGate(policy ?? PolicyDocument.None, Clock);
        Handler = new ApproverHandler(Source, Source, Gate, Grants, Policy);
    }

    public void Dispose()
    {
        Gate.Dispose();
        Grants.Dispose();
    }
}

/// <summary>A vault with two entries, one inside the default exposure and one well outside it.</summary>
internal sealed class FakeSource : ICredentialSource, IEntryNameLister
{
    private static readonly EntryName[] _entries =
    [
        new EntryName("env/dev", "STRIPE_KEY"),
        new EntryName("personal", "bank"),
    ];

    internal bool Locked { get; set; }

    internal bool FailReads { get; set; }

    internal int Reads { get; private set; }

    public bool TryResolve(string entryArgument, [NotNullWhen(true)] out EntryName? name, out CredentialFailure failure)
    {
        name = null;

        if (Locked)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        foreach (var candidate in _entries)
        {
            var path = candidate.GroupPath.Length == 0
                ? candidate.Title
                : candidate.GroupPath + "/" + candidate.Title;

            if (string.Equals(path, entryArgument, StringComparison.Ordinal)
                || string.Equals(EntryHandle.For(candidate), entryArgument, StringComparison.Ordinal))
            {
                name = candidate;
                failure = CredentialFailure.None;
                return true;
            }
        }

        failure = CredentialFailure.NotFound;
        return false;
    }

    public bool TryRead(EntryName name, string field, [NotNullWhen(true)] out ReleasedField? value, out CredentialFailure failure)
    {
        value = null;

        if (FailReads)
        {
            failure = CredentialFailure.Failed;
            return false;
        }

        Reads++;
        value = new ReleasedField(field, ApproverFixture.Sentinel);
        failure = CredentialFailure.None;
        return true;
    }

    public bool TryList(EntryExposure exposure, [NotNullWhen(true)] out IReadOnlyList<EntryName>? names, out CredentialFailure failure)
    {
        names = null;

        if (Locked)
        {
            failure = CredentialFailure.VaultLocked;
            return false;
        }

        names = [.. _entries.Where(exposure.Allows)];
        failure = CredentialFailure.None;
        return true;
    }
}

internal sealed class FakeChannel : IApprovalChannel
{
    internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

    internal int Asked { get; private set; }

    internal ApprovalPrompt? LastPrompt { get; private set; }

    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        Asked++;
        LastPrompt = prompt;
        return ValueTask.FromResult(Answer);
    }
}
