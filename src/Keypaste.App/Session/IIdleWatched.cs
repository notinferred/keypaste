namespace Keypaste.App.Session;

/// <summary>What a person's use of the window keeps unlocked: one vault's session, or every vault open in the app.</summary>
internal interface IIdleWatched
{
    /// <summary>A person did something; postpones the idle lock, or locks when its deadline has already passed.</summary>
    void Touch();

    /// <summary>Re-checks the idle deadline now, instead of waiting for the timer.</summary>
    void Reevaluate();
}
