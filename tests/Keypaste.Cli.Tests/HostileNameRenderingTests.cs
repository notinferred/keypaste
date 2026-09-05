using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Untrusted names must not reach a terminal able to misrepresent themselves.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EntryNameSanitizer"/> exists for this, and its own documentation says
/// <c>WasAltered</c> is there "so a listing can mark an entry whose displayed name is not what the
/// vault holds". These are the listings, and they did not call it. Titles and group paths are
/// attacker-reachable: anything with write access to the vault chooses them, which includes
/// KeePassXC, a vault shared with a teammate, and <c>keypaste add</c> itself.
/// </para>
/// <para>
/// <b>The payload is a bidi override, not an ANSI escape, and that is a measured choice.</b> A KDBX
/// title cannot carry a C0 control character at all: the format stores it in XML, U+001B is not a
/// legal XML 1.0 character, and the round trip drops it. Measured rather than assumed — an entry
/// titled with ESC, U+202E, U+200B and BEL comes back holding only U+202E and U+200B. So the honest
/// threat to a listing is not a repainted terminal; it is a name that <em>reads</em> as something it
/// is not, which is the case <see cref="Keypaste.Core.Approval.ApprovalPrompt"/> already argues about
/// an entry "rendering as though it lived somewhere it does not".
/// </para>
/// <para>
/// The exception is <c>env pull</c>, whose rejection message quotes a key straight out of a file
/// that arrived from elsewhere. Nothing filters that text, because it never goes near the vault, so
/// there an ANSI escape really does reach the terminal.
/// </para>
/// <para>
/// Every case asserts twice, and the second matters as much as the first: the trickery is gone,
/// <b>and</b> the row is still listed. Dropping it would be its own defect — <c>env ls</c> already
/// argues that keypaste "does not get to pretend the file says something other than what KeePassXC
/// shows" (docs/PRODUCT.md law 4.6). Sanitizing renders a name safely; it does not hide it.
/// </para>
/// </remarks>
public sealed class HostileNameRenderingTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    /// <summary>RIGHT-TO-LEFT OVERRIDE. Legal XML, so it survives a KDBX round trip.</summary>
    private static readonly string _bidi = ((char)0x202E).ToString();

    /// <summary>ZERO WIDTH SPACE. Legal XML, invisible, so two distinct names can look identical.</summary>
    private static readonly string _zwsp = ((char)0x200B).ToString();

    /// <summary>ESC. Stripped by the KDBX round trip, so it is only a threat off the vault path.</summary>
    private static readonly string _esc = ((char)27).ToString();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static string Spoofed(string name) => "prod" + _bidi + name + _zwsp;

    private static void AssertRenderedSafely(string stream)
    {
        Assert.DoesNotContain(_bidi, stream, StringComparison.Ordinal);
        Assert.DoesNotContain(_zwsp, stream, StringComparison.Ordinal);
        Assert.Contains("prod", stream, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_DoesNotRenderAnEntryTitleThatCanMisrepresentItself()
    {
        _cli.SeedVault(_master, ($"personal/{Spoofed("token")}", "value"));
        _cli.Prompt.Enqueue(_master);

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("ls", "--vault", _cli.VaultPath));

        AssertRenderedSafely(_cli.Out);
        Assert.Contains("token", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void LsFlat_DoesNotRenderAnEntryTitleThatCanMisrepresentItself()
    {
        _cli.SeedVault(_master, ($"personal/{Spoofed("token")}", "value"));
        _cli.Prompt.Enqueue(_master);

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("ls", "--flat", "--vault", _cli.VaultPath));

        AssertRenderedSafely(_cli.Out);
        Assert.Contains("token", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvLs_DoesNotRenderAProjectNameThatCanMisrepresentItself()
    {
        _cli.SeedVault(_master, ($"env/{Spoofed("staging")}/API_KEY", "value"));
        _cli.Prompt.Enqueue(_master);

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("env", "ls", "--vault", _cli.VaultPath));

        AssertRenderedSafely(_cli.Out);
    }

    [Fact]
    public void EnvLsProject_DoesNotRenderAVariableNameThatCanMisrepresentItself()
    {
        const string project = "demo";
        _cli.SeedVault(_master, ($"env/{project}/{Spoofed("KEY")}", "value"));
        _cli.Prompt.Enqueue(_master);

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("env", "ls", project, "--vault", _cli.VaultPath));

        AssertRenderedSafely(_cli.Out);
        AssertRenderedSafely(_cli.Out + _cli.Err);
    }

    [Fact]
    public void EnvPull_DoesNotEchoAnEscapeOutOfARejectedKeyInAFileFromElsewhere()
    {
        // The one place an ANSI escape genuinely reaches a terminal: the key is refused by
        // EnvConvention.IsValidKey, the refusal quotes it back, and it never went near the vault.
        var dotenv = Path.Combine(_cli.Directory, "from-elsewhere.env");
        File.WriteAllText(dotenv, "A" + _esc + "[31mB=whatever\n");

        _cli.SeedVault(_master);
        _cli.Prompt.Enqueue(_master);

        _cli.Run("env", "pull", "demo", dotenv, "--yes", "--vault", _cli.VaultPath);

        Assert.DoesNotContain(_esc, _cli.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_esc, _cli.Out, StringComparison.Ordinal);
    }
}
