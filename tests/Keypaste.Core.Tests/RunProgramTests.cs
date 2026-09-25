using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The program and directory a person approves are the ones that start: a bare name is looked up on
/// <c>PATH</c> only, a relative one against the directory shown, and a link in the directory is
/// resolved before anybody is asked (D-0358).
/// </summary>
public sealed class RunProgramTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-program-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Program(string directory, string name)
    {
        var folder = Path.Combine(_root, directory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "#!/bin/sh\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    [Fact]
    public void ABareNameInTheBridgesDirectory_IsNotRun()
    {
        var planted = Program("project", "npm");
        var real = Program("bin", "npm");

        Assert.True(RunProgram.TryResolve("npm", Path.GetDirectoryName(planted)!, Path.GetDirectoryName(real), null, out var program, out _));
        Assert.Equal(real, program);

        var bridge = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
        Assert.False(RunProgram.TryResolve(bridge, _root, Path.Combine(_root, "empty"), null, out _, out var error));
        Assert.Contains("PATH", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelativeEntryOnPath_IsNeverSearched()
    {
        Program("project", "tool");

        Assert.False(RunProgram.TryResolve("tool", Path.Combine(_root, "project"), "." + Path.PathSeparator + "project", null, out _, out _));
    }

    [Fact]
    public void ARelativePath_IsTakenAgainstTheDirectoryShown()
    {
        var script = Program("project", "deploy");
        var named = "./" + Path.GetFileName(script);

        Assert.True(RunProgram.TryResolve(named, Path.Combine(_root, "project"), null, null, out var program, out _));
        Assert.Equal(script, program);
    }

    [Fact]
    public void OnWindows_ABatchShim_IsFound_InPathextOrder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "extensions are Windows' rule");

        var folder = Path.Combine(_root, "node");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "npm.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "npm.ps1"), "write-host");

        Assert.True(RunProgram.TryResolve("npm", _root, folder, ".COM;.EXE;.BAT;.CMD;.PS1", out var program, out _));
        Assert.Equal(Path.Combine(folder, "npm.cmd"), program, ignoreCase: true);
    }

    [Fact]
    public void AMissingDirectory_IsNotResolved()
    {
        Assert.False(RunProgram.TryResolveDirectory(Path.Combine(_root, "absent"), out _, out _));
        Assert.True(RunProgram.TryResolveDirectory(_root, out var resolved, out _));
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void ALinkInTheDirectory_IsResolvedToWhereItPoints()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(Path.Combine(real, "api"));
        var link = Path.Combine(_root, "link");

        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this account cannot create a symbolic link: {ex.Message}");
        }

        Assert.True(RunProgram.TryResolveDirectory(Path.Combine(link, "api"), out var resolved, out _));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(RunProgram.TryResolveDirectory(real, out var target, out _) ? target : real, "api")),
            resolved);
    }

    [Fact]
    public void ALinkWhoseTargetPassesThroughAnotherLink_ResolvesToTheRealDirectory()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(Path.Combine(real, "api"));
        var hop = Path.Combine(_root, "hop");
        var deep = Path.Combine(_root, "deep");

        try
        {
            Directory.CreateSymbolicLink(hop, real);
            Directory.CreateSymbolicLink(deep, Path.Combine(hop, "api"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this account cannot create a symbolic link: {ex.Message}");
        }

        Assert.True(RunProgram.TryResolveDirectory(Path.Combine(real, "api"), out var direct, out _));
        Assert.True(RunProgram.TryResolveDirectory(deep, out var throughLinks, out _));
        Assert.Equal(direct, throughLinks);
    }
}
