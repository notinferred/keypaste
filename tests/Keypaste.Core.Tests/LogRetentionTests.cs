using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>How long the vault's owner keeps the agent log for, which only the person sets (SECURITY.md law 3).</summary>
public sealed class LogRetentionTests : IDisposable
{
    private readonly string _home =
        Path.Combine(Path.GetTempPath(), "keypaste-retention-tests", Guid.NewGuid().ToString("n"));

    private string SettingsFile => KeypasteHome.SettingsPath(_home);

    public LogRetentionTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void With_no_file_the_log_is_kept_a_year()
    {
        Assert.Equal(AuditRetention.DefaultDays, LogRetention.KeptDays(_home));
    }

    [Fact]
    public void The_period_the_person_set_is_kept()
    {
        File.WriteAllText(SettingsFile, "[[log]]\nkeep_days = 730\n");

        Assert.Equal(730, LogRetention.KeptDays(_home));
    }

    /// <summary>A file that stops parsing would otherwise read as a year, and remove days the person chose to keep for good.</summary>
    [Fact]
    public void A_file_that_cannot_be_read_removes_nothing()
    {
        File.WriteAllText(SettingsFile, "[[log]]\nkeep_days = 0\nthis is not a pair at all\n");

        Assert.Equal(AuditRetention.Forever, LogRetention.KeptDays(_home));
    }
}
