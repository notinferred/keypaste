using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>An owner's agent settings: records in its open vault, saved through its own save, and its home's <c>clients.toml</c>.</summary>
/// <remarks>
/// The vault arrives through a delegate, as <see cref="Approval.VaultCredentialSource"/>'s does, so a
/// locked owner answers with no records rather than with a disposed vault. A vault that cannot be read
/// refuses, rather than falling back to the defaults, which could give more than a record says.
/// </remarks>
/// <param name="unlockedVault">The vault this owner holds unlocked, or null while it is locked.</param>
/// <param name="save">Runs an edit on the open vault and saves it when the edit says it changed something.</param>
/// <param name="capsFile">This owner's <c>clients.toml</c>.</param>
public sealed class VaultAgentSettings(Func<Vault?> unlockedVault, Func<Func<Vault, bool>, AgentSave> save, AccessCapsSource capsFile) : IAgentSettings
{
    private readonly Func<Vault?> _unlockedVault = unlockedVault ?? throw new ArgumentNullException(nameof(unlockedVault));
    private readonly Func<Func<Vault, bool>, AgentSave> _save = save ?? throw new ArgumentNullException(nameof(save));
    private readonly AccessCapsSource _caps = capsFile ?? throw new ArgumentNullException(nameof(capsFile));

    /// <inheritdoc/>
    public bool TryRead(string? label, [NotNullWhen(true)] out AgentRecords? records, [NotNullWhen(true)] out AccessCaps? caps, out string problem)
    {
        records = null;

        if (!_caps.TryCurrent(out caps, out problem))
        {
            return false;
        }

        try
        {
            records = _unlockedVault() is { } vault ? new AgentAccessStore(vault).Read(label) : AgentRecords.None;
            return true;
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            caps = null;
            problem = "the vault's agent settings could not be read";
            return false;
        }
    }

    /// <inheritdoc/>
    public AgentRecordList List()
    {
        try
        {
            if (_unlockedVault() is not { } vault)
            {
                return AgentRecordList.Empty;
            }

            var records = new AgentAccessStore(vault).List();
            return new AgentRecordList(vault.ReadSavedRecords(ReservedGroups.Agents, AgentAccessStore.Field, out _), records);
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            return AgentRecordList.Empty;
        }
    }

    /// <inheritdoc/>
    /// <remarks>With no vault open, the change reads as made to the defaults, and writing it is refused as locked.</remarks>
    public bool TryPreview(string name, AccessEdit edit, [NotNullWhen(true)] out AcafRead? record, out string problem)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(edit);

        record = null;

        try
        {
            if (_unlockedVault() is not { } vault)
            {
                record = new AcafRead(edit.Apply(AgentAccess.Defaults), true, true);
                problem = string.Empty;
                return true;
            }

            var outcome = new AgentAccessStore(vault).Preview(name, edit, out record);
            problem = Refusal(outcome) ?? string.Empty;
            return record is not null;
        }
        catch (Exception failure) when (failure is VaultException or ObjectDisposedException)
        {
            record = null;
            problem = "the vault's agent settings could not be read";
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A save that fails takes the change back out of the open vault, so that no later save, of an
    /// entry or another agent's settings, writes what the reply says was not made.
    /// </remarks>
    public AgentSave Write(string name, AccessEdit edit)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(edit);

        var written = AgentAccessWrite.Unchanged;
        Action? undo = null;
        var saved = _save(vault => (written = new AgentAccessStore(vault).Write(name, edit, out undo)) == AgentAccessWrite.Written);

        if (!saved.Holds && undo is not null)
        {
            try
            {
                undo();
            }
            catch (ObjectDisposedException)
            {
                // Locked meanwhile: the open vault, and the change with it, is gone.
            }
        }

        return Refusal(written) is { } why ? new AgentSave(AgentSaveOutcome.Refused, why) : saved;
    }

    /// <inheritdoc/>
    public bool TryChangeCaps(Func<AccessCaps, AccessCaps> change, out string problem)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (AccessCaps.TryChange(_caps.Path, change, out problem))
        {
            case CapsChange.Written or CapsChange.Unchanged:
                return true;
            case CapsChange.Unreadable:
                problem = $"{_caps.Path}: {problem}";
                return false;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public CapsFile ProbeCaps(out string problem) => AccessCaps.Probe(_caps.Path, out problem);

    /// <inheritdoc/>
    public CapsChange TryResetCaps(out string kept, out string problem) => AccessCaps.TryReset(_caps.Path, out kept, out problem);

    private static string? Refusal(AgentAccessWrite written) => written switch
    {
        AgentAccessWrite.InvalidName => "that is not a name an agent's settings are kept under",
        AgentAccessWrite.UnknownVersion => "the vault holds this agent's settings in a version of the format keypaste does not know, which it never rewrites",
        AgentAccessWrite.WiderThanDefaults => "the settings for every other agent only take away from the defaults",
        AgentAccessWrite.ProjectsUnreadable => "the vault holds this agent's project rows in a form keypaste cannot read, and changing one would lose them: fix them in KeePassXC, or reset this agent's settings",
        AgentAccessWrite.TooManyProjects => string.Create(
            CultureInfo.InvariantCulture,
            $"this agent already has {AgentAccess.MaximumProjects} project rows, the most its settings keep; remove one it no longer needs, then try again"),
        AgentAccessWrite.ProjectsOverLimit => string.Create(
            CultureInfo.InvariantCulture,
            $"the vault holds more than {AgentAccess.MaximumProjects} project rows for this agent, which keypaste does not read: remove some in KeePassXC, or reset this agent's settings"),
        _ => null,
    };
}
