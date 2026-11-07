namespace Keypaste.Core.Approval;

/// <summary>Why a prompt offers no Always allow, so a channel can say so.</summary>
public enum OnceOnly
{
    /// <summary>Always allow is offered.</summary>
    None = 0,

    /// <summary>What is asked for lives in a protected environment, which is asked about every time.</summary>
    ProtectedProfile = 1,

    /// <summary>An Automatic limit, its end or its hourly count, was reached, and Always would bypass a limit the person set.</summary>
    LimitReached = 2,

    /// <summary>The agent has no name of its own, so its settings have nowhere to keep it.</summary>
    Unnamed = 3,
}
