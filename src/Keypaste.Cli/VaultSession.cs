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
            return body(vault);
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
    /// Reads a master password twice and checks the two match.
    /// </summary>
    /// <remarks>
    /// Confirmation happens even when stdin is redirected: one code path means the compatibility
    /// gate exercises the branch a human takes, and it costs a script one extra line.
    /// </remarks>
    internal static SecretBuffer? ReadNewMasterPassword(CliContext context)
    {
        var first = context.Prompt.ReadSecret("New master password: ");
        if (first is null)
        {
            context.Stderr.WriteLine("keypaste: no master password given");
            return null;
        }

        if (first.Length == 0)
        {
            first.Dispose();
            context.Stderr.WriteLine("keypaste: the master password cannot be empty");
            return null;
        }

        using var second = context.Prompt.ReadSecret("Confirm master password: ");
        if (second is null || !first.ValueEquals(second))
        {
            first.Dispose();
            context.Stderr.WriteLine("keypaste: the passwords do not match");
            return null;
        }

        return first;
    }
}
