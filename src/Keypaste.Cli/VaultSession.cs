using Keypaste.Core;

namespace Keypaste.Cli;

/// <summary>
/// Prompts for the master password, opens a vault, and maps every failure to an exit code.
/// </summary>
/// <remarks>
/// One place, so no command invents its own wording or its own exit code for "wrong password".
/// The callback shape is also what keeps the <c>using var vault</c> inside a single method, so no
/// <see cref="Vault"/> can escape the method that created it — which is what satisfies CA2000
/// (an error in this repository) by construction rather than by suppression.
/// </remarks>
internal static class VaultSession
{
    /// <summary>Opens the vault at <paramref name="path"/> and runs <paramref name="body"/>.</summary>
    internal static int Open(string path, CliContext context, Func<Vault, int> body)
    {
        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste: no vault at '{path}'");
            return CliApp.ExitNotFound;
        }

        using var master = context.Prompt.ReadSecret("Master password: ");
        if (master is null)
        {
            context.Stderr.WriteLine("keypaste: no master password given");
            return CliApp.ExitAuthFailed;
        }

        try
        {
            using var vault = Vault.Open(path, master.Value);
            var exit = body(vault);
            Announce(vault, context);
            return exit;
        }
        catch (InvalidMasterPasswordException)
        {
            context.Stderr.WriteLine("keypaste: wrong master password");
            return CliApp.ExitAuthFailed;
        }
        catch (VaultChangedOnDiskException)
        {
            // A command opens, edits and saves within milliseconds, so reaching this means
            // something wrote to the file during that window — a second keypaste, or KeePassXC.
            // Running the command again is the whole recovery, which is why no verb grows a
            // --force: an override flag on five commands would buy nothing and be reached for.
            context.Stderr.WriteLine(
                "keypaste: that vault changed while keypaste was writing it. Nothing was saved — run the command again.");
            return CliApp.ExitInternalError;
        }
        catch (VaultException ex)
        {
            context.Stderr.WriteLine($"keypaste: {ex.Message}");
            return CliApp.ExitInternalError;
        }
    }

    /// <summary>Says, once, that keypaste has started keeping copies beside this vault.</summary>
    /// <remarks>
    /// <para>
    /// Only on the save that creates the directory. A directory of encrypted vaults appearing next to
    /// somebody's file without a word is the kind of discovery docs/PRODUCT.md §6.1 calls a risk to
    /// trust; saying so on every later command would be noise on a command whose job is something
    /// else, and the first thing anyone scripting keypaste would want silenced.
    /// </para>
    /// <para>
    /// On stderr, because stdout is a command's result and a redirect should capture that alone —
    /// the rule <c>keypaste generate</c> already follows for what a passphrase is made of.
    /// </para>
    /// <para>
    /// Here rather than in the five commands that save, because one wording is the point, and the
    /// desktop is not here at all: its Settings screen names the directory instead (V.4b).
    /// </para>
    /// </remarks>
    private static void Announce(Vault vault, CliContext context)
    {
        if (vault.LastBackup is not { CreatedDirectory: true } report)
        {
            return;
        }

        context.Stderr.WriteLine(
            $"keypaste: keeping the last {report.Retained} copies of this vault in " +
            $"'{report.Directory}', in case the file is lost. They open with the master password " +
            "they were made under.");
    }

    /// <summary>
    /// Opens the vault, runs <paramref name="load"/> to take out what is needed, closes it, and
    /// only then runs <paramref name="use"/> with what was taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <c>keypaste run</c>, whose second phase is a child process that may last hours. Holding
    /// a decrypted database open for the lifetime of something unrelated is not a thing a
    /// credential tool gets to do, and making that an ordering rule every future verb has to
    /// remember would be a matter of time.
    /// </para>
    /// <para>
    /// What escapes the callback is <b>data</b> — values the caller already had a right to read —
    /// never a lifetime. <see cref="Open"/>'s <c>using var vault</c> is untouched, so no
    /// <see cref="Vault"/> can outlive the method that created it and CA2000 is still satisfied by
    /// construction rather than by suppression.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">What the first phase takes out of the vault.</typeparam>
    /// <param name="path">The vault file.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="load">Reads the vault. Returns an exit code, and what to hand on.</param>
    /// <param name="use">Runs after the vault has been disposed.</param>
    internal static int OpenThen<T>(
        string path,
        CliContext context,
        Func<Vault, (int Exit, T? Loaded)> load,
        Func<T, int> use)
        where T : class
    {
        T? loaded = null;

        var exit = Open(path, context, vault =>
        {
            var (code, value) = load(vault);
            loaded = value;
            return code;
        });

        // By this line the vault has been disposed and the master password buffer zeroed.
        return exit == CliApp.ExitSuccess && loaded is not null ? use(loaded) : exit;
    }

    /// <summary>
    /// Reads a master password twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Confirmation happens even when stdin is redirected: one code path means the compatibility
    /// gate exercises the branch a human takes, and it costs a script one extra line.
    /// </para>
    /// <para>
    /// <b>It asks; it does not judge.</b> Whether the two match, and whether either is empty, are
    /// creation rules and live in <see cref="VaultCreation"/> so the desktop applies the same ones
    /// (docs/PRODUCT.md law 4.2). Only "nothing was typed at all" is answered here, because that is
    /// a fact about the prompt rather than about the vault.
    /// </para>
    /// </remarks>
    internal static NewMasterPassword? ReadNewMasterPassword(CliContext context)
    {
        var first = context.Prompt.ReadSecret("New master password: ");
        if (first is null)
        {
            context.Stderr.WriteLine("keypaste: no master password given");
            return null;
        }

        // Both buffers pass to NewMasterPassword, which zeroes them together; CA2000 cannot see a
        // lifetime that leaves the method.
#pragma warning disable CA2000

        // Nothing typed, nothing to confirm. This decides no outcome — VaultCreation still refuses
        // the empty password and supplies the words — it only keeps somebody from being asked to
        // retype a blank line, which is what this verb did before the rules moved.
        if (first.Length == 0)
        {
            return new NewMasterPassword(first, new SecretBuffer());
        }

        // A confirmation that never arrived is one that does not match, which is the conclusion
        // VaultCreation draws from an empty buffer.
        var second = context.Prompt.ReadSecret("Confirm master password: ") ?? new SecretBuffer();

        return new NewMasterPassword(first, second);
#pragma warning restore CA2000
    }
}

/// <summary>A new master password and the confirmation typed after it.</summary>
/// <remarks>
/// Both buffers are owned by this object and zeroed together, so no caller can dispose one and
/// forget the other. <see cref="VaultCreation"/> takes the two spans and decides what they mean.
/// </remarks>
internal sealed class NewMasterPassword(SecretBuffer password, SecretBuffer confirmation) : IDisposable
{
    internal SecretBuffer Password { get; } = password;

    internal SecretBuffer Confirmation { get; } = confirmation;

    public void Dispose()
    {
        Password.Dispose();
        Confirmation.Dispose();
    }
}
