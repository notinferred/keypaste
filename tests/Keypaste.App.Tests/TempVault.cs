using Keypaste.Core;
using Keypaste.Core.Agents;

namespace Keypaste.App.Tests;

/// <summary>
/// A real KDBX file in a temporary directory, deleted when the test finishes.
/// </summary>
/// <remarks>
/// A real vault rather than a fake, because what these tests assert is that Argon2 either accepted
/// a password or did not, and a fake that returns <c>true</c> would prove nothing about the one
/// path where being wrong matters.
/// </remarks>
internal sealed class TempVault : IDisposable
{
    internal const string Password = "correct-horse-battery-staple";

    /// <summary>The password of the vault's one item, strong and used once, so Settings › Recommendations has nothing to say about it (V.9).</summary>
    internal const string ExamplePassword = "entry-secret-7Kq2Vx9mZ4";

    private readonly string _directory;

    internal TempVault()
    {
        _directory = Path.Combine(Path.GetTempPath(), "keypaste-app-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);

        Path_ = Path.Combine(_directory, "test.kdbx");

        using var vault = Vault.Create(Path_, Password);
        vault.AddEntry(new VaultEntry { Title = "example", Password = ExamplePassword });
        vault.Save();
    }

    /// <summary>The agent a test's requests come from when it names no other.</summary>
    internal const string Agent = "ci-probe";

    /// <summary>The vault file.</summary>
    internal string Path_ { get; }

    /// <summary>
    /// What an agent the tests give settings to may do: see as far as its bridge exposes and run
    /// commands, so its bridge's own exposure decides what it reaches, as before agents had settings.
    /// </summary>
    internal static AgentAccess SeesAll { get; } = AgentAccess.Defaults with
    {
        See = EntryExposure.TryCreate(["**"], out var all, out var error) ? all : throw new InvalidOperationException(error),
        Run = true,
    };

    /// <summary>Gives agents <see cref="SeesAll"/> in an open vault, as a person confirms it in the app; the caller saves.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="agents">The agents' names.</param>
    internal static void Sees(Vault vault, params string[] agents)
    {
        var store = new AgentAccessStore(vault);

        foreach (var agent in agents)
        {
            if (store.Write(agent, SeesAll) is not (AgentAccessWrite.Written or AgentAccessWrite.Unchanged) and var refused)
            {
                throw new InvalidOperationException($"{agent}'s settings were refused: {refused}");
            }
        }
    }

    /// <summary>Gives agents <see cref="SeesAll"/> in this fixture's vault, before anything unlocks it.</summary>
    /// <param name="agents">The agents' names; none gives <see cref="Agent"/>.</param>
    internal void Sees(params string[] agents)
    {
        using var vault = Vault.Open(Path_, Password);
        Sees(vault, agents.Length == 0 ? [Agent] : agents);
        vault.Save();
    }

    /// <summary>
    /// A <c>KEYPASTE_HOME</c> pointing inside this fixture, so a test never touches the real one.
    /// </summary>
    internal string Home => _directory;

    /// <summary>Writes a recent-vaults list containing this vault.</summary>
    internal void RememberSelf() =>
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(Home),
            [new Core.Recent.RecentVault(Path_, DateTimeOffset.UtcNow)]);

    /// <summary>A path inside the same directory that holds no file at all.</summary>
    internal string MissingPath => Path.Combine(_directory, "absent.kdbx");

    /// <summary>A file with a <c>.kdbx</c> name that was never a vault.</summary>
    internal string ImposterPath
    {
        get
        {
            var path = Path.Combine(_directory, "imposter.kdbx");
            File.WriteAllText(path, "this is not a vault, it is a text file wearing the extension");
            return path;
        }
    }

    /// <summary>A buffer holding <paramref name="text"/>, for handing to the session.</summary>
    internal static SecretBuffer Secret(string text)
    {
        var buffer = new SecretBuffer();
        buffer.Append(text);
        return buffer;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }
}
