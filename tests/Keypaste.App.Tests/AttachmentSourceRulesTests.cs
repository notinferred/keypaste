using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The app never opens or runs an attachment (D-0534), held where only a look at the source can hold it.
/// </summary>
/// <remarks>
/// An attachment reaches another program only if the app hands it one: through Avalonia's launcher, a process it starts,
/// a button that navigates, or core's launch, tool and link code. These hold that the code which reaches an attachment
/// names none of them, and that the only launcher left in the app takes a web address in files that never reach one.
/// </remarks>
public sealed partial class AttachmentSourceRulesTests
{
    /// <summary>A call that reaches an item's attachments, or a type that carries one.</summary>
    [GeneratedRegex(@"\.(Attachments|AddAttachment|ReplaceAttachment|RemoveAttachment|SaveAttachment)\(|\bEntryAttachment\b|\bAttachmentSaveOutcome\b")]
    private static partial Regex AttachmentData();

    /// <summary>A way to hand something to another program.</summary>
    [GeneratedRegex(@"Launch|Process|ShellExecute|System\.Diagnostics|IWebLauncher|OpenAsync|Opener|NavigateUri|\bUri\b|Core\.Clients|Core\.Linking|Terminal")]
    private static partial Regex HandingOn();

    /// <summary>
    /// The item pane's two attachment files are the only app code that reaches an attachment.
    /// </summary>
    /// <remarks>The mutation that must fail it: an attachment read, listed or saved from any other file, such as the pane's own view model, which holds the web launcher.</remarks>
    [Fact]
    public void Attachments_are_reached_only_from_the_panes_attachment_files()
    {
        var reaching = AppSources("*.cs")
            .Where(file => AttachmentData().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["EntryAttachmentRow.cs", "EntryAttachmentsViewModel.cs"], reaching);
    }

    /// <summary>The code that reaches an attachment names no launcher, process, address or core code that starts a program.</summary>
    /// <remarks>The mutation that must fail it: an Open button on an attachment, or a saved copy shown in its folder.</remarks>
    [Fact]
    public void The_attachment_files_hand_nothing_to_another_program()
    {
        foreach (var file in AppSources("*.cs").Where(file => AttachmentData().IsMatch(File.ReadAllText(file))))
        {
            Assert.Empty(HandingOn().Matches(File.ReadAllText(file)).Select(match => $"{Path.GetFileName(file)}: {match.Value}"));
        }
    }

    /// <summary>
    /// Nothing in the app opens a file or folder in another program or navigates from a button, and its one launcher and
    /// its one process each live in one file, which takes a web address.
    /// </summary>
    /// <remarks>The mutation that must fail it: a <c>LaunchFileInfoAsync</c> anywhere, a second <c>Process.Start</c>, or a <c>HyperlinkButton</c>'s <c>NavigateUri</c>.</remarks>
    [Fact]
    public void The_app_hands_another_program_only_a_web_address()
    {
        var sources = AppSources("*.cs").Concat(AppSources("*.axaml")).ToList();

        Assert.Empty(Containing(sources, "LaunchFile", "LaunchDirectory", "NavigateUri"));
        Assert.Equal(["PlatformWebLauncher.cs"], Containing(sources, "LaunchUriAsync"));
        Assert.Equal(["OpenerProcess.cs"], Containing(sources, "Process.Start", "ProcessStartInfo"));
    }

    private static List<string> Containing(IEnumerable<string> files, params string[] needles) =>
        [.. files
            .Where(file => needles.Any(needle => File.ReadAllText(file).Contains(needle, StringComparison.Ordinal)))
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> AppSources(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Keypaste.App"), pattern, SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

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
