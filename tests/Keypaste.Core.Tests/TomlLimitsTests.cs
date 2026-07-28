using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// That making the parser's ceilings a parameter did not change what a policy file is.
/// </summary>
/// <remarks>
/// The whole risk of that change is that it silently loosened the policy path while nobody was
/// looking, so the assertion is written against the constants themselves rather than against a
/// remembered number.
/// </remarks>
public sealed class TomlLimitsTests
{
    [Fact]
    public void The_policy_limits_are_exactly_what_they_were()
    {
        Assert.Equal(Toml.MaximumBytes, TomlLimits.Policy.Bytes);
        Assert.Equal(Toml.MaximumLines, TomlLimits.Policy.Lines);
        Assert.Equal(Toml.MaximumLineLength, TomlLimits.Policy.LineLength);
        Assert.Equal(Toml.MaximumTables, TomlLimits.Policy.Tables);
        Assert.Equal(Toml.MaximumPairs, TomlLimits.Policy.Pairs);
        Assert.Equal(Toml.MaximumItems, TomlLimits.Policy.Items);
        Assert.Equal(Toml.MaximumStringLength, TomlLimits.Policy.StringLength);
    }

    /// <summary>The overload without limits must still be the policy one.</summary>
    [Fact]
    public void The_default_overload_still_enforces_the_policy_string_length()
    {
        var tooLong = new string('a', Toml.MaximumStringLength + 1);

        Assert.False(Toml.TryParse($"[[allow]]\nclient = \"{tooLong}\"\n", out _, out var error));
        Assert.Contains($"at most {Toml.MaximumStringLength} characters", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason the parameter exists: a Windows vault path is longer than a policy file's
    /// strings are allowed to be.
    /// </summary>
    [Fact]
    public void The_path_limits_accept_a_string_a_policy_file_would_refuse()
    {
        var longPath = "C:/Users/somebody/Documents/" + new string('d', 200) + "/vault.kdbx";

        Assert.False(Toml.TryParse($"[[vault]]\npath = \"{longPath}\"\n", out _, out _));
        Assert.True(Toml.TryParse(
            $"[[vault]]\npath = \"{longPath}\"\n",
            TomlLimits.Paths,
            out var document,
            out var error));

        Assert.Equal(string.Empty, error);
        Assert.Equal(longPath, document.Tables[0].Pairs[0].Value.Text);
    }

    /// <summary>
    /// Only sizes are adjustable. A caller cannot buy its way past a syntax rule, and the backslash
    /// refusal is the one that would otherwise have been tempting to relax for Windows paths.
    /// </summary>
    [Fact]
    public void Wider_limits_do_not_permit_a_backslash()
    {
        Assert.False(Toml.TryParse(
            "[[vault]]\npath = \"C:\\Users\\somebody\\vault.kdbx\"\n",
            TomlLimits.Paths,
            out _,
            out var error));

        Assert.Contains("backslash", error, StringComparison.Ordinal);
    }
}
