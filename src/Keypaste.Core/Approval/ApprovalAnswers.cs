namespace Keypaste.Core.Approval;

/// <summary>Reading an <see cref="ApprovalAnswer"/> the one safe way.</summary>
public static class ApprovalAnswers
{
    /// <summary>Whether the answer releases anything. Every other value is a denial.</summary>
    public static bool Releases(this ApprovalAnswer answer) =>
        answer is ApprovalAnswer.Approved or ApprovalAnswer.ApprovedOnce;
}
