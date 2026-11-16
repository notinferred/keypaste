using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>Checks a program's code signature with the operating system, for the audit line and nothing else.</summary>
internal interface ICodeSignatures
{
    /// <summary>What a program's signature checks out as, and who signed it when that is cheap to say.</summary>
    /// <param name="executable">The program's absolute path.</param>
    /// <param name="running">On macOS, the audit token of the process running it, which is checked instead of the file; null elsewhere.</param>
    /// <returns>The state, and the signer or null.</returns>
    (CallerSignature State, string? Signer) Check(string executable, MacAuditToken? running);
}
