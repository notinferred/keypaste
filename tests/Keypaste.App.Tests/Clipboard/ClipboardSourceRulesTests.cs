using Keypaste.App.Clipboard;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// Two rules about the app's clipboard code that only a look at the source can hold.
/// </summary>
/// <remarks>
/// The same mechanism as <c>CompatGateIsPermanentTests</c> in <c>Keypaste.Core.Tests</c>, and for
/// the same reason: some properties are about where code is allowed to be rather than about what it
/// computes, and a reviewer noticing is not a gate.
/// </remarks>
public sealed class ClipboardSourceRulesTests
{
    /// <summary>
    /// Reading the clipboard happens in exactly one file, and it is the one that says why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// D-0011 refused to put a "give me the clipboard's text" member on the CLI's
    /// <c>IClipboard</c>, because it would pull the user's whole clipboard into a process holding an
    /// unlocked vault and give a future caller something to log. Avalonia's clipboard has that
    /// member anyway, and the equality guard genuinely needs a read-back, so the call is made once,
    /// hashed at once, and fenced by this.
    /// </para>
    /// <para>
    /// <b>The mutation that must fail it:</b> a second caller — a "paste into the search box"
    /// convenience, a diagnostic, an "is the copy still there?" check written inline rather than
    /// through <see cref="IAppClipboard"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_clipboard_is_read_in_exactly_one_place()
    {
        var offenders = new List<string>();
        var app = Path.Combine(RepoRoot(), "src", "Keypaste.App");

        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            if (File.ReadAllText(file).Contains("TryGetTextAsync", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.Equal(["AvaloniaClipboard.cs"], offenders);
    }

    /// <summary>
    /// The Windows clipboard-exclusion format names are spelled exactly, with nothing around them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written from a citation. O-0008 records KeePassXC shipping one of these names with a
    /// trailing space for three releases — 2.7.10 to 2.7.12 — which silently turned the opt-out off
    /// while the code carried on looking right. Its stated lesson is that a string literal passed to
    /// <c>RegisterClipboardFormat</c> cannot be checked by review.
    /// </para>
    /// <para>
    /// <b>What this does not prove:</b> that Windows honours them. That needs a real Windows session
    /// with Clipboard History switched on, so it is on docs/desktop.md's manual checklist instead.
    /// This holds the half that a typo breaks, which is the half that actually broke for somebody.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_exclusion_format_names_carry_no_stray_whitespace()
    {
        Assert.Equal(
            [
                "ExcludeClipboardContentFromMonitorProcessing",
                "CanIncludeInClipboardHistory",
                "CanUploadToCloudClipboard",
            ],
            AvaloniaClipboard.ExclusionFormats);

        foreach (var name in AvaloniaClipboard.ExclusionFormats)
        {
            Assert.Equal(name.Trim(), name);
            Assert.DoesNotContain(' ', name);
        }
    }

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
