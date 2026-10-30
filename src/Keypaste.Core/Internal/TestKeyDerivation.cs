namespace Keypaste.Core.Internal;

/// <summary>
/// Lets a test assembly create vaults whose Argon2 costs one pass over 1 MiB rather than <see cref="KdbxFormat"/>'s, so
/// thousands of derivations do not saturate the runner (D-0469).
/// </summary>
/// <remarks>
/// A vault keeps the cost it was created with, so this reaches only creation. Only the assemblies Core's internals are
/// visible to can set it, and none of them ships.
/// </remarks>
internal static class TestKeyDerivation
{
    internal const ulong Iterations = 1;

    internal const ulong Memory = 1024 * 1024;

    private static readonly AsyncLocal<bool> _production = new();

    /// <summary>Set once, by a test assembly's module initializer.</summary>
    internal static bool Cheap { get; set; }

    /// <summary>Whether a vault this flow creates now takes the token cost.</summary>
#pragma warning disable RS0030 // The one read of the switch it bans everywhere else.
    internal static bool Applies => Cheap && !_production.Value;
#pragma warning restore RS0030

    /// <summary>Gives vaults this async flow creates the production cost until disposed.</summary>
    internal static IDisposable Production()
    {
        _production.Value = true;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => _production.Value = false;
    }
}
