using System.Xml.Linq;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// Why the KeePassXC compatibility gate covers a vault the desktop app wrote.
/// </summary>
/// <remarks>
/// <para>
/// docs/PRODUCT.md law 4.6 says any KDBX keypaste writes must open in KeePassXC, and it is tested in CI
/// against real KeePassXC — but that gate lives in <c>ci.yml</c> and drives the CLI. 4.2 makes the
/// app a writer for the first time, so either it needs a gate of its own or there is an argument for
/// why it does not.
/// </para>
/// <para>
/// <b>The argument.</b> Every vault mutation the app can perform goes through
/// <c>Vault.AddEntry</c>, <c>UpdateEntry</c>, <c>RemoveEntry</c>, <c>RestoreRevision</c>,
/// <c>EnvStore.TrySet</c> or
/// <c>EnvStore.Remove</c>, and every write goes through <c>Vault.Save()</c> into the same vendored
/// KeePassLib with the same <c>KdbxFormat</c> parameters. The CLI's path is the identical set of
/// calls. The salt and nonces differ per save, which is a property of the format rather than of the
/// caller. In particular an inline edit calls <c>UpdateEntry</c>, whose <c>&lt;History&gt;</c>
/// element is exactly what section A of <c>scripts/verify-keepassxc-writeback.sh</c> already opens
/// in KeePassXC.
/// </para>
/// <para>
/// <b>4.8 made the app a vault creator, and the argument survived it for one reason.</b> Create does
/// not reach <c>Vault.Create</c>: it calls <c>VaultCreation.TryCreate</c>, which is the same
/// function, in the same assembly, that <c>keypaste init</c> calls — and
/// <c>make-compat-fixture.sh</c> builds the gate's fixture with <c>keypaste init</c>. So the file
/// KeePassXC opens on three operating systems every CI run is produced by the app's creation path,
/// not by one that resembles it. <c>Vault.Create(</c> and <c>Directory.CreateDirectory</c> are
/// forbidden below to keep that literally true rather than nearly true; the app's <c>--selftest</c>
/// was moved onto <c>VaultCreation</c> so the ban needs no exemption.
/// </para>
/// <para>
/// <b>V.2b made the app a restorer, and the argument covers it for the same reason.</b> The pane's
/// Restore button calls <c>Vault.RestoreRevision</c> and <c>Vault.Save</c> — which is exactly the
/// pair <c>tests/Keypaste.VaultRestorer</c> calls for <c>scripts/verify-keepassxc-history.sh</c>, so
/// the vault KeePassXC reads back in that gate is written by the path the button takes (D-0230).
/// </para>
/// <para>
/// V.4b is the feature this test was written ahead of: the app now restores a backup and exports
/// the vault, and it still moves no byte. Both are <c>VaultBackups.Restore</c> and
/// <c>Vault.ExportTo</c>, copies of bytes a gated save wrote, and
/// <c>scripts/verify-keepassxc-backup.sh</c> opens the result of each in KeePassXC through the same
/// calls. This stayed green without being touched, which is the argument surviving.
/// </para>
/// <para>
/// <b>"No new gate is needed" is itself a claim, and D-0036's standard is that a claim needs
/// something that can hold it.</b> These two tests are that something. The day either fails, the
/// argument above has stopped being true and <c>app.yml</c> needs a KeePassXC job.
/// </para>
/// </remarks>
public sealed class TheAppSharesTheWriterTests
{
    /// <summary>
    /// No code in the app writes a vault file itself.
    /// </summary>
    /// <remarks>
    /// <b>The mutations that must fail this:</b> a "back up my vault" button implemented with
    /// <c>File.Copy</c>, or an export written by hand — either of which produces a KDBX no
    /// compatibility gate has ever opened; and a create that reaches <c>Vault.Create</c> directly,
    /// which would give the app a creation path of its own and expire the argument above.
    /// </remarks>
    [Fact]
    public void No_app_code_writes_a_file_itself()
    {
        string[] forbidden =
        [
            "KeePassLib",
            "File.WriteAllBytes",
            "File.WriteAllText",
            "File.Create(",
            "new FileStream",
            "File.Copy",
            "File.Move",
            "Vault.Create(",
            "Directory.CreateDirectory",
        ];

        var offenders = new List<string>();
        var app = Path.Combine(RepoRoot(), "src", "Keypaste.App");

        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (var needle in forbidden)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {needle}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The app references the core and nothing else of this repository's own code.
    /// </summary>
    /// <remarks>
    /// The foundation the argument rests on: there is no second route to a KDBX writer because there
    /// is no second project to reach one through. One line, and without it every sentence above is
    /// an assurance rather than a check.
    /// </remarks>
    [Fact]
    public void The_app_references_only_the_core()
    {
        var csproj = Path.Combine(RepoRoot(), "src", "Keypaste.App", "Keypaste.App.csproj");

        var referenced = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Keypaste.Core"], referenced);
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.app.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));

            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.app.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
