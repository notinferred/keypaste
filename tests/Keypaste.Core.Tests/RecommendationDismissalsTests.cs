using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>C.2: dismissals are kept by vault, entry identifier and key, and a bad file dismisses nothing.</summary>
public sealed class RecommendationDismissalsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-dismissals-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Dismissals_round_trip_and_hold_no_title_or_value()
    {
        var path = Path.Combine(_directory, "recommendations.json");
        Dismissal[] dismissals = [new("0123456789abcdef", "a1b2c3d4e5f60718293a4b5c6d7e8f90", RecommendationDismissals.NoteKey, "STRIPE_SECRET_KEY")];

        Assert.True(RecommendationDismissals.Save(path, dismissals));
        Assert.True(RecommendationDismissals.TryLoad(path, out var read));

        Assert.Equal(dismissals, read);
        Assert.DoesNotContain("sk_test", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_dismisses_nothing_and_can_be_written()
    {
        Assert.True(RecommendationDismissals.TryLoad(Path.Combine(_directory, "absent.json"), out var read));
        Assert.Empty(read);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"dismissed\": 3}")]
    public void A_malformed_file_is_not_read(string text)
    {
        var path = Path.Combine(_directory, "recommendations.json");
        File.WriteAllText(path, text);

        Assert.False(RecommendationDismissals.TryLoad(path, out var read));
        Assert.Empty(read);
    }

    [Fact]
    public void An_oversized_file_is_not_read()
    {
        var path = Path.Combine(_directory, "recommendations.json");
        File.WriteAllText(path, "{\"dismissed\":[]}" + new string(' ', RecommendationDismissals.MaximumBytes));

        Assert.False(RecommendationDismissals.TryLoad(path, out _));
    }

    [Fact]
    public void Incomplete_rows_are_skipped()
    {
        var path = Path.Combine(_directory, "recommendations.json");
        File.WriteAllText(path, "{\"dismissed\":[{\"vault\":\"v\",\"entry\":\"e\",\"kind\":\"note-key\"},{\"vault\":\"v\",\"entry\":\"e\",\"kind\":\"note-key\",\"key\":\"K\"}]}");

        Assert.True(RecommendationDismissals.TryLoad(path, out var read));
        Assert.Equal([new Dismissal("v", "e", "note-key", "K")], read);
    }
}
