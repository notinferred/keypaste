using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ownership;

namespace Keypaste.Mcp.Tests;

/// <summary>Puts a real handler behind the session check a vault's owner applies.</summary>
internal static class TestSession
{
    /// <summary>The session every test owner holds.</summary>
    internal const string Id = "test-session";

    private static readonly SessionLifetime _lifetime = new(Id);

    /// <summary>What an owner of <paramref name="vault"/> answers its endpoint with.</summary>
    internal static SessionAuthority Over(Vault vault, ApproverHandler handler) =>
        new(VaultIdentity.Of(Path.GetDirectoryName(vault.Path)!, vault.Path), () => _lifetime, handler);
}
