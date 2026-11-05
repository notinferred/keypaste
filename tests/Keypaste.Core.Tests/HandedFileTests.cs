using System.Diagnostics;
using System.Text;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A path from a start's arguments or a second start is untrusted, and only a well-formed one for a whole file goes anywhere.</summary>
public sealed class HandedFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("keypaste-handed-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_start_names_its_first_argument_that_is_no_flag_against_its_own_directory()
    {
        Assert.Equal(Path.Combine(_folder, "a.kdbx"), HandedFile.FromArguments(["--background", "a.kdbx", "b.kdbx"], _folder));
        Assert.Null(HandedFile.FromArguments([], _folder));
        Assert.Null(HandedFile.FromArguments(["--background"], _folder));
        Assert.Null(HandedFile.FromArguments(["line\nbreak.kdbx"], _folder));
        Assert.Null(HandedFile.FromArguments(["nul\0.kdbx"], _folder));
    }

    [Fact]
    public void Only_a_well_formed_path_survives_a_request()
    {
        var path = Path.Combine(_folder, "ü.kdbx");

        Assert.Equal(path + "\n", Encoding.UTF8.GetString(HandedFile.Request(path)));
        Assert.Equal(path, HandedFile.FromRequest(Encoding.UTF8.GetBytes(path)));
        Assert.Equal("\n", Encoding.UTF8.GetString(HandedFile.Request(null)));
        Assert.Equal("\n", Encoding.UTF8.GetString(HandedFile.Request("relative.kdbx")));
        Assert.Equal("\n", Encoding.UTF8.GetString(HandedFile.Request(Path.Combine(_folder, new string('a', HandedFile.MaximumRequestBytes)))));
        Assert.Null(HandedFile.FromRequest([]));
        Assert.Null(HandedFile.FromRequest(Encoding.UTF8.GetBytes("relative.kdbx")));
        Assert.Null(HandedFile.FromRequest(Encoding.UTF8.GetBytes(path + "\u0007")));
        Assert.Null(HandedFile.FromRequest([0x2F, 0xC3, 0x28]));
    }

    [Fact]
    public void A_whole_file_has_something_in_it()
    {
        var full = Path.Combine(_folder, "full");
        var empty = Path.Combine(_folder, "empty");
        File.WriteAllText(full, "x");
        File.WriteAllText(empty, string.Empty);

        Assert.True(HandedFile.IsWholeFile(full));
        Assert.False(HandedFile.IsWholeFile(empty));
        Assert.False(HandedFile.IsWholeFile(_folder));
        Assert.False(HandedFile.IsWholeFile(Path.Combine(_folder, "missing")));
        Assert.Equal(1, HandedFile.LengthOf(full));
        Assert.Null(HandedFile.LengthOf(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void A_fifo_is_not_a_whole_file_itself_or_behind_a_link()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no FIFO in its file system");
        }

        var fifo = Path.Combine(_folder, "fifo");
        using (var made = Process.Start("mkfifo", [fifo]))
        {
            made.WaitForExit();
        }

        var link = Path.Combine(_folder, "link");
        File.CreateSymbolicLink(link, fifo);

        Assert.False(HandedFile.IsWholeFile(fifo));
        Assert.False(HandedFile.IsWholeFile(link));
    }

    [Fact]
    public async Task Asking_whether_a_fifo_is_a_vault_never_opens_it()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no FIFO in its file system");
        }

        var fifo = Path.Combine(_folder, "fifo.kdbx");
        using (var made = Process.Start("mkfifo", [fifo]))
        {
            made.WaitForExit();
        }

        Assert.False(await Task.Run(() => KdbxHeader.IsVaultFile(fifo), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"\\server\share\a.kdbx")]
    [InlineData("//server/share/a.kdbx")]
    [InlineData(@"/\server\share\a.kdbx")]
    [InlineData(@"\\?\UNC\server\share\a.kdbx")]
    [InlineData(@"\\?\unc\server\share\a.kdbx")]
    [InlineData("//?/UNC/server/share/a.kdbx")]
    [InlineData(@"\??\UNC\server\share\a.kdbx")]
    [InlineData(@"\\.\UNC\server\share\a.kdbx")]
    [InlineData(@"\\?\GLOBALROOT\Device\Mup\server\share\a.kdbx")]
    [InlineData(@"\\?\Volume{0d3f7a52-0000-0000-0000-100000000000}\a.kdbx")]
    [InlineData(@"\\.\C:\a.kdbx")]
    [InlineData(@"C:a.kdbx")]
    [InlineData(@"1:\a.kdbx")]
    public void A_windows_path_not_starting_at_a_drive_is_not_local(string path)
    {
        Assert.False(HandedFile.IsDriveRooted(path));
        Assert.Equal(!OperatingSystem.IsWindows(), HandedFile.IsLocal(path));
    }

    [Theory]
    [InlineData(@"C:\a.kdbx")]
    [InlineData("c:/a.kdbx")]
    [InlineData(@"\\?\C:\a.kdbx")]
    public void A_windows_path_starting_at_a_drive_is_local(string path)
    {
        Assert.True(HandedFile.IsDriveRooted(path));
        Assert.True(HandedFile.IsLocal(path));
    }

    [Fact]
    public void A_path_on_this_computer_is_local()
    {
        Assert.True(HandedFile.IsLocal(Path.Combine(_folder, "a.kdbx")));
    }
}
