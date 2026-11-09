using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The app never opens or runs an attachment: the two files that reach one hand nothing to another program.
/// </summary>
/// <remarks>
/// The app's <c>BannedSymbols.txt</c> keeps attachments in these two files and the launcher and the opener in one file each,
/// and its bans hold in these files outside the attachment calls, so <c>Process.Start</c> or <c>IWebLauncher.OpenAsync</c> fails the build.
/// What stays here is what no ban expresses: core's launch, tool and link code and the app's own <c>Action&lt;Uri&gt;</c> and opener
/// delegates, which other app files use for other reasons, so a ban list per project cannot close them for two files alone,
/// and a P/Invoke of <c>ShellExecute</c>. A name in a comment counts too.
/// </remarks>
public sealed partial class AttachmentSourceRulesTests
{
    private static readonly string[] _attachmentFiles = ["EntryAttachmentRow.cs", "EntryAttachmentsViewModel.cs"];

    /// <summary>A way to hand something to another program.</summary>
    [GeneratedRegex(@"Launch|Process|ShellExecute|Opener|NavigateUri|\bUri\b|Core\.Clients|Core\.Linking|Terminal")]
    private static partial Regex HandingOn();

    /// <summary>The code that reaches an attachment names no launcher, process, address or core code that starts a program.</summary>
    /// <remarks>The mutation that must fail it: an Open button on an attachment, or a saved copy shown in its folder.</remarks>
    [Fact]
    public void The_attachment_files_hand_nothing_to_another_program()
    {
        foreach (var file in _attachmentFiles)
        {
            Assert.Empty(HandingOn().Matches(Read(file)).Select(match => $"{file}: {match.Value}"));
        }
    }

    [Fact]
    public void A_launch_added_to_an_attachment_file_is_found() =>
        Assert.NotEmpty(HandingOn().Matches(Read(_attachmentFiles[1]) + "\n_launching.Opener(saved, wait);"));

    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.App", "ViewModels", file));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.app.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));

            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.app.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
