using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Cli.Tests;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A vault the desktop app made is a vault the shipped CLI opens.
/// </summary>
/// <remarks>
/// <para>
/// The inverse of <see cref="VaultFixture"/>, which seeds through the CLI and then asks the app.
/// 4.8 makes the app a vault <i>creator</i> for the first time, and the claim that needs holding is
/// the other direction: the file the Create button produces is an ordinary keypaste vault, not
/// something only the app can read.
/// </para>
/// <para>
/// <b>Reopening it with <c>Vault.Open</c> would prove nothing</b> — core is the shared path, so a
/// round trip through it assumes the agreement it is meant to establish. The CLI's own verbs are
/// asked instead, in-process, the way <c>Keypaste.Cli.Tests</c> asks them.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> creating a vault the app never saves, so the
/// path exists in memory only; writing it somewhere other than where the picker said; and accepting
/// a create whose master password is not the one that was typed, which the wrong-password control
/// below is what catches.
/// </para>
/// </remarks>
public sealed class TheCliOpensAVaultTheAppCreatedTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly CliHarness _cli = new();
    private readonly AppVaultSession _session = new(TimeProvider.System);
    private readonly StubPicker _picker = new();

    /// <summary>The app chooses the first vault it creates, and the CLI then opens it with no flag (G.1).</summary>
    [Fact]
    public async Task The_cli_uses_the_vault_the_app_chose_without_a_flag()
    {
        await CreateThroughTheApp(_cli.Environment[Core.Audit.KeypasteHome.EnvironmentVariable]);

        _cli.Prompt.Interactive = false;
        _cli.Prompt.Enqueue(_master);

        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("ls"));
        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("use"));
        Assert.Equal(_cli.VaultPath, _cli.Out.Trim());
    }

    /// <summary>
    /// The control that keeps the other tests honest: without it, a create that quietly protected the
    /// vault with some other password would still pass them, as long as the harness supplied it.
    /// </summary>
    [Fact]
    public async Task Another_password_does_not_open_it()
    {
        await CreateThroughTheApp();

        _cli.Prompt.Interactive = false;
        _cli.Prompt.Enqueue("not the master password");

        Assert.Equal(CliApp.ExitAuthFailed, _cli.Run("ls", "--vault", _cli.VaultPath));
    }

    [Fact]
    public async Task The_cli_can_add_to_it_and_read_the_value_back()
    {
        await CreateThroughTheApp();

        _cli.Prompt.Interactive = false;
        _cli.Prompt.Enqueue(_master, "s3cret");
        _cli.AssertExit(CliApp.ExitSuccess, _cli.Run("add", "work/github", "--vault", _cli.VaultPath));

        _cli.Stdout.GetStringBuilder().Clear();
        _cli.Prompt.Enqueue(_master);
        _cli.AssertExit(
            CliApp.ExitSuccess,
            _cli.Run("get", "work/github", "--show", "--vault", _cli.VaultPath));

        Assert.Contains("s3cret", _cli.Out, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _session.Dispose();
        _cli.Dispose();
    }

    /// <summary>Drives the desktop Create path at the harness's vault path.</summary>
    private async Task CreateThroughTheApp(string? home = null)
    {
        _picker.NewPath = _cli.VaultPath;

        using var model = new UnlockViewModel(_session, home ?? _cli.Directory, _picker, () => { });

        await model.StartCreateAsync();

        foreach (var c in _master)
        {
            model.TypeNew(c);
            model.TypeConfirm(c);
        }

        await model.CreateAsync();

        Assert.True(_session.IsUnlocked, "the app did not open the vault it created");

        // The app holds the file open with its own in-memory copy; the CLI is a separate reader and
        // must not be asked while this session still owns it.
        _session.Lock(VaultLockReason.Manual);
    }

    /// <summary>The picker, answering whatever the test chose.</summary>
    /// <remarks>
    /// Written here rather than reused from <c>Keypaste.App.Tests</c>: this project deliberately
    /// references the two front ends and nothing else, and a reference to another test assembly
    /// would be the second thing it depends on.
    /// </remarks>
    private sealed class StubPicker : IVaultFilePicker
    {
        internal string? NewPath { get; set; }

        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult(NewPath);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyfileAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickDotEnvAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
