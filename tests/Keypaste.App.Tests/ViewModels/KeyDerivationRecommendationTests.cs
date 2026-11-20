using System.Text;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Security suggests strengthening a vault whose key derivation is weaker than keypaste's, says who can no longer open it
/// before it saves, keeps a copy of the file it replaces, and remembers a dismissal on this machine.
/// </summary>
public sealed class KeyDerivationRecommendationTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;
    private readonly string _old;

    public KeyDerivationRecommendationTests()
    {
        _old = Path.Combine(_fixture.Home, "old.kdbx");
        KeePassInterop.WriteForeignUnchecked(_old, Encoding.UTF8.GetBytes(TempVault.Password), null, "AES-KDF", "AES-256");
        _session = new AppVaultSession(_clock, home: _fixture.Home);
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void A_vault_at_keypastes_key_derivation_has_nothing_to_strengthen()
    {
        using var recommendations = Unlocked(_fixture.Path_);

        Assert.False(recommendations.HasWeakKeyDerivation);
    }

    [Fact]
    public void An_AES_KDF_vault_is_offered_Strengthen_naming_what_it_uses_now_and_after()
    {
        using var recommendations = Unlocked(_old);

        Assert.True(recommendations.HasWeakKeyDerivation);
        Assert.True(recommendations.OffersStrengthen);
        Assert.Equal("Now AES-KDF. Strengthen changes it to Argon2d (memory 1 MiB, iterations 1).", recommendations.KeyDerivationDetails);
    }

    [Fact]
    public async Task Strengthen_asks_first_saying_old_KeePass_cannot_open_the_result_then_saves_keeping_a_copy()
    {
        using var recommendations = Unlocked(_old);
        var review = recommendations.NeedsReview;

        recommendations.AskStrengthenCommand.Execute(null);

        Assert.True(recommendations.ConfirmsStrengthen);
        Assert.False(recommendations.OffersStrengthen);
        Assert.Contains("KeePass versions before 2.35", recommendations.StrengthenConfirmation, StringComparison.Ordinal);
        Assert.Contains("KeePassXC", recommendations.StrengthenConfirmation, StringComparison.Ordinal);
        Assert.Equal(KeyDerivationFunction.AesKdf, VaultKeyDerivation.Read(_old)?.Function);

        await recommendations.StrengthenAsync();

        Assert.Equal(Strings.StrengthenedThisVaultsPasswordProtection, recommendations.Message);
        Assert.False(recommendations.HasWeakKeyDerivation);
        Assert.Equal(review - 1, recommendations.NeedsReview);
        Assert.Equal(VaultKeyDerivation.Keypastes, VaultKeyDerivation.Read(_old));
        Assert.Equal(KeyDerivationFunction.AesKdf, VaultKeyDerivation.Read(Assert.Single(VaultBackups.List(_old)).Path)?.Function);
    }

    [Fact]
    public void Cancel_leaves_the_vault_as_it_is()
    {
        using var recommendations = Unlocked(_old);
        var file = File.ReadAllBytes(_old);

        recommendations.AskStrengthenCommand.Execute(null);
        recommendations.CancelStrengthenCommand.Execute(null);

        Assert.True(recommendations.OffersStrengthen);
        Assert.Equal(file, File.ReadAllBytes(_old));
    }

    [Fact]
    public void A_dismissal_outlives_the_unlock_and_review_again_brings_it_back()
    {
        int review;

        using (var recommendations = Unlocked(_old))
        {
            review = recommendations.NeedsReview;
            recommendations.DismissKeyDerivationCommand.Execute(null);

            Assert.True(recommendations.KeyDerivationDismissed);
            Assert.False(recommendations.OffersStrengthen);
            Assert.Equal(review - 1, recommendations.NeedsReview);
        }

        using var again = new RecommendationsViewModel(_session, _fixture.Home);
        Assert.True(again.KeyDerivationDismissed);
        Assert.Equal(review - 1, again.NeedsReview);

        again.ReviewKeyDerivationAgainCommand.Execute(null);

        Assert.False(again.KeyDerivationDismissed);
        Assert.Equal(review, again.NeedsReview);
    }

    private RecommendationsViewModel Unlocked(string path)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(path, master.Value));
        return new RecommendationsViewModel(_session, _fixture.Home);
    }
}
