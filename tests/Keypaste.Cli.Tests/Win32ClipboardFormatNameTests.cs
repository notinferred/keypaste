using Keypaste.Cli.Clipboard;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The one assertion that cannot be made by reading the code: what Windows actually registered.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because of a shipped bug in KeePassXC.</b> Every released version — 2.7.10,
/// 2.7.11 and 2.7.12 — passes <c>"CanUploadToCloudClipboard "</c> with a trailing space to
/// <c>RegisterClipboardFormat</c>, which silently registers a different and meaningless format.
/// It is fixed on their develop branch and in no release. Nothing about that literal looks wrong
/// in a diff, and no test that merely checks the format "was set" would catch it, because a
/// format genuinely was — just not the one that does anything (O-0008).
/// </para>
/// <para>
/// So the name is round-tripped through <c>GetClipboardFormatName</c> instead of trusted. That
/// needs the real Win32 API, which is why this is the only clipboard test here that cannot run on
/// Linux. <see cref="WindowsClipboardWriterTests"/> holds everything that can.
/// </para>
/// </remarks>
public sealed class Win32ClipboardFormatNameTests
{
    [Fact]
    public void Every_opt_out_format_registers_under_the_name_we_passed()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Not a silent pass: there is no clipboard here to ask, and asserting anything about
            // one would be inventing a result. V-0012 says so, and calls itself BLOCKED off
            // Windows rather than green.
            return;
        }

        var win32 = new Win32Clipboard();

        foreach (var name in WindowsClipboardWriter.OptOutFormatNames)
        {
            var id = win32.RegisterFormat(name);
            Assert.NotEqual(0u, id);

            // The whole point. A trailing space, a typo or a case change survives review and
            // dies here.
            Assert.Equal(name, win32.GetFormatName(id));
        }
    }

    [Fact]
    public void A_name_with_a_trailing_space_registers_as_a_different_format()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Proving the check above can actually fire, by reproducing KeePassXC's defect on
        // purpose. If Windows folded the trailing space away, the round-trip assertion would be
        // vacuous and would pass against the broken literal too.
        var win32 = new Win32Clipboard();

        var correct = win32.RegisterFormat("CanUploadToCloudClipboard");
        var defective = win32.RegisterFormat("CanUploadToCloudClipboard ");

        Assert.NotEqual(correct, defective);
        Assert.Equal("CanUploadToCloudClipboard ", win32.GetFormatName(defective));
    }
}
