using Keypaste.Core.Audit;

namespace Keypaste.Core.Approval;

/// <summary>Reading an <see cref="ApprovalAnswer"/> the one safe way.</summary>
public static class ApprovalAnswers
{
    /// <summary>Whether the answer releases anything. Every other value is a denial.</summary>
    public static bool Releases(this ApprovalAnswer answer) =>
        answer is ApprovalAnswer.Approved or ApprovalAnswer.ApprovedOnce;

    /// <summary>The audit method a denial is recorded under.</summary>
    /// <param name="answer">An answer that did not release.</param>
    /// <returns>The method; a person's no is <see cref="AuditMethod.Prompt"/>.</returns>
    public static AuditMethod ToAuditMethod(this ApprovalAnswer answer) => answer switch
    {
        ApprovalAnswer.Denied => AuditMethod.Prompt,
        ApprovalAnswer.TimedOut => AuditMethod.TimedOut,
        ApprovalAnswer.Cancelled => AuditMethod.Cancelled,
        ApprovalAnswer.Busy => AuditMethod.Busy,
        ApprovalAnswer.Cooldown => AuditMethod.Cooldown,
        ApprovalAnswer.NoChannel => AuditMethod.NoApprover,
        _ => AuditMethod.Failed,
    };
}
