using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Recommendations lists a sync tool's conflicted copy to review and merge, and the session rescues what is open when the file is lost.</summary>
public sealed class ConflictedCopyRecommendationTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session;

    public ConflictedCopyRecommendationTests()
    {
        _fixture.Sees();
        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    private string Copy => Path.Combine(Path.GetDirectoryName(_fixture.Path_)!, $"{Path.GetFileNameWithoutExtension(_fixture.Path_)} (conflicted copy).kdbx");

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task A_conflicted_copy_is_reviewed_then_merged_and_leaves_the_list()
    {
        File.Copy(_fixture.Path_, Copy);
        using var recommendations = new RecommendationsViewModel(_session, _fixture.Home);
        recommendations.LookForConflictedCopies();
        var row = Assert.Single(recommendations.ConflictedCopies);
        Assert.False(recommendations.IsEmpty);

        await recommendations.ReviewAsync(row);
        Assert.True(row.IsReviewed);

        await recommendations.MergeAsync(row);

        Assert.Empty(recommendations.ConflictedCopies);
        Assert.False(File.Exists(Copy));
    }

    [Fact]
    public void What_is_open_can_be_saved_back_when_the_file_is_gone()
    {
        File.Delete(_fixture.Path_);

        Assert.Null(_session.SaveOpenCopyOverFile());

        Assert.True(File.Exists(_fixture.Path_));
    }
}
