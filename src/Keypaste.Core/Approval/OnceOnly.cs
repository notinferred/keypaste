namespace Keypaste.Core.Approval;

/// <summary>Why a prompt offers no timed grant, so a channel can say so.</summary>
public enum OnceOnly
{
    /// <summary>A timed grant is offered, or nothing says why not.</summary>
    None = 0,

    /// <summary>What is asked for lives in a protected profile, which is asked about every time.</summary>
    ProtectedProfile = 1,

    /// <summary>The client's policy is Ask every time.</summary>
    ClientPolicy = 2,
}
