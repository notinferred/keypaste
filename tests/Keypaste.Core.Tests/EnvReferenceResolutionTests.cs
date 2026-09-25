using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A reference file resolves whole or not at all, each value under the file's name for it, and a refusal holds no value (D-0349).</summary>
public sealed class EnvReferenceResolutionTests : IDisposable
{
    private const string _devDb = "postgres://dev-sentinel@db/app";
    private const string _stagingDb = "postgres://staging-sentinel@db/app";
    private const string _awsUser = "aws-user-sentinel";
    private const string _tokenVerifier = "verifier-sentinel";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-reference-resolution-").FullName;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly Vault _vault;

    public EnvReferenceResolutionTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            var store = new EnvStore(created);
            store.TrySet("acme-api", "DATABASE_URL", _devDb, out _);
            store.TrySet("acme-api", "staging", "DATABASE_URL", _stagingDb, out _);
            store.TrySet("acme-api", "staging", "STRIPE_KEY", "sk_staging_sentinel", out _);
            created.AddEntry(new VaultEntry { GroupPath = "work", Title = "aws", Username = _awsUser, Password = "aws-password-sentinel" });
            created.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Tokens, Title = "t1", Password = _tokenVerifier });
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AllOrNothing_OneMissingKeyRefusesTheWhole()
    {
        var resolved = Resolve("DATABASE_URL=kp://acme-api/staging/DATABASE_URL\nMISSING=kp://acme-api/staging/NOT_THERE\n");

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        var problem = Assert.Single(resolved.Problems);
        Assert.Equal("MISSING", problem.Key);
        Assert.Equal("(kp://acme-api/staging/NOT_THERE) NOT_THERE is not in this profile's set", problem.Reason);
        AssertNoValue(resolved);
    }

    [Fact]
    public void RenamedVariables_GetTheReferencedValues()
    {
        var resolved = Resolve("DB=kp://acme-api/staging/DATABASE_URL\nDEV_DB=kp://acme-api/dev/DATABASE_URL\nPROXY=http://proxy:3128\n");

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal(
            [new EnvVariable("DB", _stagingDb), new EnvVariable("DEV_DB", _devDb), new EnvVariable("PROXY", "http://proxy:3128")],
            resolved.Variables);
        Assert.Equal("acme-api", resolved.Project);
        Assert.Equal(EnvReferenceResolution.MixedProfile, resolved.Profile);
    }

    [Fact]
    public void EntryReference_ReleasesTheNamedField()
    {
        var resolved = Resolve("AWS_USER=kp:///work/aws#username\nAWS_PASSWORD=kp:///work/aws\n");

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal([new EnvVariable("AWS_USER", _awsUser), new EnvVariable("AWS_PASSWORD", "aws-password-sentinel")], resolved.Variables);
        Assert.Equal(EnvReferenceResolution.MixedProject, resolved.Project);
    }

    [Fact]
    public void ReservedGroupReference_IsRefused()
    {
        var resolved = Resolve($"T=kp:///{ReservedGroups.Tokens}/t1\n");

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Contains("keeps for itself", Assert.Single(resolved.Problems).Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(_tokenVerifier, resolved.Refusal + string.Concat(resolved.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedOnDisk_Refuses()
    {
        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            new EnvStore(other).TrySet("acme-api", "staging", "DATABASE_URL", "elsewhere", out _);
            other.Save();
        }

        var resolved = Resolve("DB=kp://acme-api/staging/DATABASE_URL\n");

        Assert.Equal(EnvOutcome.ChangedOnDisk, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public void EmptyField_Refuses()
    {
        var resolved = Resolve("AWS_URL=kp:///work/aws#url\n");

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Equal("(kp:///work/aws#url) has an empty url", Assert.Single(resolved.Problems).Reason);
    }

    [Fact]
    public void An_unusable_profile_refuses_every_reference_to_it()
    {
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api/staging", Title = "BAD-NAME", Password = "x" });
        _vault.Save();

        var resolved = Resolve("DB=kp://acme-api/staging/DATABASE_URL\n");

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Contains("'env/acme-api/staging' cannot be used: BAD-NAME", Assert.Single(resolved.Problems).Reason, StringComparison.Ordinal);
        AssertNoValue(resolved);
    }

    [Fact]
    public void Apply_maps_a_released_subset_onto_the_files_names_and_literals()
    {
        var document = Document("DB=kp://acme-api/staging/DATABASE_URL\nPROXY=http://proxy:3128\n");
        var released = EnvResolution.Resolve(_vault, "acme-api", "staging", ["DATABASE_URL"], _clock);

        var applied = EnvReferenceResolution.Apply(document, released);

        Assert.Equal([new EnvVariable("DB", _stagingDb), new EnvVariable("PROXY", "http://proxy:3128")], applied.Variables);
        Assert.Equal("staging", applied.Profile);

        var otherProfile = EnvResolution.Resolve(_vault, "acme-api", "dev", ["DATABASE_URL"], _clock);
        Assert.Equal(EnvOutcome.Unusable, EnvReferenceResolution.Apply(document, otherProfile).Outcome);
    }

    private EnvResolved Resolve(string text) => EnvReferenceResolution.Resolve(_vault, Document(text), _clock);

    private static EnvReferenceDocument Document(string text)
    {
        Assert.True(EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes(text), out var document));
        return document;
    }

    private static void AssertNoValue(EnvResolved resolved)
    {
        var said = resolved.Refusal + string.Concat(resolved.Problems.Select(problem => problem.Key + problem.Reason));

        foreach (var value in new[] { _devDb, _stagingDb, "sk_staging_sentinel", _awsUser, "aws-password-sentinel" })
        {
            Assert.DoesNotContain(value, said, StringComparison.Ordinal);
        }
    }
}
