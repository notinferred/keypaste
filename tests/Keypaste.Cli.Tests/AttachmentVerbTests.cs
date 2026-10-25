using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Attachments from the terminal: <c>attachment ls</c>, <c>add</c>, <c>replace</c>, <c>rm</c> and <c>save</c> (V.8a, D-0535).
/// Names and sizes are printed, never content, and a file that cannot be attached is refused before the master password.
/// </summary>
public sealed class AttachmentVerbTests : IDisposable
{
    private const string _master = "attachment-verb-master";

    private static readonly EntryName _stripe = new("api", "Stripe");

    private readonly CliHarness _harness = new();

    public AttachmentVerbTests()
    {
        _harness.SeedVault(_master, ("api/Stripe", "login-password"));
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Prompt.Enqueue(_master);
        return _harness.Run([.. args, "--vault", _harness.VaultPath]);
    }

    private string Written(string name, byte[] bytes)
    {
        var path = Path.Combine(_harness.Directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Dictionary<string, byte[]> Held()
    {
        var saves = Directory.CreateTempSubdirectory("keypaste-attachment-held-").FullName;

        try
        {
            using var vault = Vault.Open(_harness.VaultPath, _master);

            return vault.Attachments(_stripe)!.ToDictionary(
                attachment => attachment.Name,
                attachment =>
                {
                    var path = Path.Combine(saves, Guid.NewGuid().ToString("N"));
                    Assert.Equal(AttachmentSaveOutcome.Saved, vault.SaveAttachment(_stripe, attachment.Name, path, out _));
                    return File.ReadAllBytes(path);
                },
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }

    private int Revisions()
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.ReadHistory(_stripe)!.Count;
    }

    private byte[] Bytes() => File.ReadAllBytes(_harness.VaultPath);

    [Fact]
    public void Add_attaches_a_file_under_its_own_name_in_one_revision_and_ls_names_it_with_its_size()
    {
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        var file = Written("statement.pdf", bytes);

        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", file));

        Assert.Equal(bytes, Held()["statement.pdf"]);
        Assert.Equal(1, Revisions());
        Assert.Contains("attached statement.pdf to api/Stripe · 4.0 KiB", _harness.Err, StringComparison.Ordinal);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "ls", "api/Stripe"));
        Assert.Equal("statement.pdf  4.0 KiB" + Environment.NewLine, _harness.Out);

        _harness.Stdout.GetStringBuilder().Clear();
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "ls", "api/Stripe", "--json"));
        Assert.Equal("""[{"name":"statement.pdf","size":4096}]""" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void Add_takes_a_name_and_refuses_one_already_there_without_writing()
    {
        var file = Written("notes.txt", [1, 2, 3]);

        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", file, "--name", "Recovery codes.txt"));
        Assert.Equal(new byte[] { 1, 2, 3 }, Held()["Recovery codes.txt"]);
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("attachment", "add", "api/Stripe", file, "--name", "Recovery codes.txt"));

        Assert.Equal(before, Bytes());
        Assert.Contains("already has an attachment called Recovery codes.txt", _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a/b.txt")]
    [InlineData("..")]
    [InlineData(" padded")]
    public void Add_refuses_a_name_before_the_master_password(string name)
    {
        var file = Written("notes.txt", [1]);
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("attachment", "add", "api/Stripe", file, "--name", name));

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Add_and_replace_refuse_a_missing_file_or_one_over_the_cap_before_the_master_password()
    {
        var missing = Path.Combine(_harness.Directory, "absent.bin");
        var large = Written("large.bin", new byte[EntryAttachment.MaximumSize + 1]);
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "add", "api/Stripe", missing));
        _harness.AssertExit(CliApp.ExitUsageError, Run("attachment", "add", "api/Stripe", large));
        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "replace", "api/Stripe", "large.bin", missing));
        _harness.AssertExit(CliApp.ExitUsageError, Run("attachment", "replace", "api/Stripe", "large.bin", large));

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(before, Bytes());
        Assert.Contains("is larger than 5 MiB, the most keypaste attaches", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_writes_new_bytes_under_the_same_name_and_says_what_it_held_stays_in_history()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", Written("notes.txt", [1, 2, 3])));

        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "replace", "api/Stripe", "notes.txt", Written("newer.txt", [4, 5])));

        Assert.Equal(new byte[] { 4, 5 }, Assert.Single(Held()).Value);
        Assert.Equal(2, Revisions());
        Assert.Contains("replaced notes.txt on api/Stripe · 2 bytes · what it held stays in history", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Rm_removes_one_attachment_in_one_revision_and_says_it_stays_in_history()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", Written("notes.txt", [1, 2, 3])));

        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "rm", "api/Stripe", "notes.txt"));

        Assert.Empty(Held());
        Assert.Equal(2, Revisions());
        Assert.Contains("removed notes.txt from api/Stripe · it stays in history", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_and_rm_of_an_attachment_the_item_lacks_are_not_found_and_write_nothing()
    {
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "replace", "api/Stripe", "absent.txt", Written("notes.txt", [1])));
        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "rm", "api/Stripe", "absent.txt"));
        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "rm", "api/Nobody", "absent.txt"));

        Assert.Equal(before, Bytes());
        Assert.Contains("'api/Stripe' has no attachment absent.txt", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_writes_the_bytes_to_a_new_file_and_says_it_is_not_encrypted()
    {
        var bytes = Enumerable.Range(0, 300).Select(i => (byte)(i * 7)).ToArray();
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", Written("statement.pdf", bytes)));
        var destination = Path.Combine(_harness.Directory, "saved.pdf");

        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "save", "api/Stripe", "statement.pdf", destination));

        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Contains($"saved statement.pdf from api/Stripe to {destination}", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("note: the file is not encrypted", _harness.Err, StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
        }
    }

    [Fact]
    public void Save_refuses_an_occupied_path_before_the_master_password_and_an_absent_attachment_writes_nothing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "add", "api/Stripe", Written("statement.pdf", [1, 2])));
        var occupied = Written("occupied.pdf", [9]);
        _harness.Prompt.PromptsSeen.Clear();

        _harness.AssertExit(CliApp.ExitUsageError, Run("attachment", "save", "api/Stripe", "statement.pdf", occupied));
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(occupied));

        var absent = Path.Combine(_harness.Directory, "absent.pdf");
        _harness.AssertExit(CliApp.ExitNotFound, Run("attachment", "save", "api/Stripe", "absent.pdf", absent));
        Assert.False(File.Exists(absent));
    }

    [Fact]
    public void Ls_of_an_item_without_attachments_says_so()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("attachment", "ls", "api/Stripe"));

        Assert.Empty(_harness.Out);
        Assert.Contains("'api/Stripe' has no attachments", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_usage_fits_eighty_columns()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("attachment", "--help"));

        Assert.All(_harness.Out.Split(Environment.NewLine), line => Assert.True(line.Length <= 80, $"{line.Length} columns: {line}"));
    }
}
