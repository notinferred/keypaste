using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Whether a file a command read is still the file a command is about to delete.
/// </summary>
/// <remarks>
/// The cases that matter are the ones a path comparison alone gets wrong — a file rewritten where
/// it stands — and the ones a digest alone gets wrong — a different file of identical content put
/// in its place. Everything <c>keypaste env pull</c> does with the answer rests on both.
/// </remarks>
public sealed class SourceSnapshotTests : IDisposable
{
    private readonly string _directory;

    public SourceSnapshotTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-source-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AnUntouchedFile_Matches()
    {
        var (path, snapshot) = Written("A=1\n");

        Assert.True(snapshot.Matches(path));
    }

    [Fact]
    public void AnEmptyFile_Matches()
    {
        var (path, snapshot) = Written(string.Empty);

        Assert.True(snapshot.Matches(path));
    }

    /// <summary>One byte is the whole point: a variable appended is a variable never imported.</summary>
    [Fact]
    public void AFileWithOneByteAdded_DoesNotMatch()
    {
        var (path, snapshot) = Written("A=1\n");
        File.WriteAllText(path, "A=1\nB=2\n");

        Assert.False(snapshot.Matches(path));
    }

    [Fact]
    public void TheSameContentAtAnotherPath_DoesNotMatch()
    {
        var (_, snapshot) = Written("A=1\n");
        var twin = Path.Combine(_directory, "twin.env");
        File.WriteAllText(twin, "A=1\n");

        Assert.False(snapshot.Matches(twin));
    }

    [Fact]
    public void AnUnnormalisedSpellingOfTheSameFile_Matches()
    {
        var (path, snapshot) = Written("A=1\n");
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));

        Assert.True(snapshot.Matches(Path.Combine(_directory, "sub", "..", Path.GetFileName(path))));
    }

    [Fact]
    public void AMissingFile_DoesNotMatch()
    {
        var (path, snapshot) = Written("A=1\n");
        File.Delete(path);

        Assert.False(snapshot.Matches(path));
    }

    [Fact]
    public void AnUnchangedFile_IsDeleted_AndLeavesNothingBesideIt()
    {
        var (path, snapshot) = Written("A=1\n");

        Assert.Equal(SourceCleanup.Deleted, snapshot.DeleteIfUnchanged(path, out var detail));
        Assert.Null(detail);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    /// <summary>
    /// The file must come back exactly as the second writer left it. Renaming it away to check it
    /// is keypaste's business; a caller who declines to delete must be unable to tell it happened.
    /// </summary>
    [Fact]
    public void AChangedFile_IsPutBackWhereItWas_ByteForByte()
    {
        var (path, snapshot) = Written("A=1\n");
        File.WriteAllText(path, "A=1\nB=2\n");

        Assert.Equal(SourceCleanup.Changed, snapshot.DeleteIfUnchanged(path, out var detail));
        Assert.Null(detail);
        Assert.Equal("A=1\nB=2\n", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void AFileSomethingElseRemoved_IsReportedMissing_RatherThanDeleted()
    {
        var (path, snapshot) = Written("A=1\n");
        File.Delete(path);

        Assert.Equal(SourceCleanup.Missing, snapshot.DeleteIfUnchanged(path, out _));
    }

    /// <summary>
    /// The case a digest cannot see. Both files hold the same bytes, so only the recorded path
    /// distinguishes them — and it has to be the path as it resolved when the bytes were read,
    /// not as it resolves now, or the link resolves both spellings to the same answer and the
    /// question stops being asked.
    /// </summary>
    [Fact]
    public void APathReplacedByALinkElsewhere_DoesNotMatch_AndTheTargetSurvives()
    {
        var (path, snapshot) = Written("A=1\n");
        var target = Path.Combine(_directory, "target.env");
        File.WriteAllText(target, "A=1\n");

        File.Delete(path);
        Link(path, target);

        Assert.False(snapshot.Matches(path));
        Assert.Equal(SourceCleanup.Changed, snapshot.DeleteIfUnchanged(path, out _));
        Assert.True(File.Exists(target));
        Assert.Equal("A=1\n", File.ReadAllText(target));
    }

    /// <summary>
    /// The branch where the removal itself fails after the file is already held under a temporary
    /// name. Both the temporary name and its contents are reported rather than dropped, because a
    /// file keypaste has moved and cannot delete is still the user's data and still plaintext.
    /// </summary>
    /// <remarks>
    /// Windows only, and not for convenience: the read-only attribute is what makes
    /// <c>File.Delete</c> refuse there, while on Linux and macOS deletion is governed by the
    /// directory and an unwritable file is removed without complaint. There is no portable way to
    /// make one file undeletable, so this runs where the mechanism exists.
    /// </remarks>
    [Fact]
    public void AFileThatCannotBeDeleted_IsReportedWhereItWasLeft()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("on this platform the read-only attribute does not prevent deletion.");
        }

        var (path, snapshot) = Written("A=1\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            Assert.Equal(SourceCleanup.Stranded, snapshot.DeleteIfUnchanged(path, out var detail));

            Assert.NotNull(detail);
            Assert.True(File.Exists(detail));
            Assert.Equal("A=1\n", File.ReadAllText(detail));
            Assert.False(File.Exists(path));
        }
        finally
        {
            foreach (var left in Directory.GetFiles(_directory))
            {
                File.SetAttributes(left, FileAttributes.Normal);
            }
        }
    }

    /// <summary>
    /// The negative control. Without it every assertion above is satisfied by a snapshot that
    /// answers "changed" to everything and a cleanup that never deletes.
    /// </summary>
    [Fact]
    public void TheChecksAreNotVacuous()
    {
        var (path, snapshot) = Written("A=1\n");

        Assert.True(snapshot.Matches(path));

        File.WriteAllText(path, "A=2\n");
        Assert.False(snapshot.Matches(path));

        File.WriteAllText(path, "A=1\n");
        Assert.True(snapshot.Matches(path));
        Assert.Equal(SourceCleanup.Deleted, snapshot.DeleteIfUnchanged(path, out _));
    }

    /// <summary>The digest is of the bytes, not of anything a reader made of them.</summary>
    [Fact]
    public void ARewriteInADifferentEncoding_DoesNotMatch()
    {
        var (path, snapshot) = Written("A=1\n");
        File.WriteAllText(path, "A=1\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        Assert.False(snapshot.Matches(path));
    }

    private (string Path, SourceSnapshot Snapshot) Written(string contents, string name = ".env")
    {
        var path = Path.Combine(_directory, name);
        var bytes = Encoding.UTF8.GetBytes(contents);
        File.WriteAllBytes(path, bytes);

        return (path, SourceSnapshot.Take(path, bytes));
    }

    private static void Link(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }
    }
}
