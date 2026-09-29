using System.Globalization;
using System.Text;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What the first run offers from KeePassXC's own settings: the databases it last opened that still
/// exist, the last active first, each once, and nothing from a file it cannot read.
/// </summary>
public sealed class KeePassXcDatabasesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "keypaste-keepassxc-tests", Guid.NewGuid().ToString("n"));

    private string Ini => Path.Combine(_directory, "keepassxc.ini");

    public KeePassXcDatabasesTests() => Directory.CreateDirectory(_directory);

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
    public void No_file_offers_nothing()
    {
        Assert.Empty(KeePassXcDatabases.Read(Ini));
        Assert.Empty(KeePassXcDatabases.Read(null));
    }

    [Fact]
    public void The_last_active_comes_first_then_the_open_ones_then_the_recent_ones_each_once()
    {
        var work = Vault("work.kdbx");
        var home = Vault("home.kdbx");
        var old = Vault("old.kdbx");

        Write(
            "[General]",
            $"LastDatabases={Qt(old)}, {Qt(home)}, {Qt(work)}",
            $"LastActiveDatabase={Qt(work.Replace('\\', '/'))}",
            $"LastOpenedDatabases={Qt(home)}, {Qt(work)}");

        Assert.Equal([work, home, old], KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void A_database_deleted_since_is_not_offered()
    {
        var kept = Vault("kept.kdbx");
        var gone = Path.Combine(_directory, "gone.kdbx");

        Write("[General]", $"LastDatabases={Qt(gone)}, {Qt(kept)}");

        Assert.Equal([kept], KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void A_malformed_line_costs_that_line_and_the_rest_are_kept()
    {
        var active = Vault("active.kdbx");
        var recent = Vault("recent.kdbx");
        var text = string.Join(
            '\n',
            "[General",
            "this line is not a setting",
            $"LastActiveDatabase={Qt(active)}",
            "=no key",
            $"LastDatabases={Qt(recent)}");
        var bytes = Encoding.UTF8.GetBytes(text).ToList();
        bytes.InsertRange(bytes.IndexOf((byte)'\n') + 1, [0xC3, 0x28, (byte)'\n']);
        File.WriteAllBytes(Ini, [.. bytes]);

        Assert.Equal([active, recent], KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void Only_the_general_section_is_read()
    {
        var general = Vault("general.kdbx");
        var other = Vault("other.kdbx");

        Write($"LastActiveDatabase={Qt(general)}", "[GUI]", $"LastDatabases={Qt(other)}");

        Assert.Equal([general], KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void QSettings_escapes_quotes_and_types_are_read_as_QSettings_writes_them()
    {
        var comma = Vault("a, b.kdbx");
        var accented = Vault("café.kdbx");

        Write(
            "[General]",
            "LastDir=@Variant(\\0\\0\\0\\x7f\\0\\0\\0\\x18)",
            "LastOpenedDatabases=@Invalid()",
            $"LastDatabases=\"{Qt(comma)}\", {Qt(accented)}, @ByteArray(abc)");

        Assert.Equal([comma, accented], KeePassXcDatabases.Read(Ini));
        Assert.Equal(["@work.kdbx"], KeePassXcDatabases.Parse("LastDatabases=@@work.kdbx"u8));
    }

    [Fact]
    public void A_relative_path_is_not_offered()
    {
        Vault("work.kdbx");
        Write("[General]", "LastDatabases=work.kdbx");

        Assert.Empty(KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void At_most_ten_are_offered()
    {
        var vaults = Enumerable.Range(0, 12).Select(i => Vault(string.Create(CultureInfo.InvariantCulture, $"v{i}.kdbx"))).ToList();

        Write("[General]", "LastDatabases=" + string.Join(", ", vaults.Select(Qt)));

        Assert.Equal(vaults.Take(KeePassXcDatabases.Capacity), KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void A_file_larger_than_a_settings_file_offers_nothing()
    {
        var vault = Vault("work.kdbx");
        Write("[General]", $"LastDatabases={Qt(vault)}", "; " + new string('x', KeePassXcDatabases.MaximumBytes));

        Assert.Empty(KeePassXcDatabases.Read(Ini));
    }

    [Fact]
    public void Reading_changes_nothing_in_KeePassXCs_file()
    {
        Write("[General]", $"LastDatabases={Qt(Vault("work.kdbx"))}");
        var before = File.ReadAllBytes(Ini);
        var written = File.GetLastWriteTimeUtc(Ini);

        KeePassXcDatabases.Read(Ini);

        Assert.Equal(before, File.ReadAllBytes(Ini));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Ini));
    }

    [Theory]
    [InlineData("Windows", @"C:\Users\ana", @"C:\Users\ana\AppData\Local", null, @"C:\Users\ana\AppData\Local|KeePassXC|keepassxc.ini")]
    [InlineData("MacOS", "/Users/ana", "", null, "/Users/ana|Library|Caches|KeePassXC|keepassxc.ini")]
    [InlineData("Linux", "/home/ana", "", null, "/home/ana|.cache|keepassxc|keepassxc.ini")]
    [InlineData("Linux", "/home/ana", "", "/tmp/cache", "/tmp/cache|keepassxc|keepassxc.ini")]
    [InlineData("Linux", "/home/ana", "", "relative/cache", "/home/ana|.cache|keepassxc|keepassxc.ini")]
    public void KeePassXC_2_7s_local_settings_are_where_it_keeps_them(
        string platform, string home, string localAppData, string? xdgCacheHome, string expected) =>
        Assert.Equal(
            Path.Combine(expected.Split('|')),
            KeePassXcDatabases.LocalConfigPath(Enum.Parse<KeePassXcPlatform>(platform), home, localAppData, xdgCacheHome));

    [Fact]
    public void No_home_means_no_settings_file()
    {
        Assert.Null(KeePassXcDatabases.LocalConfigPath(KeePassXcPlatform.Windows, "", "", null));
        Assert.Null(KeePassXcDatabases.LocalConfigPath(KeePassXcPlatform.Linux, "", "", "relative"));
    }

    private string Vault(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    private void Write(params string[] lines) => File.WriteAllText(Ini, string.Join("\r\n", lines) + "\r\n");

    /// <summary>A string as Qt 5's QSettings writes it without a codec: backslashes doubled and anything past ASCII as <c>\x</c>.</summary>
    private static string Qt(string text)
    {
        var escaped = new StringBuilder();
        var hexNext = false;

        foreach (var ch in text)
        {
            if (hexNext && Uri.IsHexDigit(ch))
            {
                escaped.Append(CultureInfo.InvariantCulture, $"\\x{(int)ch:x}");
                continue;
            }

            hexNext = false;

            if (ch == '\\')
            {
                escaped.Append(@"\\");
            }
            else if (ch > 0x7E)
            {
                escaped.Append(CultureInfo.InvariantCulture, $"\\x{(int)ch:x}");
                hexNext = true;
            }
            else
            {
                escaped.Append(ch);
            }
        }

        return escaped.ToString();
    }
}
