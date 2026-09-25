using System.Reflection;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A project's keys against its profiles, compared by value and holding none.</summary>
public sealed class EnvMatrixTests : IDisposable
{
    internal const string Shared = "shared-value-sentinel";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-matrix-").FullName;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly Vault _vault;

    public EnvMatrixTests() => _vault = Seeded(_directory);

    /// <summary>
    /// <c>acme-api</c> with dev, staging and prod: SENTRY_DSN only in prod, STRIPE_KEY the same in dev
    /// and prod, and staging's JWT_SIGNING_KEY expired on 2026-09-01.
    /// </summary>
    internal static Vault Seeded(string directory)
    {
        var vault = Vault.Create(Path.Combine(directory, "vault.kdbx"), EnvStoreTests.MasterPassword);
        var store = new EnvStore(vault);

        store.TrySet("acme-api", "DATABASE_URL", "dev-db", out _);
        store.TrySet("acme-api", "STRIPE_KEY", Shared, out _);
        store.TrySet("acme-api", "prod", "DATABASE_URL", "prod-db", out _);
        store.TrySet("acme-api", "prod", "STRIPE_KEY", Shared, out _);
        store.TrySet("acme-api", "prod", "SENTRY_DSN", "prod-sentry", out _);
        store.TrySet("acme-api", "staging", "DATABASE_URL", "staging-db", out _);
        store.TrySet("acme-api", "staging", "JWT_SIGNING_KEY", "staging-jwt", out _);
        vault.SetExpiryUnchecked(new EntryName("env/acme-api/staging", "JWT_SIGNING_KEY"), new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        return vault;
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void States_SetMissingUnusable()
    {
        var matrix = EnvMatrix.Build(_vault, "acme-api", _clock);

        Assert.Equal(["DATABASE_URL", "JWT_SIGNING_KEY", "SENTRY_DSN", "STRIPE_KEY"], matrix.Rows.Select(row => row.Key));

        var sentry = matrix.Row("SENTRY_DSN")!;
        Assert.Equal([EnvCellState.Missing, EnvCellState.Missing, EnvCellState.Set], sentry.Cells.Select(cell => cell.State));

        var jwt = matrix.Row("JWT_SIGNING_KEY")!.Cells[1];
        Assert.Equal(EnvCellState.Unusable, jwt.State);
        Assert.Equal("staging", jwt.Profile);
        Assert.Equal("expired 2026-09-01 00:00:00Z", jwt.Problem);

        Assert.Null(matrix.Row("ABSENT"));
        Assert.Empty(EnvMatrix.Build(_vault, "absent", _clock).Rows);
    }

    [Fact]
    public void SameValueAcrossProfiles_IsFlagged()
    {
        var matrix = EnvMatrix.Build(_vault, "acme-api", _clock);

        var stripe = matrix.Row("STRIPE_KEY")!;
        Assert.Equal(["prod"], stripe.Cells[0].SameValueAs);
        Assert.Equal(["dev"], stripe.Cells[2].SameValueAs);
        Assert.All(matrix.Row("DATABASE_URL")!.Cells, cell => Assert.Empty(cell.SameValueAs));
    }

    [Fact]
    public void NoRecordHasAValueMember()
    {
        foreach (var type in new[] { typeof(EnvMatrix), typeof(EnvMatrixRow), typeof(EnvCell), typeof(EnvProfileInfo), typeof(EnvDiffLine) })
        {
            var members = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(property => property.Name)
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => field.Name));

            foreach (var member in members)
            {
                Assert.DoesNotMatch("(?i)(^|[<_])(value|values|password|secret)(>|$)", member);
            }
        }

        var matrix = EnvMatrix.Build(_vault, "acme-api", _clock);
        Assert.DoesNotContain(Shared, string.Concat(matrix.Rows.SelectMany(row => row.Cells).Select(cell => cell + string.Join(",", cell.SameValueAs))), StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileOrder()
    {
        var matrix = EnvMatrix.Build(_vault, "acme-api", _clock);

        Assert.Equal(["dev", "staging", "prod"], matrix.Profiles.Select(profile => profile.Name));
        Assert.All(matrix.Rows, row => Assert.Equal(["dev", "staging", "prod"], row.Cells.Select(cell => cell.Profile)));
    }
}
