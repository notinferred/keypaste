using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Making a vault from the desktop (4.8).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UnlockViewModel"/> names no Avalonia type and the picker is behind
/// <see cref="IVaultFilePicker"/>, so the whole create journey is assertable with no application and
/// no window. The headless session is for the claims that are only true of a visual tree, which for
/// 4.8 is the accessibility sweep in <c>MaskedInputAutomationTests</c>.
/// </para>
/// <para>
/// <b>Every refusal asserts a directory snapshot, not just <c>File.Exists</c>.</b> "Nothing was
/// written" has to cover a <c>recent.toml</c> that should not be there and a stranded temporary from
/// a half-run save, neither of which a single-path check would see.
/// </para>
/// </remarks>
public sealed class UnlockCreateTests : IDisposable
{
    private readonly TempHome _home = new();
    private readonly AppVaultSession _session = new(new ManualClock());
    private readonly FakeVaultFilePicker _picker = new();

    private int _unlockedCalls;

    [Fact]
    public async Task A_created_vault_opens_an_unlocked_empty_entry_list()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Assert.True(model.IsCreating);
        Assert.Equal(Path.GetFileName(_home.FreeVaultPath), model.NewVaultName);

        Type(model, TempHome.Password, TempHome.Password);
        await model.CreateAsync();

        Assert.Equal(1, _unlockedCalls);
        Assert.True(_session.IsUnlocked);
        Assert.False(model.IsCreating);
        Assert.True(File.Exists(_home.FreeVaultPath));

        // The screen a person actually lands on, built the way the shell builds it.
        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
        using var entries = new EntriesViewModel(_session, countdown);

        Assert.Empty(entries.Rows);
    }

    [Fact]
    public async Task The_created_vault_reopens_with_the_password_that_was_typed()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, TempHome.Password);
        await model.CreateAsync();

        using var reopened = Vault.Open(_home.FreeVaultPath, TempHome.Password);
        Assert.Empty(reopened.ReadEntries());
    }

    [Fact]
    public async Task A_cancelled_picker_writes_nothing()
    {
        using var model = NewModel();
        var before = _home.Snapshot();

        // Null is a cancelled picker.
        _picker.NewPath = null;

        await model.StartCreateAsync();

        // First that the picker was actually reached. Without this the assertions below would pass
        // against code that wrote a vault before ever opening a dialog.
        Assert.Equal(1, _picker.NewCalls);

        Assert.False(model.IsCreating);
        Assert.Equal(string.Empty, model.Message);
        Assert.False(_session.IsUnlocked);
        Assert.Equal(before, _home.Snapshot());
        Assert.Empty(Remembered());
    }

    /// <summary>
    /// The paired positive control for <see cref="A_cancelled_picker_writes_nothing"/>.
    /// </summary>
    /// <remarks>
    /// If <see cref="TempHome.Snapshot"/> ever stopped seeing the tree, the cancel test would pass
    /// for the wrong reason forever. This one fails first.
    /// </remarks>
    [Fact]
    public async Task A_completed_create_does_change_the_directory()
    {
        using var model = NewModel();
        var before = _home.Snapshot();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, TempHome.Password);
        await model.CreateAsync();

        Assert.NotEqual(before, _home.Snapshot());
        Assert.Equal(2, _home.Snapshot().Count - before.Count);
    }

    [Fact]
    public async Task An_occupied_path_is_refused_and_the_file_there_is_untouched()
    {
        using (var existing = Vault.Create(_home.FreeVaultPath, "the other password"))
        {
            existing.AddEntry(new VaultEntry { Title = "keep me", Password = "secret" });
            existing.Save();
        }

        using var model = NewModel();
        var before = _home.Snapshot();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();

        Assert.False(model.IsCreating);
        Assert.Contains("already a file", model.Message, StringComparison.Ordinal);
        Assert.False(_session.IsUnlocked);
        Assert.Equal(before, _home.Snapshot());
        Assert.Empty(Remembered());

        using var untouched = Vault.Open(_home.FreeVaultPath, "the other password");
        Assert.Single(untouched.ReadEntries());
    }

    [Fact]
    public async Task A_mismatched_confirmation_is_refused_before_any_write()
    {
        using var model = NewModel();
        var before = _home.Snapshot();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, "something else");
        await model.CreateAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.False(_session.IsUnlocked);
        Assert.False(File.Exists(_home.FreeVaultPath));
        Assert.Contains("aren't the same", model.Message, StringComparison.Ordinal);
        Assert.Equal(before, _home.Snapshot());
        Assert.Empty(Remembered());
    }

    /// <summary>
    /// An empty password is refused by the rule, not merely by a disabled button.
    /// </summary>
    /// <remarks>
    /// <see cref="UnlockViewModel.CreateAsync"/> is driven directly here. Going through the command
    /// would prove only that <c>CanExecute</c> is false and would never reach the rule — so the
    /// button is asserted separately, below.
    /// </remarks>
    [Fact]
    public async Task An_empty_password_is_refused_before_any_write()
    {
        using var model = NewModel();
        var before = _home.Snapshot();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        await model.CreateAsync();

        Assert.Equal(0, _unlockedCalls);
        Assert.False(_session.IsUnlocked);
        Assert.False(File.Exists(_home.FreeVaultPath));
        Assert.Contains("needs a master password", model.Message, StringComparison.Ordinal);
        Assert.Equal(before, _home.Snapshot());
        Assert.Empty(Remembered());
    }

    [Fact]
    public async Task The_create_button_is_disabled_until_a_password_is_typed()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        Assert.False(model.CreateCommand.CanExecute(null));

        await model.StartCreateAsync();
        Assert.False(model.CreateCommand.CanExecute(null));

        model.TypeNew('a');
        Assert.True(model.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Recent_gains_the_vault_only_after_a_successful_create()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, "not the same");
        await model.CreateAsync();
        Assert.Empty(Remembered());

        Type(model, TempHome.Password, TempHome.Password);
        await model.CreateAsync();

        var remembered = Remembered();
        Assert.Single(remembered);
        Assert.Equal(_home.FreeVaultPath, remembered[0].Path, ignoreCase: true);
    }

    [Fact]
    public async Task Both_password_fields_are_emptied_after_a_create_and_after_a_refusal()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, "not the same");
        await model.CreateAsync();

        Assert.Equal(0, model.NewMaskedLength);
        Assert.Equal(0, model.ConfirmMaskedLength);

        Type(model, TempHome.Password, TempHome.Password);
        await model.CreateAsync();

        Assert.Equal(0, model.NewMaskedLength);
        Assert.Equal(0, model.ConfirmMaskedLength);
    }

    [Fact]
    public async Task Cancelling_the_form_forgets_both_passwords_and_leaves_the_open_controls()
    {
        using var model = NewModel();
        _picker.NewPath = _home.FreeVaultPath;

        await model.StartCreateAsync();
        Type(model, TempHome.Password, TempHome.Password);

        await model.CancelCreateAsync();

        Assert.False(model.IsCreating);
        Assert.True(model.IsOpening);
        Assert.Equal(0, model.NewMaskedLength);
        Assert.Equal(0, model.ConfirmMaskedLength);
        Assert.False(File.Exists(_home.FreeVaultPath));
        Assert.Empty(Remembered());
    }

    [Fact]
    public async Task A_cancelled_browse_selects_nothing()
    {
        using var model = NewModel();
        _picker.ExistingPath = null;

        await model.BrowseAsync();

        Assert.Equal(1, _picker.ExistingCalls);
        Assert.Null(model.SelectedPath);
        Assert.Equal(string.Empty, model.Message);
    }

    public void Dispose()
    {
        _session.Dispose();
        _home.Dispose();
    }

    private UnlockViewModel NewModel() =>
        new(_session, _home.Path, _picker, () => _unlockedCalls++);

    private IReadOnlyList<RecentVault> Remembered() =>
        RecentVaults.Load(KeypasteHome.RecentPath(_home.Path));

    private static void Type(UnlockViewModel model, string password, string confirmation)
    {
        model.ClearNew();
        model.ClearConfirm();

        foreach (var c in password)
        {
            model.TypeNew(c);
        }

        foreach (var c in confirmation)
        {
            model.TypeConfirm(c);
        }
    }
}
