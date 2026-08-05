using System.Globalization;
using System.Text;
using Keypaste.Cli.Clipboard;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The Windows clipboard write, and the ordering that makes it worth anything (D-0056).
/// </summary>
/// <remarks>
/// <para>
/// <b>These run everywhere, deliberately.</b> The property under test is the order of the Win32
/// calls, not the calls themselves, and a Windows-only test would never execute in the Linux leg
/// of CI — which is where a reordering would land. The seam exists so this is checkable on any
/// machine; <see cref="Win32ClipboardFormatNameTests"/> covers what only Windows can answer.
/// </para>
/// <para>
/// The test that matters most is <see cref="Everything_goes_on_in_one_session"/>. Splitting the
/// write across two open/close sessions leaves every format genuinely set, every other assertion
/// here passing, and the password sitting in Win+V — because the notification Clipboard History
/// acts on is raised at <c>CloseClipboard</c>, so the first close has already recorded it.
/// </para>
/// </remarks>
public sealed class WindowsClipboardWriterTests
{
    private const uint CfUnicodeText = 13;

    [Fact]
    public void Everything_goes_on_in_one_session()
    {
        var win32 = new RecordingWin32();

        Assert.True(new WindowsClipboardWriter(win32).TrySet("sk_live_the_secret", out _));

        // One open and one close, and every write strictly between them.
        Assert.Equal(1, win32.Calls.Count(c => c == "open"));
        Assert.Equal(1, win32.Calls.Count(c => c == "close"));
        Assert.Equal(0, win32.Calls.IndexOf("open"));
        Assert.Equal(win32.Calls.Count - 1, win32.Calls.LastIndexOf("close"));
        Assert.Equal(1, win32.Calls.IndexOf("empty"));
    }

    [Fact]
    public void The_text_and_all_three_opt_outs_are_set()
    {
        var win32 = new RecordingWin32();

        Assert.True(new WindowsClipboardWriter(win32).TrySet("sk_live_the_secret", out _));

        Assert.Contains(CfUnicodeText, win32.Written.Keys);
        foreach (var name in WindowsClipboardWriter.OptOutFormatNames)
        {
            Assert.Contains(win32.IdOf(name), win32.Written.Keys);
        }
    }

    [Fact]
    public void The_text_is_null_terminated_utf16()
    {
        var win32 = new RecordingWin32();

        Assert.True(new WindowsClipboardWriter(win32).TrySet("hi", out _));

        // CF_UNICODETEXT means a null-terminated UTF-16 string. Omitting the terminator is the
        // classic way to paste the password plus whatever happened to follow it in memory.
        Assert.Equal(Encoding.Unicode.GetBytes("hi\0"), win32.Written[CfUnicodeText]);
    }

    [Fact]
    public void A_marker_that_fails_empties_the_clipboard_rather_than_leaving_the_secret()
    {
        // The text lands, then the exclusion marker fails. Leaving the clipboard as-is would be
        // the exact defect this class exists to prevent, arrived at through the error path.
        var win32 = new RecordingWin32
        {
            FailSetOfFormatNamed = WindowsClipboardWriter.OptOutFormatNames[0],
        };

        Assert.False(new WindowsClipboardWriter(win32).TrySet("sk_live_the_secret", out var error));

        Assert.Equal(2, win32.Calls.Count(c => c == "empty"));
        Assert.Equal("close", win32.Calls[^1]);
        Assert.Contains("excluded from history", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clipboard_another_program_holds_is_a_failure_not_a_silent_pass()
    {
        var win32 = new RecordingWin32 { FailOpen = true };

        Assert.False(new WindowsClipboardWriter(win32).TrySet("sk_live_the_secret", out var error));

        Assert.Empty(win32.Written);
        Assert.Contains("holding the clipboard", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_empties_rather_than_overwriting()
    {
        var win32 = new RecordingWin32();

        Assert.True(new WindowsClipboardWriter(win32).TryClear(out _));

        // Writing a blank value would put one more entry into the history we just opted out of.
        Assert.Empty(win32.Written);
        Assert.Equal(["open", "empty", "close"], win32.Calls);
    }

    [Fact]
    public void A_format_that_will_not_register_stops_the_write_before_it_opens()
    {
        var win32 = new RecordingWin32 { FailRegister = true };

        Assert.False(new WindowsClipboardWriter(win32).TrySet("sk_live_the_secret", out var error));

        Assert.Empty(win32.Calls);
        Assert.Contains("could not register", error, StringComparison.Ordinal);
    }

    private sealed class RecordingWin32 : IWin32Clipboard
    {
        private readonly Dictionary<string, uint> _ids = [];

        internal List<string> Calls { get; } = [];

        internal Dictionary<uint, byte[]> Written { get; } = [];

        internal bool FailOpen { get; init; }

        internal bool FailRegister { get; init; }

        internal string? FailSetOfFormatNamed { get; init; }

        internal uint IdOf(string name) => _ids[name];

        public uint RegisterFormat(string name)
        {
            if (FailRegister)
            {
                return 0;
            }

            // Ids start above CF_UNICODETEXT so a mix-up cannot pass by coincidence.
            if (!_ids.TryGetValue(name, out var id))
            {
                id = (uint)(1000 + _ids.Count);
                _ids[name] = id;
            }

            return id;
        }

        public string? GetFormatName(uint id)
        {
            foreach (var pair in _ids)
            {
                if (pair.Value == id)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        public bool Open()
        {
            if (FailOpen)
            {
                return false;
            }

            Calls.Add("open");
            return true;
        }

        public bool Empty()
        {
            Calls.Add("empty");
            Written.Clear();
            return true;
        }

        public bool SetData(uint format, byte[] data)
        {
            Calls.Add(string.Format(CultureInfo.InvariantCulture, "set:{0}", format));

            if (FailSetOfFormatNamed is not null
                && _ids.TryGetValue(FailSetOfFormatNamed, out var failing)
                && failing == format)
            {
                return false;
            }

            Written[format] = data;
            return true;
        }

        public bool Close()
        {
            Calls.Add("close");
            return true;
        }
    }
}
