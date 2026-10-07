using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// keypaste reads whether file locking is off exactly as the runtime does, so a claim is never granted
/// by a lock the runtime is not taking.
/// </summary>
public sealed class FileLockingTests
{
    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(false, null, "1", true)]
    [InlineData(false, null, "true", true)]
    [InlineData(false, null, " TRUE ", true)]
    [InlineData(false, null, "0", false)]
    [InlineData(false, null, "yes", false)]
    [InlineData(false, true, null, true)]
    [InlineData(false, false, "1", false)]
    [InlineData(true, true, "1", false)]
    public void Locking_is_off_exactly_when_the_runtime_turns_it_off(bool windows, bool? switchValue, string? variable, bool off) =>
        Assert.Equal(off, FileLocking.Disabled(windows, switchValue, variable));
}
