using Xunit;

namespace Keypaste.App.Tests;

/// <summary>The Windows app relaunches itself with .NET's diagnostics port off only once, only as itself, and never over the person's own setting.</summary>
public sealed class DiagnosticsRelaunchTests
{
    private const string _installed = "C:/Users/person/AppData/Local/Programs/keypaste/keypaste-app.exe";

    [Fact]
    public void The_app_relaunches_when_the_variable_is_unset() => Assert.True(DiagnosticsRelaunch.IsNeeded(null, _installed));

    [Fact]
    public void The_app_relaunches_whatever_case_its_name_is_started_in() => Assert.True(DiagnosticsRelaunch.IsNeeded(null, _installed.ToUpperInvariant()));

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    public void A_relaunched_app_or_a_person_who_set_the_variable_is_left_as_is(string setting) =>
        Assert.False(DiagnosticsRelaunch.IsNeeded(setting, _installed));

    [Theory]
    [InlineData("C:/Program Files/dotnet/dotnet.exe")]
    [InlineData("C:/repo/artifacts/bin/Keypaste.App.Tests/debug/testhost.exe")]
    [InlineData(null)]
    public void A_host_running_the_app_is_not_relaunched(string? processPath) => Assert.False(DiagnosticsRelaunch.IsNeeded(null, processPath));

    [Fact]
    public void A_test_host_is_never_relaunched() => Assert.False(DiagnosticsRelaunch.IsNeeded(null, Environment.ProcessPath));
}
