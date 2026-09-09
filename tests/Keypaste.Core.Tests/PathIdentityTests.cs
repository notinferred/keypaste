using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// One answer to "do these two paths name the same file", and an honest account of what it cannot
/// reach.
/// </summary>
/// <remarks>
/// The cases that matter are the ones a lexical comparison gets wrong: a destination that does not
/// exist yet, and a link somewhere above it. Everything a caller does with the answer — refusing to
/// write a <c>.env</c> over a vault, above all — is only as good as those two.
/// </remarks>
public sealed class PathIdentityTests : IDisposable
{
    private readonly string _directory;

    public PathIdentityTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-path-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TheSamePath_IsTheSameFile()
    {
        var file = Touch("vault.kdbx");

        Assert.True(PathIdentity.SameFile(file, file));
    }

    [Fact]
    public void ADottedPath_IsTheSameFileAsItsNormalForm()
    {
        var file = Touch("vault.kdbx");
        var dotted = Path.Combine(_directory, "sub", "..", "vault.kdbx");
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));

        Assert.True(PathIdentity.SameFile(file, dotted));
    }

    /// <summary>
    /// Relative against absolute, without <c>Directory.SetCurrentDirectory</c>: the working
    /// directory is read to build the spelling, never changed, because it is process-global and
    /// other tests are running beside this one.
    /// </summary>
    [Fact]
    public void ARelativeSpelling_IsTheSameFileAsItsAbsoluteForm()
    {
        var file = Touch("vault.kdbx");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
        if (Path.IsPathRooted(relative))
        {
            Assert.Skip("the temporary directory is on another volume, so there is no relative spelling.");
        }

        Assert.True(PathIdentity.SameFile(relative, file));
    }

    [Fact]
    public void TwoDifferentFiles_AreNotTheSameFile()
    {
        Assert.False(PathIdentity.SameFile(Touch("vault.kdbx"), Touch("other.kdbx")));
    }

    [Fact]
    public void ATrailingSeparator_DoesNotMakeADifferentDirectory()
    {
        Assert.True(PathIdentity.SameFile(_directory, _directory + Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// The filesystem decides, not the operating system: a case-sensitive volume on macOS and a
    /// case-insensitive mount on Linux both exist, so the test asks the volume it is running on.
    /// </summary>
    [Fact]
    public void ACaseDifference_FollowsTheFileSystem()
    {
        var file = Touch("vault.kdbx");
        var shouted = Path.Combine(_directory, "VAULT.KDBX");

        Assert.Equal(CaseInsensitiveHere(), PathIdentity.SameFile(file, shouted));
    }

    [Fact]
    public void TheComparison_IsCaseSensitiveOnlyOnLinux()
    {
        Assert.Equal(
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
            PathIdentity.Comparison);
    }

    [Fact]
    public void ASymlink_IsTheSameFileAsItsTarget()
    {
        var file = Touch("vault.kdbx");
        var alias = Link(Path.Combine(_directory, "alias.kdbx"), file, directory: false);

        Assert.True(PathIdentity.SameFile(alias, file));
    }

    [Fact]
    public void AChainOfSymlinks_IsTheSameFileAsItsTarget()
    {
        var file = Touch("vault.kdbx");
        var first = Link(Path.Combine(_directory, "one.kdbx"), file, directory: false);
        var second = Link(Path.Combine(_directory, "two.kdbx"), first, directory: false);

        Assert.True(PathIdentity.SameFile(second, file));
    }

    [Fact]
    public void AFileUnderASymlinkedDirectory_IsTheSameFile()
    {
        var real = Directory.CreateDirectory(Path.Combine(_directory, "real")).FullName;
        var file = Path.Combine(real, "vault.kdbx");
        File.WriteAllText(file, string.Empty);
        var link = Link(Path.Combine(_directory, "link"), real, directory: true);

        Assert.True(PathIdentity.SameFile(Path.Combine(link, "vault.kdbx"), file));
    }

    /// <summary>
    /// The case the whole ancestor walk exists for. An export names a file that is not there yet,
    /// so there is nothing to resolve at the end of the path and everything to resolve above it.
    /// </summary>
    [Fact]
    public void ADestinationThatDoesNotExistYet_ResolvesThroughItsRealParent()
    {
        var real = Directory.CreateDirectory(Path.Combine(_directory, "real")).FullName;
        var link = Link(Path.Combine(_directory, "link"), real, directory: true);

        Assert.True(PathIdentity.SameFile(
            Path.Combine(link, ".env"),
            Path.Combine(real, ".env")));
    }

    [Fact]
    public void ALinkLoop_IsAnsweredWithoutHanging()
    {
        var left = Path.Combine(_directory, "left");
        var right = Path.Combine(_directory, "right");
        Link(left, right, directory: false);
        Link(right, left, directory: false);

        // The answer is only required to arrive. A path that loops is not a file anyone can open,
        // so "not the vault" is the safe half of a wrong answer.
        Assert.False(string.IsNullOrEmpty(PathIdentity.Canonical(left)));
    }

    [Fact]
    public void APathWhoseAncestorsDoNotExist_IsItsOwnLexicalForm()
    {
        var missing = Path.Combine("no", "such", "place", ".env");

        // Against the resolved fixture directory, not the spelling it was handed: macOS puts a
        // temporary directory under /var, which is a link to /private/var, so the deepest existing
        // ancestor legitimately resolves and only the part below it is the lexical claim here.
        Assert.Equal(
            Path.Combine(PathIdentity.Canonical(_directory), missing),
            PathIdentity.Canonical(Path.Combine(_directory, missing)));
    }

    [Fact]
    public void AMalformedPath_IsReturnedUnchanged()
    {
        const string Malformed = "\0not a path";

        Assert.Equal(Malformed, PathIdentity.Canonical(Malformed));
    }

    [Fact]
    public void AnEmptyPath_IsARejectedArgument()
    {
        Assert.Throws<ArgumentException>(() => PathIdentity.Canonical(string.Empty));
        Assert.Throws<ArgumentException>(() => PathIdentity.SameFile(string.Empty, "x"));
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>
    /// Creates a link, or skips. Windows needs Developer Mode or an elevated shell, and a machine
    /// that cannot make one must say so rather than pass a test it never ran.
    /// </summary>
    private static string Link(string path, string target, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create symbolic links: {ex.Message}");
        }

        return path;
    }

    private bool CaseInsensitiveHere()
    {
        var probe = Path.Combine(_directory, "case-probe");
        File.WriteAllText(probe, string.Empty);
        try
        {
            return File.Exists(Path.Combine(_directory, "CASE-PROBE"));
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
