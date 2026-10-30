using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The key-derivation cost a vault is created with: <see cref="KdbxFormat"/>'s for every vault keypaste ships, and a token
/// one only for vaults a test assembly creates (D-0469).
/// </summary>
public sealed class KeyDerivationCostTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-kdf-cost-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_vault_created_outside_a_test_derives_its_key_at_the_stated_production_cost()
    {
        var parameters = CreatedWith(TestKeyDerivation.Production);

        Assert.Equal(new Argon2Kdf(Argon2Type.D).Uuid, parameters.KdfUuid);
        Assert.Equal(KdbxFormat.Argon2Iterations, parameters.GetUInt64(Argon2Kdf.ParamIterations, 0));
        Assert.Equal(KdbxFormat.Argon2Memory, parameters.GetUInt64(Argon2Kdf.ParamMemory, 0));
        Assert.Equal(KdbxFormat.Argon2Parallelism, parameters.GetUInt32(Argon2Kdf.ParamParallelism, 0));
        Assert.Equal(KdbxFormat.Argon2Version, parameters.GetUInt32(Argon2Kdf.ParamVersion, 0));
        Assert.Equal(64UL * 1024 * 1024, KdbxFormat.Argon2Memory);
        Assert.Equal(2UL, KdbxFormat.Argon2Iterations);
        Assert.Equal(2U, KdbxFormat.Argon2Parallelism);
    }

    [Fact]
    public void A_vault_a_test_creates_derives_its_key_at_the_token_cost_with_the_same_algorithm()
    {
        var parameters = CreatedWith(() => null);

        Assert.Equal(new Argon2Kdf(Argon2Type.D).Uuid, parameters.KdfUuid);
        Assert.Equal(TestKeyDerivation.Iterations, parameters.GetUInt64(Argon2Kdf.ParamIterations, 0));
        Assert.Equal(TestKeyDerivation.Memory, parameters.GetUInt64(Argon2Kdf.ParamMemory, 0));
        Assert.Equal(KdbxFormat.Argon2Parallelism, parameters.GetUInt32(Argon2Kdf.ParamParallelism, 0));
    }

    /// <summary>Core's <c>BannedSymbols.txt</c> keeps every shipped file but its own switch from reaching it.</summary>
    [Fact]
    public void This_test_assembly_turns_the_token_cost_on() =>
        Assert.True(TestKeyDerivation.Cheap, "this assembly's module initializer did not turn the token cost on");

    private KdfParameters CreatedWith(Func<IDisposable?> scope)
    {
        var path = Path.Combine(_directory, Path.GetRandomFileName() + ".kdbx");
        using (scope())
        using (var vault = Vault.Create(path, _master))
        {
            vault.Save();
        }

        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(_master), false));
        PwDatabase database = new();
        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
            return database.KdfParameters;
        }
        finally
        {
            database.Close();
        }
    }
}
