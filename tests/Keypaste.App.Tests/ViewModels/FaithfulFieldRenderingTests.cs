using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the detail pane draws is what the vault holds, punctuation and line breaks included.
/// </summary>
/// <remarks>
/// <para>
/// <b>The counterweight to <see cref="HostileNameRenderingTests"/>, and it is a counterweight in the
/// strict sense</b>: that file holds the line that deceptive characters never reach a screen, and
/// this one holds the line that protecting against them is not a licence to rewrite somebody's
/// data. Both are true of the same three members at once, which is the only reason
/// <see cref="DisplayTextSanitizer"/> exists as a rule separate from
/// <see cref="EntryNameSanitizer"/>.
/// </para>
/// <para>
/// <b>Every case here was red before 4.9.</b> The pane drew <c>Username</c>, <c>Url</c> and
/// <c>Notes</c> through the name rule, which replaces ten structural characters and every control
/// character with a space, so <c>https://example.test/path?a=b#c</c> reached the screen as
/// <c>https: example.test path?a=b#c</c>, a note's line breaks and brackets were flattened, and a
/// Windows login lost its backslash (docs/ui-review.md).
/// </para>
/// </remarks>
public sealed class FaithfulFieldRenderingTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private const string _url = "https://example.test/path?a=b#c";
    private const string _username = @"DOMAIN\user";

    /// <summary>
    /// Authentication instructions and a configuration example, which is what notes actually hold.
    /// </summary>
    private const string _notes = "Step 1: open <https://example.test/admin>\nStep 2: paste\n\n[server]\n\tport = 8080\n\tpath = /var/lib";

    private static readonly string _bidi = ((char)0x202E).ToString();
    private static readonly string _zwsp = ((char)0x200B).ToString();

    // Built from code points rather than written as escapes: the C# lexer turns a unicode escape
    // for either of these into a real line terminator, even inside a comment, which ends the line
    // it is written on. Found by doing it.
    private static readonly string _lineSeparator = ((char)0x2028).ToString();
    private static readonly string _paragraphSeparator = ((char)0x2029).ToString();

    private readonly string _directory;
    private readonly string _vaultPath;

    public FaithfulFieldRenderingTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-faithful-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);

        vault.AddEntry(new VaultEntry
        {
            Title = "ordinary",
            Username = _username,
            Url = _url,
            Notes = _notes,
            Password = "p",
            GroupPath = "servers",
        });

        // The same fields, hostile. Drawn by the same members, so one pane cannot be faithful for
        // one entry and unprotected for the next.
        vault.AddEntry(new VaultEntry
        {
            Title = "hostile",
            Username = "user" + _bidi + "name",
            Url = "https://example.test/" + _zwsp + "path",
            Notes = "note" + _bidi + "s" + _lineSeparator + "and" + _paragraphSeparator + "more",
            Password = "p",
            GroupPath = "servers",
        });

        // An entry with nothing in these fields at all: the pane used to draw the literal text
        // "(unnamed)" under the URL heading, because the name rule substitutes a placeholder.
        vault.AddEntry(new VaultEntry { Title = "bare", Password = "p", GroupPath = "servers" });

        vault.Save();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_url_keeps_its_slashes_query_and_fragment()
    {
        using var context = New();

        Assert.Equal(_url, Detail(context, "ordinary").DisplayUrl);
    }

    [Fact]
    public void A_windows_login_keeps_its_backslash()
    {
        using var context = New();

        Assert.Equal(_username, Detail(context, "ordinary").DisplayUsername);
    }

    [Fact]
    public void Notes_keep_their_line_breaks_brackets_and_indentation()
    {
        using var context = New();

        Assert.Equal(_notes, Detail(context, "ordinary").DisplayNotes);
    }

    [Fact]
    public void A_field_nobody_filled_in_draws_nothing_rather_than_a_placeholder()
    {
        using var context = New();
        var detail = Detail(context, "bare");

        Assert.Equal(string.Empty, detail.DisplayUrl);
        Assert.Equal(string.Empty, detail.DisplayNotes);
        Assert.Equal(string.Empty, detail.DisplayUsername);
    }

    /// <summary>
    /// The other half, without which the three above would pass for a pane that drew raw text.
    /// </summary>
    [Fact]
    public void Text_that_can_misrepresent_itself_is_still_refused_in_the_same_fields()
    {
        using var context = New();
        var detail = Detail(context, "hostile");

        foreach (var drawn in new[] { detail.DisplayUsername, detail.DisplayUrl, detail.DisplayNotes })
        {
            Assert.DoesNotContain(_bidi, drawn, StringComparison.Ordinal);
            Assert.DoesNotContain(_zwsp, drawn, StringComparison.Ordinal);
            Assert.DoesNotContain(_lineSeparator, drawn, StringComparison.Ordinal);
            Assert.DoesNotContain(_paragraphSeparator, drawn, StringComparison.Ordinal);
        }

        // And the vault still holds what it held: these seed the edit drafts and the clipboard.
        Assert.Contains(_bidi, detail.Username, StringComparison.Ordinal);
        Assert.Contains(_zwsp, detail.Url, StringComparison.Ordinal);
    }

    private static EntryDetailViewModel Detail(Context context, string title)
    {
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == title);
        return context.Entries.Detail!;
    }

    private Context New() => new(_vaultPath);

    /// <summary>An unlocked session and the entries screen over it, mirroring EntriesViewModelTests.</summary>
    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock());
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal FakeClipboard Clipboard { get; }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
