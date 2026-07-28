using Keypaste.App.Session;
using Keypaste.Cli;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// The app and the CLI looking at one file at the same time.
/// </summary>
/// <remarks>
/// <para>
/// The foundation the rest of this project stands on. Before any test can claim "an edit made in
/// the GUI is what the CLI returns", it has to be true that the two can hold the same vault at all
/// — and on Windows that is a question about file sharing, not a formality.
/// </para>
/// <para>
/// It is also the wiring check. This assembly is the only one that references both front ends, is
/// in neither solution, and is built by a step in <c>app.yml</c> rather than by
/// <c>dotnet build &lt;solution&gt;</c>. If any of that comes undone, these fail first and loudest.
/// </para>
/// </remarks>
public sealed class OneVaultTwoFrontEndsTests
{
    [Fact]
    public void The_cli_reads_a_vault_the_app_is_holding_open()
    {
        using var fixture = new VaultFixture(("servers/production", "production-password"));

        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        // The app has the vault open right now. On Windows a reader that took an exclusive handle
        // would make this fail, and every later test in this project would fail with it.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("ls"));
        Assert.Contains("production", fixture.Cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_opens_a_vault_the_cli_wrote()
    {
        using var fixture = new VaultFixture(("servers/production", "production-password"));

        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());
        Assert.NotNull(fixture.Unlocked.Find("servers/production"));
    }

    /// <summary>
    /// The negative control for <see cref="The_app_opens_a_vault_the_cli_wrote"/>.
    /// </summary>
    /// <remarks>
    /// Without it, an <c>Unlock</c> that returned <c>Opened</c> unconditionally would satisfy every
    /// other test here.
    /// </remarks>
    [Fact]
    public void A_wrong_master_password_does_not_open_it()
    {
        using var fixture = new VaultFixture(("servers/production", "production-password"));

        using var wrong = new Core.SecretBuffer();
        wrong.Append("not the master password");

        Assert.Equal(UnlockOutcome.WrongPassword, fixture.Session.TryUnlock(fixture.VaultPath, wrong.Value));
        Assert.False(fixture.Session.IsUnlocked);
    }
}
