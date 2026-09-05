using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the app draws from a vault must not be able to misrepresent itself.
/// </summary>
/// <remarks>
/// <para>
/// The CLI half of this is <c>HostileNameRenderingTests</c> in <c>Keypaste.Cli.Tests</c>, and the
/// argument is the same: titles and group paths are attacker-reachable, and
/// <see cref="EntryNameSanitizer"/> is the answer the repository already chose for every other
/// surface. The app reached it only on the write path — rejecting a name it would not create — and
/// never on the way back out.
/// </para>
/// <para>
/// <b>The payload is a bidi override and a zero-width space, not an ANSI escape.</b> A KDBX title is
/// stored in XML and U+001B is not a legal XML 1.0 character, so a control character cannot survive
/// the round trip. What survives is the trickery that makes a name read as something it is not.
/// Avalonia interprets no markup, so nothing here is code execution; it is a person being shown a
/// name that is not the one in the file.
/// </para>
/// <para>
/// <b>What these tests deliberately do not touch.</b> <c>Title</c>, <c>GroupPath</c> and <c>Path</c>
/// address the entry — <c>Find</c>, <c>RemoveEntry</c> and the search filter all use them — and
/// <c>Username</c>, <c>Url</c> and <c>Notes</c> seed the edit drafts and the clipboard. Sanitizing
/// any of those in place would write scrubbed text back into somebody's vault, or paste it. The
/// display members are separate for exactly that reason, and
/// <see cref="Title_is_left_alone_because_it_addresses_the_entry"/> holds that line.
/// </para>
/// </remarks>
public sealed class HostileNameRenderingTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly string _bidi = ((char)0x202E).ToString();
    private static readonly string _zwsp = ((char)0x200B).ToString();

    private readonly string _directory;
    private readonly string _vaultPath;

    public HostileNameRenderingTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-hostile-app-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry
        {
            Title = "prod" + _bidi + "token" + _zwsp,
            Username = "user" + _bidi + "name",
            Url = "https://example.test/" + _bidi,
            Notes = "note" + _zwsp + "s",
            Password = "p",
            GroupPath = "servers" + _bidi + "live",
        });
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

    private static void AssertSafe(string drawn)
    {
        Assert.DoesNotContain(_bidi, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain(_zwsp, drawn, StringComparison.Ordinal);
    }

    private EntryRow Row()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        var entry = vault.ReadEntries().Single();
        return new EntryRow(entry.Title, entry.GroupPath);
    }

    [Fact]
    public void The_group_column_does_not_draw_a_name_that_can_misrepresent_itself()
    {
        AssertSafe(Row().Where);
    }

    [Fact]
    public void The_sidebar_label_does_not_draw_a_name_that_can_misrepresent_itself()
    {
        using var vault = Vault.Open(_vaultPath, _master);

        foreach (var node in GroupNode.Flatten(vault.ReadGroupPaths()))
        {
            AssertSafe(node.Label);
        }
    }

    [Fact]
    public void The_entry_list_column_does_not_draw_a_title_that_can_misrepresent_itself()
    {
        AssertSafe(Row().DisplayTitle);
    }

    [Fact]
    public void The_detail_pane_draws_no_field_that_can_misrepresent_itself()
    {
        using var context = New();
        context.Entries.Selected = context.Entries.Rows.Single();
        var detail = context.Entries.Detail!;

        AssertSafe(detail.DisplayTitle);
        AssertSafe(detail.DisplayPath);
        AssertSafe(detail.DisplayUsername);
        AssertSafe(detail.DisplayUrl);
        AssertSafe(detail.DisplayNotes);

        // The counterweight: what the editor and the clipboard read is still what the vault holds.
        Assert.Contains(_bidi, detail.Username, StringComparison.Ordinal);
        Assert.Contains(_bidi, detail.Url, StringComparison.Ordinal);
        Assert.Contains(_zwsp, detail.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_is_left_alone_because_it_addresses_the_entry()
    {
        // The counterweight to every case above: sanitizing this would break Find, RemoveEntry and
        // the search filter, and there is a separate display member for what the screen shows.
        var row = Row();

        Assert.Contains(_bidi, row.Title, StringComparison.Ordinal);
        Assert.Contains(_bidi, row.Path, StringComparison.Ordinal);
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
