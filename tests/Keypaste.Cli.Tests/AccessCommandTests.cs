using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste access</c>: the prompts it reads and in what order, the exit code for each refusal,
/// which keyfile each flag and variable means, and what it says afterwards.
/// </summary>
public sealed class AccessCommandTests
{
    private const string _master = "correct horse battery staple";
    private const string _next = "a different horse entirely";

    [Fact]
    public void A_piped_password_change_reads_current_new_and_confirmation_in_that_order()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(_master, _next, _next);
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(
            ["Master password: ", "New master password: ", "Confirm master password: "],
            harness.Prompt.PromptsSeen);
        AssertOpens(harness.VaultPath, _next, null);
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(harness.VaultPath, _master));
    }

    [Fact]
    public void A_keyfile_change_asks_only_for_the_current_password()
    {
        using var harness = Seeded();
        var keyfile = Keyfile(harness, "new.key");

        harness.Prompt.Enqueue(_master);
        var exit = harness.Run("access", "--new-keyfile", keyfile, "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal(["Master password: "], harness.Prompt.PromptsSeen);
        AssertOpens(harness.VaultPath, _master, keyfile);
    }

    [Fact]
    public void The_keyfile_variable_is_the_current_keyfile_and_never_the_new_one()
    {
        using var harness = Seeded();
        var current = Keyfile(harness, "current.key");
        var next = Keyfile(harness, "next.key", seed: 2);

        harness.Prompt.Enqueue(_master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("access", "--new-keyfile", current, "--vault", harness.VaultPath));

        harness.Environment[VaultLocation.KeyfileEnvironmentVariable] = current;
        harness.Prompt.Enqueue(_master);
        var exit = harness.Run("access", "--new-keyfile", next, "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        AssertOpens(harness.VaultPath, _master, next);
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(harness.VaultPath, _master, current));
    }

    [Fact]
    public void A_leftover_keyfile_variable_does_not_become_a_new_keyfile()
    {
        using var harness = Seeded();
        harness.Environment[VaultLocation.KeyfileEnvironmentVariable] = Keyfile(harness, "stray.key");

        harness.Prompt.Enqueue(_master, _next, _next);
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        // The variable names the keyfile that opens the vault now, which this vault does not have.
        Assert.Equal(CliApp.ExitAuthFailed, exit);
        AssertOpens(harness.VaultPath, _master, null);
    }

    [Fact]
    public void Nothing_to_change_is_a_usage_error_before_any_prompt()
    {
        using var harness = Seeded();
        var bytes = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run("access", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void Both_keyfile_flags_together_are_a_usage_error()
    {
        using var harness = Seeded();

        var exit = harness.Run(
            "access", "--new-keyfile", Keyfile(harness, "k.key"), "--remove-keyfile", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void The_vault_itself_as_the_new_keyfile_is_refused_before_any_prompt()
    {
        using var harness = Seeded();
        var bytes = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run("access", "--new-keyfile", harness.VaultPath, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("this vault or one of its backups", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void A_file_in_the_backup_directory_is_refused_as_the_new_keyfile()
    {
        using var harness = Seeded();
        var directory = Directory.CreateDirectory(VaultBackups.DirectoryFor(harness.VaultPath)).FullName;
        var inside = Path.Combine(directory, "raw.key");
        File.WriteAllBytes(inside, new byte[32]);

        var exit = harness.Run("access", "--new-keyfile", inside, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void A_hashed_file_is_refused_as_the_new_keyfile()
    {
        using var harness = Seeded();
        var hashed = Path.Combine(harness.Directory, "notes.txt");
        File.WriteAllText(hashed, "an ordinary file somebody picked, which is exactly the problem");
        var bytes = File.ReadAllBytes(harness.VaultPath);

        var exit = harness.Run("access", "--new-keyfile", hashed, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("exact contents", harness.Err, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void A_missing_new_keyfile_is_not_found()
    {
        using var harness = Seeded();

        var exit = harness.Run("access", "--new-keyfile", Path.Combine(harness.Directory, "absent.key"), "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("no keyfile at", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_current_password_exits_four_and_names_hardware_keys()
    {
        using var harness = Seeded();
        var bytes = File.ReadAllBytes(harness.VaultPath);

        harness.Prompt.Enqueue("not it", _next, _next);
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitAuthFailed, exit);
        Assert.Contains("hardware key", harness.Err, StringComparison.Ordinal);
        Assert.Equal(["Master password: "], harness.Prompt.PromptsSeen);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void An_empty_new_password_is_a_usage_error()
    {
        using var harness = Seeded();
        var bytes = File.ReadAllBytes(harness.VaultPath);

        harness.Prompt.Enqueue(_master, string.Empty);
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("cannot be empty", harness.Err, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void A_mismatched_confirmation_is_a_usage_error()
    {
        using var harness = Seeded();
        var bytes = File.ReadAllBytes(harness.VaultPath);

        harness.Prompt.Enqueue(_master, _next, _next + "!");
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("do not match", harness.Err, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
    }

    [Fact]
    public void Removing_a_keyfile_the_vault_does_not_have_is_a_usage_error()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(_master);
        var exit = harness.Run("access", "--remove-keyfile", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitUsageError, exit);
        Assert.Contains("no keyfile to remove", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vault_changed_while_the_new_password_is_typed_is_not_changed()
    {
        using var harness = Seeded();
        harness.Prompt.OnPrompt = prompt =>
        {
            if (prompt == "New master password: ")
            {
                using var other = Vault.Open(harness.VaultPath, _master);
                other.AddEntry(new VaultEntry { Title = "THEIRS", Password = "value", GroupPath = "env/demo" });
                other.Save();
            }
        };

        harness.Prompt.Enqueue(_master, _next, _next);
        var exit = harness.Run("access", "--password", "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitInternalError, exit);
        Assert.Contains("Nothing was saved", harness.Err, StringComparison.Ordinal);
        AssertOpens(harness.VaultPath, _master, null);
    }

    [Fact]
    public void A_change_reports_on_stderr_and_writes_nothing_to_stdout()
    {
        using var harness = Seeded();
        var keyfile = Keyfile(harness, "new.key");

        harness.Prompt.Enqueue(_master, _next, _next);
        var exit = harness.Run("access", "--password", "--new-keyfile", keyfile, "--vault", harness.VaultPath);

        harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Empty(harness.Out);

        var kept = VaultBackups.List(harness.VaultPath)[0];
        Assert.Contains(kept.Path, harness.Err, StringComparison.Ordinal);
        Assert.Contains("copies made before this change may open with earlier credentials", harness.Err, StringComparison.Ordinal);
        Assert.Contains($"keep only the last {VaultBackups.Retained} copies", harness.Err, StringComparison.Ordinal);
        Assert.Contains("including the one this change just took", harness.Err, StringComparison.Ordinal);
        Assert.Contains("Losing that file locks the vault", harness.Err, StringComparison.Ordinal);
        Assert.Contains("somewhere other than beside the vault", harness.Err, StringComparison.Ordinal);
        Assert.Contains("restart it to use the new credentials", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_next, harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_master, harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_change_alone_does_not_warn_about_a_keyfile()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(_master, _next, _next);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("access", "--password", "--vault", harness.VaultPath));

        Assert.DoesNotContain("Losing that file", harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_secret_buffer_is_zeroed_afterwards()
    {
        using var harness = Seeded();

        harness.Prompt.Enqueue(_master, _next, _next);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("access", "--password", "--vault", harness.VaultPath));

        Assert.Equal(3, harness.Prompt.IssuedSecrets.Count);
        Assert.All(harness.Prompt.IssuedSecrets, buffer => Assert.True(buffer.IsZeroed));
    }

    [Fact]
    public void An_xml_keyfile_is_refused_before_any_prompt_by_a_build_that_would_hash_it()
    {
        using var harness = Seeded();
        var keyfile = XmlKeyfile(harness);
        var bytes = File.ReadAllBytes(harness.VaultPath);
        var copies = VaultBackups.List(harness.VaultPath);

        using var fallback = VaultKeyfile.SimulateXmlFallback();
        var exit = harness.Run("access", "--new-keyfile", keyfile, "--vault", harness.VaultPath);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("cannot read one", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
        Assert.Equal(bytes, File.ReadAllBytes(harness.VaultPath));
        Assert.Equal(copies, VaultBackups.List(harness.VaultPath));
    }

    [Fact]
    public void An_xml_keyfile_vault_is_refused_as_the_keyfile_and_not_as_a_wrong_password()
    {
        using var harness = Seeded();
        var keyfile = XmlKeyfile(harness);
        harness.Prompt.Enqueue(_master);
        harness.AssertExit(CliApp.ExitSuccess, harness.Run("access", "--new-keyfile", keyfile, "--vault", harness.VaultPath));
        harness.Prompt.PromptsSeen.Clear();
        harness.Stderr.GetStringBuilder().Clear();

        using var fallback = VaultKeyfile.SimulateXmlFallback();
        var exit = harness.Run("ls", "--vault", harness.VaultPath, "--keyfile", keyfile);

        Assert.Equal(CliApp.ExitNotFound, exit);
        Assert.Contains("The password is not the problem", harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong master password", harness.Err, StringComparison.Ordinal);
        Assert.Empty(harness.Prompt.PromptsSeen);
    }

    [Fact]
    public void The_verb_is_listed_in_the_usage()
    {
        using var harness = new CliHarness();

        harness.Run("help");

        Assert.Contains("access", harness.Out, StringComparison.Ordinal);
    }

    private static CliHarness Seeded()
    {
        var harness = new CliHarness();
        harness.SeedVault(_master, ("env/demo/TOKEN", "value"));
        harness.Prompt.PromptsSeen.Clear();
        harness.Prompt.IssuedSecrets.Clear();
        return harness;
    }

    private static string Keyfile(CliHarness harness, string name, byte seed = 1)
    {
        var path = Path.Combine(harness.Directory, name);
        File.WriteAllBytes(path, Enumerable.Range(seed, 32).Select(i => (byte)i).ToArray());
        return path;
    }

    private static string XmlKeyfile(CliHarness harness)
    {
        var path = Path.Combine(harness.Directory, "key.keyx");
        File.WriteAllText(
            path,
            """<?xml version="1.0" encoding="UTF-8"?><KeyFile><Meta><Version>2.0</Version></Meta><Key><Data Hash="1E281D73">B243DCB7 D3F97ECC E1DB3620 C8B7D53B A0CE206E 92889C8C 75755038 EE5DCCDD</Data></Key></KeyFile>""");
        return path;
    }

    private static void AssertOpens(string path, string password, string? keyfile)
    {
        using var vault = Vault.Open(path, password, keyfile);
        Assert.NotEmpty(vault.ReadEntries());
    }
}
