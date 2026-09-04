using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What the settings file promises: it round-trips, and every way of failing to read it leaves a
/// timeout that still locks.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "keypaste-settings-tests", Guid.NewGuid().ToString("n"));

    private string SettingsFile => Path.Combine(_directory, "app.toml");

    public AppSettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_absent_file_reads_as_the_defaults()
    {
        var settings = AppSettings.Load(SettingsFile);

        Assert.Equal(AppSettings.Default, settings);
        Assert.Equal(300, settings.IdleTimeoutSeconds);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.False(settings.LockWhenMinimized);
    }

    [Fact]
    public void It_round_trips()
    {
        var written = AppSettings.Default with
        {
            IdleTimeoutSeconds = 900,
            Theme = AppTheme.Dark,
            LockWhenMinimized = true,
        };

        Assert.True(AppSettings.Save(SettingsFile, written));
        Assert.Equal(written, AppSettings.Load(SettingsFile));
    }

    [Theory]
    [InlineData(AppTheme.System)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Every_theme_round_trips(AppTheme theme)
    {
        AppSettings.Save(SettingsFile, AppSettings.Default with { Theme = theme });

        Assert.Equal(theme, AppSettings.Load(SettingsFile).Theme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_flag_round_trips_as_a_number(bool locked)
    {
        AppSettings.Save(SettingsFile, AppSettings.Default with { LockWhenMinimized = locked });

        var text = File.ReadAllText(SettingsFile);

        Assert.Contains($"lock_when_minimized = {(locked ? 1 : 0)}", text, StringComparison.Ordinal);
        Assert.Equal(locked, AppSettings.Load(SettingsFile).LockWhenMinimized);
    }

    /// <summary>
    /// The reason the flag is a number: the reader refuses <c>true</c> and <c>false</c> so that a
    /// policy file cannot say yes in a shape keypaste had to guess at.
    /// </summary>
    [Fact]
    public void The_file_it_writes_contains_no_true_or_false()
    {
        AppSettings.Save(SettingsFile, AppSettings.Default with { LockWhenMinimized = true });

        var text = File.ReadAllText(SettingsFile);

        Assert.DoesNotContain("true", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("false", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fail closed, and do not clobber. A file somebody is part-way through editing by hand must
    /// survive being read.
    /// </summary>
    [Fact]
    public void A_malformed_file_reads_as_the_defaults_and_is_left_alone()
    {
        const string Broken = "[[settings]]\nthis is not a pair at all\n";
        File.WriteAllText(SettingsFile, Broken);

        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsFile));
        Assert.Equal(Broken, File.ReadAllText(SettingsFile));
    }

    /// <summary>
    /// What a hand edit is most likely to get wrong, and the one case where the whole file has to
    /// go: past a syntax error nothing in it can be trusted to mean what it looks like.
    /// </summary>
    [Fact]
    public void A_boolean_written_as_true_voids_the_file_rather_than_being_guessed_at()
    {
        File.WriteAllText(
            SettingsFile,
            "[[settings]]\nidle_timeout_seconds = 900\nlock_when_minimized = true\n");

        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsFile));
    }

    /// <summary>
    /// The parser only accepts pairs inside a <c>[[section]]</c>, so a file written without the
    /// header is a parse error rather than a set of top-level settings.
    /// </summary>
    [Fact]
    public void Settings_outside_a_section_are_not_read()
    {
        File.WriteAllText(SettingsFile, "idle_timeout_seconds = 900\n");

        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsFile));
    }

    [Fact]
    public void A_file_with_no_settings_section_reads_as_the_defaults()
    {
        File.WriteAllText(SettingsFile, "[[something-else]]\nidle_timeout_seconds = 900\n");

        Assert.Equal(AppSettings.Default, AppSettings.Load(SettingsFile));
    }

    /// <summary>
    /// A person who set only the theme meant to set only the theme. This is not the malformed
    /// case, so the other two keys take their defaults on their own.
    /// </summary>
    [Fact]
    public void A_key_the_file_omits_takes_its_default()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\ntheme = \"dark\"\n");

        var settings = AppSettings.Load(SettingsFile);

        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.Equal(AppSettings.Default.IdleTimeoutSeconds, settings.IdleTimeoutSeconds);
        Assert.False(settings.LockWhenMinimized);
    }

    /// <summary>
    /// The clamp goes to the nearest end of the range and never to the far one. A file asking for
    /// no timeout at all gets the shortest one, which is the whole point of there being no "never".
    /// </summary>
    [Theory]
    [InlineData(0, AppSettings.MinimumIdleTimeoutSeconds)]
    [InlineData(1, AppSettings.MinimumIdleTimeoutSeconds)]
    [InlineData(59, AppSettings.MinimumIdleTimeoutSeconds)]
    [InlineData(60, 60)]
    [InlineData(900, 900)]
    [InlineData(28800, AppSettings.MaximumIdleTimeoutSeconds)]
    [InlineData(100000, AppSettings.MaximumIdleTimeoutSeconds)]
    public void An_idle_timeout_out_of_range_is_clamped_to_the_nearest_end(int written, int expected)
    {
        File.WriteAllText(
            SettingsFile,
            $"[[settings]]\nidle_timeout_seconds = {written}\n");

        Assert.Equal(expected, AppSettings.Load(SettingsFile).IdleTimeoutSeconds);
    }

    /// <summary>
    /// The clamp is a property of the record, not of the loader, so no settings screen and no
    /// <c>with</c> expression can produce a timeout that fails to lock.
    /// </summary>
    [Fact]
    public void A_timeout_out_of_range_cannot_be_constructed_either()
    {
        Assert.Equal(
            AppSettings.MinimumIdleTimeoutSeconds,
            (AppSettings.Default with { IdleTimeoutSeconds = 0 }).IdleTimeoutSeconds);
        Assert.Equal(
            AppSettings.MaximumIdleTimeoutSeconds,
            (AppSettings.Default with { IdleTimeoutSeconds = int.MaxValue }).IdleTimeoutSeconds);
    }

    /// <summary>
    /// A timeout that is not a number at all is not an out-of-range number: it falls back to the
    /// default rather than to either end of the range.
    /// </summary>
    [Fact]
    public void A_timeout_that_is_not_a_number_falls_back_to_the_default_and_not_to_the_maximum()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\nidle_timeout_seconds = \"forever\"\n");

        var settings = AppSettings.Load(SettingsFile);

        Assert.Equal(AppSettings.Default.IdleTimeoutSeconds, settings.IdleTimeoutSeconds);
        Assert.NotEqual(AppSettings.MaximumIdleTimeoutSeconds, settings.IdleTimeoutSeconds);
    }

    [Fact]
    public void An_unrecognised_theme_falls_back_to_the_system_one()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\ntheme = \"solarized\"\n");

        Assert.Equal(AppTheme.System, AppSettings.Load(SettingsFile).Theme);
    }

    [Fact]
    public void A_theme_written_in_capitals_is_still_read()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\ntheme = \"Dark\"\n");

        Assert.Equal(AppTheme.Dark, AppSettings.Load(SettingsFile).Theme);
    }

    [Fact]
    public void Saving_creates_the_directory_if_it_is_missing()
    {
        var nested = Path.Combine(_directory, "nested", "app.toml");

        Assert.True(AppSettings.Save(nested, AppSettings.Default));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void The_file_it_writes_is_readable_only_by_its_owner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only file mode. SECURITY.md states that gap rather than implying a mode keypaste never set.");
            return;
        }

        AppSettings.Save(SettingsFile, AppSettings.Default);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SettingsFile));
    }
}
