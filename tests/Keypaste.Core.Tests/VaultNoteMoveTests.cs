using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// C.2: moving keys out of notes is one revision per entry, and a refused move changes nothing.
/// </summary>
public sealed class VaultNoteMoveTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _stripe = new("services", "Stripe");
    private static readonly EntryName _plain = new("services", "Plain");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-notemove-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Moving_the_fixture_writes_protected_fields_trims_the_notes_and_keeps_the_old_notes_in_one_revision()
    {
        var path = Saved((_stripe, NoteKeyCheckTests.FixtureNotes));

        using (var vault = Vault.Open(path, _master))
        using (var check = new NoteKeyCheck())
        {
            var edits = new List<VaultEdit>();
            vault.Edited += (_, edit) => edits.Add(edit);

            var move = vault.MoveNoteKeys(check, check.Scan(vault));

            Assert.True(move.Moved);
            Assert.Equal([_stripe], move.Entries);
            Assert.Equal([_stripe], Assert.Single(edits).Entries);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(
            [new EntryField("GITHUB_TOKEN", true, false), new EntryField("OPENAI_API_KEY", true, false), new EntryField("STRIPE_SECRET_KEY", true, false)],
            reopened.Fields(_stripe));
        Assert.Equal("sk_test_1", reopened.ReadField(_stripe, "STRIPE_SECRET_KEY"));
        Assert.Equal("sk-proj-2", reopened.ReadField(_stripe, "OPENAI_API_KEY"));
        Assert.Equal(NoteKeyCheckTests.GitHubToken, reopened.ReadField(_stripe, "GITHUB_TOKEN"));
        Assert.Equal(
            string.Join("\n", "-----BEGIN PRIVATE KEY-----", "API_KEY=inside-the-block", "AKIAABCDEFGHIJKLMNOP", "-----END PRIVATE KEY-----", "Recovery codes are in the safe."),
            reopened.Find(_stripe)!.Notes);
        Assert.Equal(NoteKeyCheckTests.FixtureNotes, Assert.Single(reopened.ReadHistory(_stripe)!).Fields.Notes);
        Assert.Equal("p", reopened.Find(_stripe)!.Password);
        Assert.Empty(reopened.Search("sk_test_1"));
    }

    [Fact]
    public void Two_entries_move_as_one_revision_each_and_one_edit_naming_both()
    {
        var path = Saved((_stripe, "A=1\nkeep this"), (_plain, "note\nB=2"));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.True(vault.MoveNoteKeys(check, check.Scan(vault)).Moved);

        Assert.Equal([_plain, _stripe], Assert.Single(edits).Entries.OrderBy(name => name.Title));
        Assert.Single(vault.ReadHistory(_stripe)!);
        Assert.Single(vault.ReadHistory(_plain)!);
        Assert.Equal("keep this", vault.Find(_stripe)!.Notes);
        Assert.Equal("note", vault.Find(_plain)!.Notes);
    }

    [Fact]
    public void Crlf_lines_leave_with_their_terminators_and_only_the_chosen_ones_leave()
    {
        // A KDBX save stores line breaks as LF, so CRLF notes exist only before the first save.
        using var vault = Vault.Create(Path.Combine(_directory, "crlf.kdbx"), _master);
        vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "p", Notes = "first\r\nA=1\r\nmiddle\r\nB=2\r\n" });
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);

        Assert.True(vault.MoveNoteKeys(check, [findings.Single(finding => finding.Field == "B")]).Moved);

        Assert.Equal("first\r\nA=1\r\nmiddle\r\n", vault.Find(_stripe)!.Notes);
        Assert.Equal("2", vault.ReadField(_stripe, "B"));
        Assert.Null(vault.ReadField(_stripe, "A"));
    }

    [Fact]
    public void A_field_already_holding_the_value_keeps_its_flag_and_the_line_still_leaves()
    {
        var path = Saved((_stripe, "text\nKEY=same"));

        using var vault = Vault.Open(path, _master);
        vault.SetFields(_stripe, [new FieldWrite("KEY", "same", Protect: false)]);
        using var check = new NoteKeyCheck();

        Assert.True(vault.MoveNoteKeys(check, check.Scan(vault)).Moved);

        Assert.Equal([new EntryField("KEY", false, false)], vault.Fields(_stripe));
        Assert.Equal("text", vault.Find(_stripe)!.Notes);
    }

    [Fact]
    public void Notes_changed_after_the_check_refuse_the_whole_selection_and_change_nothing()
    {
        var path = Saved((_stripe, "A=1"), (_plain, "B=2"));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);
        vault.UpdateEntry(vault.Find(_stripe)! with { Notes = "A=1\nedited" });
        vault.Save();
        var history = vault.ReadHistory(_stripe)!.Count;

        var edits = 0;
        vault.Edited += (_, _) => edits++;
        var move = vault.MoveNoteKeys(check, findings);

        Assert.False(move.Moved);
        Assert.Equal(NoteKeyRefusalReason.NotesChanged, Assert.Single(move.Refusals).Reason);
        AssertUnchanged(vault, edits, history);
        Assert.Null(vault.ReadField(_plain, "B"));
        Assert.Equal("B=2", vault.Find(_plain)!.Notes);
    }

    [Fact]
    public void A_field_holding_another_value_refuses_the_move()
    {
        var path = Saved((_stripe, "KEY=new"));

        using var vault = Vault.Open(path, _master);
        vault.SetFields(_stripe, [new FieldWrite("KEY", "old")]);
        vault.Save();
        using var check = new NoteKeyCheck();
        var edits = 0;
        vault.Edited += (_, _) => edits++;
        var history = vault.ReadHistory(_stripe)!.Count;

        var move = vault.MoveNoteKeys(check, check.Scan(vault));

        Assert.Equal(NoteKeyRefusalReason.FieldHoldsOtherValue, Assert.Single(move.Refusals).Reason);
        AssertUnchanged(vault, edits, history);
        Assert.Equal("old", vault.ReadField(_stripe, "KEY"));
    }

    [Fact]
    public void A_key_set_twice_is_refused()
    {
        var path = Saved((_stripe, "KEY=a\nKEY=b"));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var edits = 0;
        vault.Edited += (_, _) => edits++;

        var move = vault.MoveNoteKeys(check, check.Scan(vault));

        Assert.All(move.Refusals, refusal => Assert.Equal(NoteKeyRefusalReason.KeyRepeated, refusal.Reason));
        Assert.Equal(2, move.Refusals.Count);
        AssertUnchanged(vault, edits, 0);
    }

    [Fact]
    public void A_renamed_entry_is_refused_as_gone()
    {
        var path = Saved((_stripe, "KEY=v"));

        using var vault = Vault.Open(path, _master);
        using var check = new NoteKeyCheck();
        var findings = check.Scan(vault);
        vault.RenameEntry(_stripe, "Renamed", out _);
        vault.Save();

        var move = vault.MoveNoteKeys(check, findings);

        Assert.Equal(NoteKeyRefusalReason.EntryGone, Assert.Single(move.Refusals).Reason);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void A_finding_from_another_check_is_rejected()
    {
        var path = Saved((_stripe, "KEY=v"));

        using var vault = Vault.Open(path, _master);
        using var first = new NoteKeyCheck();
        using var second = new NoteKeyCheck();

        Assert.Throws<ArgumentException>(() => vault.MoveNoteKeys(second, first.Scan(vault)));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    private static void AssertUnchanged(Vault vault, int edits, int history)
    {
        Assert.Equal(0, edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Equal(history, vault.ReadHistory(_stripe)?.Count ?? 0);
    }

    private string Saved(params (EntryName Name, string Notes)[] entries)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using var vault = Vault.Create(path, _master);

        foreach (var (name, notes) in entries)
        {
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "p", Notes = notes });
        }

        vault.Save();
        return path;
    }
}
