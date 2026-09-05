using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What <c>--selftest</c> and <c>--version</c> promise the three-OS packaging job.
/// </summary>
public sealed class SelfTestTests
{
    [Fact]
    public void It_creates_a_vault_reads_an_entry_back_and_cleans_up()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var before = ScratchDirectories();

        var exit = SelfTest.Run(stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("selftest ok", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("read back", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
        Assert.Equal(before, ScratchDirectories());
    }

    /// <summary>
    /// The flag the workflow passes and the flag <c>Program.Main</c> matches must be the same
    /// string. A rename on one side would leave the packaging job running the real app on a
    /// machine with no display, which hangs rather than fails.
    /// </summary>
    [Fact]
    public void The_flags_are_the_ones_the_workflow_passes()
    {
        Assert.Equal("--selftest", Program.SelfTestFlag);
        Assert.Equal("--version", Program.VersionFlag);
    }

    [Fact]
    public void Version_prints_the_core_version_and_nothing_else()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var handled = Program.TryRunHeadless(["--version"], stdout, stderr, out var exit);

        Assert.True(handled);
        Assert.Equal(0, exit);
        Assert.Equal(CoreInfo.Version, stdout.ToString().Trim());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Selftest_is_handled_headless()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var handled = Program.TryRunHeadless(["--selftest"], stdout, stderr, out var exit);

        Assert.True(handled);
        Assert.Equal(0, exit);
    }

    /// <summary>
    /// Anything else reaches the window. A flag that was silently swallowed here would be a launch
    /// that draws nothing and exits 0, which is the failure a packaging job cannot see.
    /// </summary>
    [Fact]
    public void Ordinary_arguments_are_not_handled_headless()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Assert.False(Program.TryRunHeadless([], stdout, stderr, out _));
        Assert.False(Program.TryRunHeadless(["vault.kdbx"], stdout, stderr, out _));
        Assert.Empty(stdout.ToString());
    }

    private static string[] ScratchDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "keypaste-selftest-*").Order(StringComparer.Ordinal).ToArray();
}
