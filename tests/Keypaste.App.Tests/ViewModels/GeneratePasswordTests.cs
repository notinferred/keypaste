using Avalonia.Controls;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// A password generated where it goes, or on its own (V.12): into the field it is for, a filled one written only by its
/// form's Save with the old value kept in history, and from Generate password… copied through the clearing clipboard,
/// made a new item's password or added to the chosen item as a field, with nothing generated in the automation tree.
/// </summary>
public sealed class GeneratePasswordTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _checking = new("Banking", "Checking");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-generate-").FullName;
    private readonly string _vaultPath;

    public GeneratePasswordTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");
        KeePassInterop.WriteForeignUnchecked(_vaultPath, System.Text.Encoding.UTF8.GetBytes(_master), null, "AES-KDF", "AES-256");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_generated_value_fills_its_field_and_what_is_typed_replaces_it_whole()
    {
        using var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
        using var field = new SecretField(countdown);
        var recipe = new GeneratorViewModel().Recipe!.Value;
        var replaced = 0;
        field.GeneratedValueReplaced += (_, _) => replaced++;

        field.Type('a');
        field.Generate(recipe);

        Assert.True(field.IsGenerated);
        Assert.Equal(PasswordGenerator.DefaultLength, field.MaskedLength);

        field.Type('x');

        Assert.False(field.IsGenerated);
        Assert.Equal("x", field.Compose());
        Assert.Equal(1, replaced);

        field.Generate(recipe);
        field.Backspace();

        Assert.Equal(0, field.MaskedLength);
        Assert.False(field.IsGenerated);
    }

    [Fact]
    public void Generate_on_the_saved_password_opens_Edit_with_one_and_Save_keeps_the_old_in_history()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var before = context.Vault.Find(_checking)!.Password;
        var revisions = context.Vault.ReadHistory(_checking)!.Count;

        detail.GeneratePasswordCommand.Execute(null);

        Assert.True(detail.IsEditing);
        Assert.True(detail.NewPassword.IsGenerated);
        Assert.Equal(PasswordGenerator.DefaultLength, detail.NewPassword.MaskedLength);

        detail.SaveCommand.Execute(null);

        var after = context.Vault.Find(_checking)!.Password;
        Assert.NotEqual(before, after);
        Assert.Equal(PasswordGenerator.DefaultLength, after.Length);
        var history = context.Vault.ReadHistory(_checking)!;
        Assert.Equal(revisions + 1, history.Count);
        Assert.Contains(history, revision => revision.Fields.Password == before);
    }

    [Fact]
    public void Generate_on_a_protected_field_fills_its_replace_form_follows_the_choices_and_writes_on_Replace()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_checking);
        var pin = detail.Fields.Single(field => field.Name == "PIN");

        Assert.True(pin.CanGenerate);
        Assert.False(detail.Fields.Single(field => field.Name == "otp").CanGenerate);

        pin.GenerateCommand.Execute(null);

        Assert.Same(pin, detail.ReplacingField);
        Assert.True(detail.ReplacementFieldValue.IsGenerated);
        var characters = detail.ReplacementFieldValue.MaskedLength;

        detail.Generator.UseWords = true;

        Assert.True(detail.ReplacementFieldValue.IsGenerated);
        Assert.NotEqual(characters, detail.ReplacementFieldValue.MaskedLength);

        detail.ConfirmReplaceFieldCommand.Execute(null);

        var written = context.Vault.ReadField(_checking, "PIN");
        Assert.NotEqual("4321", written);
        Assert.Contains(PasswordGenerator.DefaultSeparator, written!);
    }

    [Fact]
    public Task Generate_password_copies_through_the_countdown_and_starts_a_new_item_with_it() => HeadlessSession.On(async () =>
    {
        using var vault = new TempVault();
        using var session = Unlocked(vault);
        var clipboard = new FakeClipboard();
        using var shell = new ShellViewModel(session, vault.Home, null, clipboard: clipboard, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());

        shell.OpenGeneratorCommand.Execute(null);
        var dialog = shell.GeneratorDialog!;
        await dialog.CopyCommand.ExecuteAsync(null);

        Assert.Equal(PasswordGenerator.DefaultLength, clipboard.Content!.Length);
        Assert.True(clipboard.ContentWasSetAsASecret);

        dialog.NewItemCommand.Execute(null);

        Assert.Null(shell.GeneratorDialog);
        var form = Assert.IsType<EntriesViewModel>(shell.Content).NewItem!;
        Assert.Equal(clipboard.Content, form.Password.Compose());
        Assert.True(dialog.Value.IsZeroed);
    });

    [Fact]
    public Task Generate_password_adds_its_value_to_the_chosen_item_as_a_protected_field() => HeadlessSession.On(() =>
    {
        using var vault = new TempVault();
        using var session = Unlocked(vault);
        using var shell = new ShellViewModel(session, vault.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        items.Selected = items.Rows.Single();

        shell.OpenGeneratorCommand.Execute(null);
        var dialog = shell.GeneratorDialog!;

        Assert.True(dialog.CanAddField);
        Assert.Equal("example", dialog.Item);
        Assert.False(dialog.AddFieldCommand.CanExecute(null));

        dialog.FieldName = "API_TOKEN";
        var value = dialog.Reveal();
        dialog.AddFieldCommand.Execute(null);

        var example = new EntryName(string.Empty, "example");
        Assert.Null(shell.GeneratorDialog);
        Assert.Equal(value, session.Unlocked!.ReadField(example, "API_TOKEN"));
        Assert.True(session.Unlocked.Fields(example)!.Single(field => field.Name == "API_TOKEN").IsProtected);
    });

    [Fact]
    public Task The_dialog_holds_the_shell_still_and_draws_no_value_into_the_automation_tree() => HeadlessSession.On(() =>
    {
        using var vault = new TempVault();
        using var session = Unlocked(vault);
        using var shell = new ShellViewModel(session, vault.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var commands = new AppCommands(new AppCommandHost { Session = session, Shell = () => shell, Unlock = () => null });

        shell.OpenGeneratorCommand.Execute(null);
        var dialog = shell.GeneratorDialog!;

        Assert.False(commands.Menus.SelectMany(menu => menu.Entries).OfType<AppCommand>().Single(command => command.Header == "New item").CanRun());

        var window = new Window { Content = new GeneratePasswordView { DataContext = dialog } };
        window.Show();
        AutomationSurface.AssertNothingExposes(window, dialog.Reveal()!);
        window.Close();
    });

    private static AppVaultSession Unlocked(TempVault vault)
    {
        var session = new AppVaultSession(new ManualClock(AppClock.Start), home: vault.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(vault.Path_, master.Value));
        return session;
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal Vault Vault => Session.Unlocked!;

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal EntryDetailViewModel Open(EntryName name)
        {
            Entries.Selected = Entries.Rows.Single(row => row.Title == name.Title && row.GroupPath == name.GroupPath);
            return Entries.Detail!;
        }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
