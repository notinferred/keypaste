using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What <c>--selftest</c> promises the three-OS packaging job.
/// </summary>
public sealed class SelfTestTests
{
    [Fact]
    public void It_succeeds_and_says_so_on_stdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = SelfTest.Run(stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("selftest ok", stdout.ToString(), StringComparison.Ordinal);
        Assert.Empty(stderr.ToString());
    }

    /// <summary>
    /// The flag the workflow passes and the flag <c>Program.Main</c> matches must be the same
    /// string. A rename on one side would leave the packaging job running the real app on a
    /// machine with no display, which hangs rather than fails.
    /// </summary>
    [Fact]
    public void The_flag_is_the_one_the_workflow_passes() =>
        Assert.Equal("--selftest", Program.SelfTestFlag);
}
