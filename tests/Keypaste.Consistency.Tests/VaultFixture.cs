using Keypaste.App.Session;
using Keypaste.Cli.Tests;
using Keypaste.Core;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A real vault, a CLI that can run verbs against it, and an app session that can open it.
/// </summary>
/// <remarks>
/// The vault is seeded <b>through the CLI</b> rather than through <see cref="Vault.Create"/>, for
/// the reason <c>make-compat-fixture.sh</c> gives about the KeePassXC gate: a fixture built by the
/// shipped verb is a fixture whose shape the shipped verb actually produces.
/// </remarks>
internal sealed class VaultFixture : IDisposable
{
    internal const string Master = "correct horse battery staple";

    private readonly AppVaultSession _session;

    internal VaultFixture(params (string Path, string Password)[] entries)
    {
        Cli = new CliHarness();
        Cli.SeedVault(Master, entries);

        // TimeProvider.System, not a fake clock: nothing here is about idle locking, and a fake
        // clock would only be a second thing to get wrong.
        _session = new AppVaultSession(TimeProvider.System);
    }

    /// <summary>The CLI, driven in-process the way <c>Keypaste.Cli.Tests</c> drives it.</summary>
    internal CliHarness Cli { get; }

    /// <summary>The vault file both front ends are looking at.</summary>
    internal string VaultPath => Cli.VaultPath;

    /// <summary>The app's session, once <see cref="Unlock"/> has been called.</summary>
    internal AppVaultSession Session => _session;

    /// <summary>The open vault the app is holding.</summary>
    /// <exception cref="InvalidOperationException">The session is locked.</exception>
    internal Vault Unlocked => _session.Unlocked
        ?? throw new InvalidOperationException("the app session is locked");

    /// <summary>Opens the vault in the app, the way the unlock screen does.</summary>
    internal UnlockOutcome Unlock()
    {
        using var master = new SecretBuffer();
        master.Append(Master);
        return _session.TryUnlock(VaultPath, master.Value);
    }

    /// <summary>Runs a verb, answering the master-password prompt.</summary>
    internal int Run(params string[] args) => RunAnswering([], args);

    /// <summary>
    /// Runs a verb, answering the master-password prompt and then <paramref name="answers"/>.
    /// </summary>
    /// <param name="answers">What to say to each prompt after the master password, in order.</param>
    /// <param name="args">The verb and its arguments.</param>
    /// <remarks>
    /// <para>
    /// The master password is enqueued <b>first</b>, because it is the first thing every verb asks
    /// for. A test that enqueued its own answer beforehand would be handing it to the vault as the
    /// master password and getting exit 4 for reasons that have nothing to do with what it meant to
    /// check.
    /// </para>
    /// <para>
    /// <c>--vault</c> is added here so no test can forget it and silently read whatever
    /// <c>KEYPASTE_VAULT</c> says on the machine running it — and it is inserted <b>before</b> any
    /// bare <c>--</c>, because <c>keypaste run</c> splits there before parsing any option, so an
    /// appended <c>--vault</c> would become an argument to the child process instead.
    /// </para>
    /// </remarks>
    internal int RunAnswering(string[] answers, params string[] args)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(args);

        Cli.Stdout.GetStringBuilder().Clear();
        Cli.Stderr.GetStringBuilder().Clear();

        Cli.Prompt.Enqueue(Master);
        Cli.Prompt.Enqueue(answers);

        var separator = Array.IndexOf(args, "--");
        var at = separator < 0 ? args.Length : separator;

        return Cli.Run([.. args[..at], "--vault", VaultPath, .. args[at..]]);
    }

    public void Dispose()
    {
        _session.Dispose();
        Cli.Dispose();
    }
}
