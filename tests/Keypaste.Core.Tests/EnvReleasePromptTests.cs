using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What a person reads before a run gets a set: the command as it will start, whole, and nothing drawn that was not sent.</summary>
public sealed class EnvReleasePromptTests
{
    private static readonly EnvPreview _preview = new("dev", ["A", "B"]);

    [Fact]
    public void Arguments_with_spaces_or_quotes_are_quoted_and_an_ordinary_command_is_not_marked_altered()
    {
        var prompt = EnvReleasePrompt.For(_preview, ["C:\\Tools\\run.exe", "a b", "say \"hi\"", string.Empty, "--x=|>"], "C:\\work");

        Assert.Equal("C:\\Tools\\run.exe \"a b\" \"say \\\"hi\\\"\" \"\" --x=|>", prompt.Command);
        Assert.False(prompt.CommandWasAltered);
        Assert.Equal("C:\\work", prompt.Directory);
        Assert.False(prompt.DirectoryWasAltered);
    }

    [Fact]
    public void A_control_or_bidi_character_is_scrubbed_and_said_to_be()
    {
        var prompt = EnvReleasePrompt.For(_preview, ["echo", "safe\u202Eexe.txt", "line\nbreak"], "/tmp/\u200Bwork");

        Assert.DoesNotContain('\u202E', prompt.Command);
        Assert.DoesNotContain('\n', prompt.Command);
        Assert.True(prompt.CommandWasAltered);
        Assert.True(prompt.DirectoryWasAltered);
    }

    [Theory]
    [InlineData("", "echo", "/d")]
    [InlineData("a/b", "echo", "/d")]
    [InlineData("dev", "", "/d")]
    [InlineData("dev", "echo", "")]
    public void A_request_that_cannot_be_shown_has_a_problem(string project, string program, string directory)
    {
        Assert.NotNull(EnvReleasePrompt.Problem(project, program.Length == 0 ? [] : [program], directory));
    }

    [Fact]
    public void A_command_one_character_over_the_limit_has_a_problem_and_one_at_it_does_not()
    {
        var atLimit = new string('a', EnvReleasePrompt.MaximumCommandLength - 5);

        Assert.Null(EnvReleasePrompt.Problem("dev", ["echo", atLimit], "/d"));
        Assert.NotNull(EnvReleasePrompt.Problem("dev", ["echo", atLimit + "a"], "/d"));
    }
}
