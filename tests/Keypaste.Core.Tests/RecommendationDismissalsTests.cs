using System.Security.Cryptography;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Dismissals are kept by vault, entry identifier and key as an HMAC under the vault's key, and a bad file dismisses nothing.</summary>
public sealed class RecommendationDismissalsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-dismissals-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private static readonly Dismissal _noteKey = new("0123456789abcdef", "a1b2c3d4e5f60718293a4b5c6d7e8f90", RecommendationDismissals.NoteKey, "STRIPE_SECRET_KEY");

    private string Path_ => Path.Combine(_directory, "recommendations.json");

    [Fact]
    public void A_dismissal_is_read_back_and_the_file_names_no_entry_kind_or_key()
    {
        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        dismissals.Dismiss([_noteKey], _key);
        Assert.True(dismissals.Save(Path_));

        Assert.True(RecommendationDismissals.TryLoad(Path_, out var read));
        Assert.True(read.IsDismissed(_noteKey, _key));
        Assert.False(read.IsDismissed(_noteKey with { Key = "OPENAI_API_KEY" }, _key));
        Assert.False(read.IsDismissed(_noteKey, null));

        var text = File.ReadAllText(Path_);
        Assert.DoesNotContain(_noteKey.Entry, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_noteKey.Kind, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_noteKey.Key, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dismissal_from_one_vault_never_hides_another_vaults_finding()
    {
        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        dismissals.Dismiss([_noteKey], _key);

        Assert.False(dismissals.IsDismissed(_noteKey with { Vault = "fedcba9876543210" }, RandomNumberGenerator.GetBytes(32)));
        Assert.False(dismissals.IsDismissed(_noteKey with { Vault = "fedcba9876543210" }, _key));
        Assert.False(dismissals.IsDismissed(_noteKey, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void An_earlier_files_dismissals_still_hold_and_are_kept_hidden_from_the_next_change()
    {
        var other = new Dismissal("fedcba9876543210", "b1b2c3d4e5f60718293a4b5c6d7e8f90", RecommendationDismissals.LostProjectTag, "billing@2026-01-01T00:00:00.0000000Z");
        File.WriteAllText(Path_, $$"""
            {"dismissed":[
              {"vault":"{{_noteKey.Vault}}","entry":"{{_noteKey.Entry}}","kind":"{{_noteKey.Kind}}","key":"{{_noteKey.Key}}"},
              {"vault":"{{other.Vault}}","entry":"{{other.Entry}}","kind":"{{other.Kind}}","key":"{{other.Key}}"}]}
            """);

        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        Assert.True(dismissals.IsDismissed(_noteKey, null));
        Assert.True(dismissals.IsDismissed(_noteKey, _key));

        dismissals.Dismiss([_noteKey with { Key = "OPENAI_API_KEY" }], _key);
        Assert.True(dismissals.Save(Path_));

        var text = File.ReadAllText(Path_);
        Assert.DoesNotContain(_noteKey.Key, text, StringComparison.Ordinal);
        Assert.Contains(other.Key, text, StringComparison.Ordinal);

        Assert.True(RecommendationDismissals.TryLoad(Path_, out var read));
        Assert.True(read.IsDismissed(_noteKey, _key));
        Assert.True(read.IsDismissed(_noteKey with { Key = "OPENAI_API_KEY" }, _key));
        Assert.True(read.IsDismissed(other, null));
    }

    [Fact]
    public void Review_again_lists_a_finding_however_it_was_kept()
    {
        File.WriteAllText(Path_, $$"""{"dismissed":[{"vault":"{{_noteKey.Vault}}","entry":"{{_noteKey.Entry}}","kind":"{{_noteKey.Kind}}","key":"{{_noteKey.Key}}"}]}""");
        var hidden = _noteKey with { Key = "OPENAI_API_KEY" };

        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        dismissals.Dismiss([hidden], _key);
        dismissals.ReviewAgain([_noteKey, hidden], _key);

        Assert.False(dismissals.IsDismissed(_noteKey, _key));
        Assert.False(dismissals.IsDismissed(hidden, _key));
    }

    [Fact]
    public void Review_again_lists_a_finding_the_file_holds_twice()
    {
        var row = $$"""{"vault":"{{_noteKey.Vault}}","entry":"{{_noteKey.Entry}}","kind":"{{_noteKey.Kind}}","key":"{{_noteKey.Key}}"}""";
        File.WriteAllText(Path_, $$"""{"dismissed":[{{row}},{{row}}]}""");

        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        dismissals.ReviewAgain([_noteKey], null);

        Assert.False(dismissals.IsDismissed(_noteKey, null));
    }

    [Fact]
    public void Saving_replaces_the_file_owner_only_and_leaves_nothing_beside_it()
    {
        Assert.True(RecommendationDismissals.TryLoad(Path_, out var dismissals));
        dismissals.Dismiss([_noteKey], _key);

        Assert.True(dismissals.Save(Path_));
        Assert.True(dismissals.Save(Path_));

        Assert.Equal([Path_], Directory.GetFiles(_directory));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path_));
        }
    }

    [Fact]
    public void A_missing_file_dismisses_nothing_and_can_be_written()
    {
        Assert.True(RecommendationDismissals.TryLoad(Path.Combine(_directory, "absent.json"), out var read));
        Assert.False(read.IsDismissed(_noteKey, _key));
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
        Assert.False(read.IsDismissed(_noteKey, _key));
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
        Assert.True(read.IsDismissed(new Dismissal("v", "e", "note-key", "K"), null));
    }
}
