using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;

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
    /// <summary>What a refused open adds when the missing factor may be a hardware key, which only the app asks (D-0366).</summary>
    internal const string HardwareKeyRefusal =
        "keypaste: a vault that also needs a YubiKey opens only in the desktop app; the CLI cannot ask a hardware key. "
        + "While the app holds the vault, `keypaste run --session` and edits such as `keypaste set` reach it there.";

    /// <summary>Opens the vault at <paramref name="path"/> and runs <paramref name="body"/>.</summary>
    /// <param name="path">The vault, already resolved by <see cref="VaultLocator.TryResolve"/>.</param>
    /// <param name="line">
    /// The parsed command line, for <c>--keyfile</c>. Taken whole rather than as a resolved path so
    /// that every verb reaches the keyfile the same way, for the reason the vault path is resolved
    /// once: twelve commands each deciding what an unreadable keyfile means is twelve wordings.
    /// </param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="body">What to do with the open vault.</param>
    /// <param name="namesHardwareKeys">
    /// Whether a refusal also says a hardware key cannot be the missing factor. keypaste cannot tell a
    /// wrong secret from a challenge-response vault, and a verb about access is where somebody with
    /// one would look for the reason. Every verb says it for a vault <c>recent.toml</c> remembers the
    /// app opening with a hardware key.
    /// </param>
    internal static int Open(
        string path, ParseResult line, CliContext context, Func<Vault, int> body, bool namesHardwareKeys = false)
    {
        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste: no vault at '{path}'");
            return CliApp.ExitNotFound;
        }

        if (!TryKeyfile(line, context, out var keyfile))
        {
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
            using var vault = Vault.Open(path, master.Value, keyfile);
            var exit = body(vault);
            Announce(vault, context);
            return exit;
        }
        catch (InvalidMasterPasswordException)
        {
            context.Stderr.WriteLine(keyfile is null
                ? "keypaste: wrong master password"
                : "keypaste: wrong master password or keyfile");
            if (namesHardwareKeys || LastOpenedWithHardwareKey(path, context))
            {
                context.Stderr.WriteLine(HardwareKeyRefusal);
            }

            return CliApp.ExitAuthFailed;
        }
        catch (UnreadableKeyfileException ex)
        {
            context.Stderr.WriteLine($"keypaste: {ex.Message}");
            return CliApp.ExitNotFound;
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
    /// Takes the vault's claim, then opens it and runs <paramref name="body"/>, for a verb that saves.
    /// </summary>
    /// <remarks>
    /// The claim is taken before the password is read, as <c>keypaste approve</c> takes it, so a
    /// command never saves under a running owner and leaves that owner's copy changed on disk (D-0317).
    /// Every verb that saves opens the vault here; <see cref="Open"/> is for verbs that never save
    /// (D-0382), which <c>SavingVerbSourceRulesTests</c> holds.
    /// </remarks>
    /// <param name="path">The vault, already resolved by <see cref="VaultLocator.TryResolve"/>.</param>
    /// <param name="line">The parsed command line, for <c>--keyfile</c>.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="body">What to do with the open vault.</param>
    /// <param name="namesHardwareKeys">As for <see cref="Open"/>.</param>
    internal static int OpenHeld(
        string path, ParseResult line, CliContext context, Func<Vault, int> body, bool namesHardwareKeys = false) =>
        Held(path, context, () => Open(path, line, context, body, namesHardwareKeys));

    /// <summary>
    /// Takes the vault's claim, then opens the vault and runs <paramref name="body"/>, for a verb that edits
    /// items; while the app or <c>keypaste approve</c> holds the vault, sends the edit to it instead (D-0464).
    /// </summary>
    /// <remarks>
    /// The holder asks the person and saves the edit under its own claim, so no master password is read
    /// here on that path; with nothing answering on its pipe the verb is refused as <see cref="OpenHeld(string, ParseResult, CliContext, Func{Vault, int}, bool)"/> refuses it.
    /// </remarks>
    /// <param name="path">The vault, already resolved by <see cref="VaultLocator.TryResolve"/>.</param>
    /// <param name="line">The parsed command line, for <c>--keyfile</c> and <c>--approver</c>.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="route">How the verb reads its values and says what was done, for the holder's path.</param>
    /// <param name="body">What to do with the open vault.</param>
    internal static int OpenHeld(string path, ParseResult line, CliContext context, OwnerRoute route, Func<Vault, int> body) =>
        Held(path, context, () => Open(path, line, context, body), (holder, refusal) => OwnerEdit.Ask(path, line, context, holder, route, refusal));

    /// <summary>
    /// Takes the vault's claim, runs <paramref name="first"/> for what the verb needs before the vault's
    /// own password, then opens the vault and runs <paramref name="body"/> with both.
    /// </summary>
    /// <remarks>
    /// <c>import</c> asks for its source's password between the claim and the target's, so a vault
    /// something holds is refused before either is typed.
    /// </remarks>
    /// <param name="path">The vault, already resolved by <see cref="VaultLocator.TryResolve"/>.</param>
    /// <param name="line">The parsed command line, for <c>--keyfile</c>.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="first">Obtains the value and hands it on, returning the exit code of what it was handed.</param>
    /// <param name="body">What to do with the value and the open vault.</param>
    internal static int OpenHeld<T>(
        string path, ParseResult line, CliContext context, Func<Func<T, int>, int> first, Func<T, Vault, int> body) =>
        Held(path, context, () => first(value => Open(path, line, context, vault => body(value, vault))));

    /// <summary>
    /// Takes the vault's claim as an owner of <paramref name="kind"/>, then opens it and runs
    /// <paramref name="body"/> with both, for <c>keypaste approve</c>, which serves the vault and saves
    /// the agent settings a person confirms in it.
    /// </summary>
    /// <remarks>A vault something holds is refused with the claim's own sentence, before the password is read (D-0309).</remarks>
    /// <param name="path">The vault, already resolved by <see cref="VaultLocator.TryResolve"/>.</param>
    /// <param name="line">The parsed command line, for <c>--keyfile</c>.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="kind">What kind of owner takes the claim.</param>
    /// <param name="body">What to do with the claim and the open vault.</param>
    internal static int OpenHeld(
        string path, ParseResult line, CliContext context, OwnerKind kind, Func<VaultClaim, Vault, int> body)
    {
        var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!VaultClaim.TryAcquire(home, path, kind, out var claim, out var refusal))
        {
            context.Stderr.WriteLine($"keypaste: {refusal}");
            return CliApp.ExitInternalError;
        }

        using (claim)
        {
            return Open(path, line, context, vault => body(claim, vault));
        }
    }

    /// <summary>
    /// Why a verb that saves is refused a vault something holds, naming the holder and what to do next.
    /// </summary>
    /// <param name="refusal">The claim's own sentence, kept when nothing is known of the holder.</param>
    /// <param name="holder">The process holding the vault, when it named itself.</param>
    internal static string HeldRefusal(string refusal, VaultOwner? holder) => holder?.Kind switch
    {
        OwnerKind.DesktopApp =>
            $"this vault is already unlocked in {holder.Describe()}. Make the change there, or run `keypaste lock` and try again.",
        OwnerKind.TerminalAgent =>
            $"this vault is already unlocked in {holder.Describe()}. Run `keypaste lock` and try again.",
        OwnerKind.CommandLine =>
            $"this vault is in use by {holder.Describe()}. Try again when it finishes.",
        _ => refusal,
    };

    private static int Held(string path, CliContext context, Func<int> body, Func<VaultOwner, string, int>? routed = null)
    {
        var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!VaultClaim.TryAcquire(home, path, OwnerKind.CommandLine, out var claim, out var refusal, out var holder))
        {
            var said = HeldRefusal(refusal, holder);

            if (routed is not null && holder is { Kind: OwnerKind.DesktopApp or OwnerKind.TerminalAgent })
            {
                return routed(holder, said);
            }

            context.Stderr.WriteLine($"keypaste: {said}");
            return CliApp.ExitInternalError;
        }

        using (claim)
        {
            return body();
        }
    }

    /// <summary>
    /// Resolves <c>--keyfile</c>, refuses a file that is not usable as one, and says when the file
    /// that was given is the fragile kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before the password prompt, because being asked for a master password and then told the
    /// keyfile was never there wastes the one thing the person had to type. The refusal is
    /// <see cref="VaultKeyfile"/>'s finding worded here, so a missing keyfile reads like a missing
    /// vault rather than like a wrong password.
    /// </para>
    /// <para>
    /// <b>The warning is on stderr and happens once.</b> A hashed-any-file keyfile is one edit away
    /// from losing the vault and somebody should hear so, but <c>keypaste get</c> is piped into
    /// other programs and <c>keypaste run</c> hands its stdout to a child. Saying it on stdout
    /// would put a sentence about key material into somebody's script output, which is the rule
    /// <see cref="Announce"/> already follows.
    /// </para>
    /// </remarks>
    private static bool TryKeyfile(ParseResult line, CliContext context, out string? keyfile)
    {
        if (!VaultLocator.TryResolveKeyfile(line, context.Environment, out keyfile))
        {
            return true;
        }

        var inspection = VaultKeyfile.Inspect(keyfile!);
        if (!inspection.Accepted)
        {
            context.Stderr.WriteLine($"keypaste: {Refusal(inspection.Outcome, keyfile!)}");
            keyfile = null;
            return false;
        }

        if (inspection.IsFragile)
        {
            context.Stderr.WriteLine(
                $"keypaste: '{keyfile}' is not a keyfile keypaste or KeePassXC made, so the vault is "
                + "keyed to its exact contents. Changing or replacing that file locks the vault for good.");
        }

        return true;
    }

    internal static string Refusal(KeyfileOutcome outcome, string path) => outcome switch
    {
        KeyfileOutcome.Missing => $"no keyfile at '{path}'",
        KeyfileOutcome.Unreadable => $"the keyfile '{path}' could not be read",
        KeyfileOutcome.Empty => $"the keyfile '{path}' is empty",
        KeyfileOutcome.IsAVault => $"'{path}' is a KeePass vault, not a keyfile",
        KeyfileOutcome.XmlUnreadable => UnreadableKeyfile.Explain(path),
        _ => $"the keyfile '{path}' cannot be used",
    };

    /// <summary>Whether <c>recent.toml</c> remembers the app last opening the vault at <paramref name="path"/> with a hardware key.</summary>
    private static bool LastOpenedWithHardwareKey(string path, CliContext context)
    {
        var recent = KeypasteHome.RecentPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));
        var full = Path.GetFullPath(path);

        return RecentVaults.Load(recent)
            .Any(vault => vault.HardwareKeySlot is not null && string.Equals(vault.Path, full, PathIdentity.Comparison));
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
    /// <param name="line">The parsed command line, for <c>--keyfile</c>.</param>
    /// <param name="context">Where prompts and errors go.</param>
    /// <param name="load">Reads the vault. Returns an exit code, and what to hand on.</param>
    /// <param name="use">Runs after the vault has been disposed.</param>
    internal static int OpenThen<T>(
        string path,
        ParseResult line,
        CliContext context,
        Func<Vault, (int Exit, T? Loaded)> load,
        Func<T, int> use)
        where T : class
    {
        T? loaded = null;

        var exit = Open(path, line, context, vault =>
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
