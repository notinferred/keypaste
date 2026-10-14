using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;

namespace Keypaste.Mcp.Tests;

/// <summary>Puts a real handler behind the session check a vault's owner applies.</summary>
internal static class TestSession
{
    /// <summary>The session every test owner holds.</summary>
    internal const string Id = "test-session";

    private static readonly SessionLifetime _lifetime = new(Id);

    /// <summary>What the harness's agent may do: see as far as its bridge exposes, and run commands.</summary>
    internal static AgentAccess Sees { get; } = AgentAccess.Defaults with { See = Everything(), Run = true };

    /// <summary>Agents' settings under which the harness's agent gets <see cref="Sees"/>.</summary>
    /// <returns>The settings, for one owner.</returns>
    internal static FakeAgentSettings Settings()
    {
        var settings = new FakeAgentSettings();
        settings.Records[McpHarness.ClientLabel] = Sees;
        return settings;
    }

    /// <summary>What an owner of <paramref name="vault"/> answers its endpoint with, recording each request in <paramref name="audit"/>.</summary>
    internal static SessionAuthority Over(Vault vault, ApproverHandler handler, AuditLog audit) =>
        new(VaultIdentity.Of(Path.GetDirectoryName(vault.Path)!, vault.Path), () => _lifetime, handler, () => audit);

    private static EntryExposure Everything() =>
        EntryExposure.TryCreate(["**"], out var exposure, out var error) ? exposure : throw new InvalidOperationException(error);
}
