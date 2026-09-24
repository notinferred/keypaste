using Keypaste.Core.Approval;

namespace Keypaste.App.Tests.Session;

/// <summary>A channel with nobody behind it, for tests about serving the vault rather than asking a person.</summary>
internal sealed class NobodyToAsk : IApprovalChannel
{
    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ApprovalAnswer.NoChannel);
}
