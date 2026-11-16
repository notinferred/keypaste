using Keypaste.Core.Agents;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Makes an agent's or a terminal's change in the open vault with its line written before the vault is saved, so a change never
/// exists without its record (THREATS.md T-6): the change is taken back out of the open vault when it no longer applies, when its
/// line cannot be written, and when the save fails after the line.
/// </summary>
internal static class LoggedWrite
{
    /// <summary>What the reply says when the line could not be written before the save.</summary>
    internal const string Unrecorded = "the audit log could not be written, so nothing was changed";

    /// <summary>Makes the change, writes its line, then saves, in that order.</summary>
    /// <param name="write">Runs the change on the lifetime's vault, and saves it when the change answers true.</param>
    /// <param name="lifetime">The unlock the change was asked under.</param>
    /// <param name="apply">Makes the change in the open vault, answering whether it still applied.</param>
    /// <param name="record">Writes the change's line, answering whether it was written.</param>
    /// <returns>How far it got.</returns>
    internal static Outcome Run(
        Func<SessionLifetime, Func<Vault, bool>, AgentSave> write,
        SessionLifetime lifetime,
        Func<Vault, bool> apply,
        Func<bool> record)
    {
        Vault? written = null;
        VaultCheckpoint? checkpoint = null;
        var applied = false;
        var recorded = false;
        AgentSave saved;

        try
        {
            saved = write(lifetime, open =>
            {
                written = open;
                checkpoint = open.Checkpoint();
                applied = apply(open);
                recorded = applied && record();
                return recorded;
            });
        }
        catch
        {
            SessionEditor.TakeBack(written, checkpoint);
            throw;
        }

        var outcome = new Outcome(written is not null, applied, recorded, saved);

        if (!outcome.Made)
        {
            SessionEditor.TakeBack(written, checkpoint);
        }

        return outcome;
    }

    /// <summary>How far a change got.</summary>
    /// <param name="Reached">Whether the open vault was reached to make it.</param>
    /// <param name="Applied">Whether it still applied there.</param>
    /// <param name="Recorded">Whether its line was written, which happens only before its save.</param>
    /// <param name="Saved">What the save came to.</param>
    internal sealed record Outcome(bool Reached, bool Applied, bool Recorded, AgentSave Saved)
    {
        /// <summary>Whether the change no longer applied once the vault was reached.</summary>
        public bool Late => Reached && !Applied;

        /// <summary>Whether it applied and its line could not be written.</summary>
        public bool Unrecorded => Applied && !Recorded;

        /// <summary>Whether it is recorded and saved.</summary>
        public bool Made => Recorded && Saved.Holds;
    }
}
