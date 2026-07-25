using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The argument parser, tested on its own: these are fast and touch no vault, so they carry no
/// Argon2 cost and can be exhaustive where the command tests cannot.
/// </summary>
public sealed class CommandLineTests
{
    private static readonly OptionSpec[] _spec =
    [
        new("vault", TakesValue: true),
        new("show", TakesValue: false),
    ];

    [Fact]
    public void UnknownOption_IsRejected()
    {
        Assert.False(CommandLine.TryParse(["get", "--nope"], 1, _spec, out _, out var error));
        Assert.Contains("--nope", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueOption_WithoutAValue_IsRejected()
    {
        Assert.False(CommandLine.TryParse(["get", "--vault"], 1, _spec, out _, out var error));
        Assert.Contains("needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FlagGivenAValue_IsRejected()
    {
        Assert.False(CommandLine.TryParse(["get", "--show=yes"], 1, _spec, out _, out var error));
        Assert.Contains("does not take a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateOption_IsRejected()
    {
        Assert.False(CommandLine.TryParse(["get", "--vault", "a", "--vault", "b"], 1, _spec, out _, out _));
    }

    [Fact]
    public void EqualsForm_AndSpaceForm_AreEquivalent()
    {
        Assert.True(CommandLine.TryParse(["get", "--vault=a"], 1, _spec, out var inline, out _));
        Assert.True(CommandLine.TryParse(["get", "--vault", "a"], 1, _spec, out var spaced, out _));

        Assert.Equal("a", inline.Value("vault"), StringComparer.Ordinal);
        Assert.Equal("a", spaced.Value("vault"), StringComparer.Ordinal);
    }

    /// <summary>
    /// A value is consumed positionally even when it looks like an option, so passwords and
    /// notes beginning with dashes are expressible without an escape.
    /// </summary>
    [Fact]
    public void ValueOption_AcceptsAValueThatLooksLikeAnOption()
    {
        Assert.True(CommandLine.TryParse(["get", "--vault", "--weird"], 1, _spec, out var line, out _));

        Assert.Equal("--weird", line.Value("vault"), StringComparer.Ordinal);
    }

    [Fact]
    public void DoubleDash_TreatsTheRestAsOperands()
    {
        Assert.True(CommandLine.TryParse(["get", "--", "--show"], 1, _spec, out var line, out _));

        Assert.False(line.HasFlag("show"));
        Assert.Equal(["--show"], line.Operands);
    }

    [Fact]
    public void Help_IsRecognisedInBothForms()
    {
        Assert.True(CommandLine.TryParse(["get", "-h"], 1, _spec, out var shortForm, out _));
        Assert.True(CommandLine.TryParse(["get", "--help"], 1, _spec, out var longForm, out _));

        Assert.True(shortForm.WantsHelp);
        Assert.True(longForm.WantsHelp);
    }

    [Fact]
    public void Operands_KeepTheirOrder()
    {
        Assert.True(CommandLine.TryParse(["add", "one", "--show", "two"], 1, _spec, out var line, out _));

        Assert.Equal(["one", "two"], line.Operands);
        Assert.True(line.HasFlag("show"));
    }
}
