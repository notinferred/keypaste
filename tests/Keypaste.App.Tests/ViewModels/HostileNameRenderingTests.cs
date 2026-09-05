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
    private const string Master = "correct horse battery staple";

    private static readonly string Bidi = ((char)0x202E).ToString();
    private static readonly string Zwsp = ((char)0x200B).ToString();

    private readonly string _directory;
    private readonly string _vaultPath;

    public HostileNameRenderingTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-hostile-app-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, Master);
        vault.AddEntry(new VaultEntry
        {
            Title = "prod" + Bidi + "token" + Zwsp,
            Username = "user" + Bidi + "name",
            Url = "https://example.test/" + Bidi,
            Notes = "note" + Zwsp + "s",
            Password = "p",
            GroupPath = "servers" + Bidi + "live",
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
        Assert.DoesNotContain(Bidi, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain(Zwsp, drawn, StringComparison.Ordinal);
    }

    private EntryRow Row()
    {
        using var vault = Vault.Open(_vaultPath, Master);
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
        using var vault = Vault.Open(_vaultPath, Master);

        foreach (var node in GroupNode.Flatten(vault.ReadGroupPaths()))
        {
            AssertSafe(node.Label);
        }
    }

    [Fact]
    public void Title_is_left_alone_because_it_addresses_the_entry()
    {
        // The counterweight to every case above: sanitizing this would break Find, RemoveEntry and
        // the search filter, and there is a separate display member for what the screen shows.
        var row = Row();

        Assert.Contains(Bidi, row.Title, StringComparison.Ordinal);
        Assert.Contains(Bidi, row.Path, StringComparison.Ordinal);
    }
}
