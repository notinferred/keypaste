using Keypaste.Core.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A file replaced through <see cref="AtomicFile"/> holds the old contents or the new, and nothing is left beside it.</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-atomic-file-tests-").FullName;

    private string Target => Path.Combine(_directory, "file.toml");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_write_replaces_the_file_and_leaves_nothing_beside_it()
    {
        File.WriteAllText(Target, "old");

        AtomicFile.Write(Target, "new"u8);

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_staged_file_changes_nothing_until_it_is_committed_and_is_removed_when_it_is_not()
    {
        File.WriteAllText(Target, "old");

        using (AtomicFile.Stage(Target, "new"u8))
        {
            Assert.Equal("old", File.ReadAllText(Target));
            Assert.Equal(2, Directory.GetFiles(_directory).Length);
        }

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_directory));

        using (var staged = AtomicFile.Stage(Target, "new"u8))
        {
            staged.Commit();
            Assert.Throws<InvalidOperationException>(staged.Commit);
        }

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_new_commit_never_replaces_what_is_there()
    {
        File.WriteAllText(Target, "old");

        using (var staged = AtomicFile.Stage(Target, "new"u8))
        {
            Assert.ThrowsAny<IOException>(staged.CommitNew);
        }

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_directory));

        File.Delete(Target);

        using (var staged = AtomicFile.Stage(Target, "new"u8))
        {
            staged.CommitNew();
        }

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_move_that_fails_leaves_no_staged_copy()
    {
        Directory.CreateDirectory(Target);

        var refused = Assert.ThrowsAny<Exception>(() => AtomicFile.Write(Target, "new"u8));

        Assert.True(refused is IOException or UnauthorizedAccessException, refused.ToString());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(AtomicFile.OwnerOnly)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    public void The_file_has_exactly_the_mode_asked_for(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no Unix file mode; the file inherits its directory's ACL.");
            return;
        }

        File.WriteAllText(Target, "old");
        File.SetUnixFileMode(Target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);

        AtomicFile.Write(Target, "new"u8, mode);

        Assert.Equal(mode, File.GetUnixFileMode(Target));
    }
}
