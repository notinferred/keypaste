using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Storing a secret somebody already has, on both screens that take one.
/// </summary>
/// <remarks>
/// <para>
/// Before 4.9 the app could only invent a value, so every one of these journeys ended at
/// <c>keypaste add</c> in a terminal. The claim under test is the whole of that gap: what was typed
/// or pasted is what the file holds afterwards, and nothing of it survives in the screen.
/// </para>
/// <para>
/// <b>The reread is done through a second unlock rather than through the open session.</b> A field
/// that never reached the vault and a field whose value only lives in the view model are
/// indistinguishable while the same <see cref="Vault"/> is still in memory. Closing the session
/// drops it, so the value comes back off the disk or it does not come back at all.
/// </para>
/// <para>
/// <b>Every clearing path is asserted on <see cref="SecretField.IsZeroed"/> and not on the
/// length.</b> A buffer that reported zero characters while still holding them would satisfy the
/// screen, the mask and any test written against <c>MaskedLength</c>.
/// </para>
/// </remarks>
public sealed class ExistingSecretTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    // Long enough that a truncation shows, and no character of it appears in any label the screens
    // draw, so a value that leaked into one is not mistaken for a legitimate string.
    private const string _typed = "s3cr3t-from-the-provider-9c41ba";
    private const string _pasted = "pasted-sk_live_51Hx8zQ2eZvKYlo2C";

    private readonly string _directory;
    private readonly string _vaultPath;

    public ExistingSecretTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-existing-secret-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "the-old-one" });
        vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = "the-old-value", GroupPath = "env/billing" });
        vault.Save();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_typed_password_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "svc/api";
            context.Entries.GeneratePassword = false;
            Enter(context.Entries.NewPassword, _typed);
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        Assert.Equal(_typed, Reread("svc/api"));
    }

    [Fact]
    public async Task A_pasted_password_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            context.Clipboard.Plant(_pasted);

            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "svc/api";
            context.Entries.GeneratePassword = false;
            await context.Entries.NewPassword.Paste();
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
            Assert.Empty(context.Entries.NewPassword.Note);
        }

        Assert.Equal(_pasted, Reread("svc/api"));
    }

    /// <summary>
    /// A paste onto a half-typed value appends rather than replaces, which is what a field somebody
    /// is composing in has to do.
    /// </summary>
    [Fact]
    public async Task A_paste_lands_after_what_was_already_typed()
    {
        using (var context = New())
        {
            context.Clipboard.Plant(_pasted);

            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "svc/api";
            context.Entries.GeneratePassword = false;
            Enter(context.Entries.NewPassword, _typed);
            await context.Entries.NewPassword.Paste();
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        Assert.Equal(_typed + _pasted, Reread("svc/api"));
    }

    /// <summary>
    /// The refusal SECURITY.md describes, from the screen's side: it is said while the form is open
    /// and nothing is appended, rather than a different secret being stored quietly.
    /// </summary>
    [Fact]
    public async Task A_paste_no_keyboard_could_have_typed_is_refused_and_says_so()
    {
        using var context = New();
        context.Clipboard.Plant("before\u0007after");

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.GeneratePassword = false;
        await context.Entries.NewPassword.Paste();

        Assert.False(context.Entries.NewPassword.HasValue);
        Assert.Contains("no keyboard can type", context.Entries.NewPassword.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_clipboard_says_so_rather_than_nothing()
    {
        using var context = New();
        context.Clipboard.Plant(string.Empty);

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.GeneratePassword = false;
        await context.Entries.NewPassword.Paste();

        Assert.False(context.Entries.NewPassword.HasValue);
        Assert.Contains("nothing to paste", context.Entries.NewPassword.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// An entry with no password is still allowed, as it was before 4.9 and as KDBX permits.
    /// </summary>
    [Fact]
    public void An_untyped_password_makes_an_entry_with_none_rather_than_a_refusal()
    {
        using (var context = New())
        {
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "svc/api";
            context.Entries.GeneratePassword = false;
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        Assert.Equal(string.Empty, Reread("svc/api"));
    }

    [Fact]
    public void A_replacement_password_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            var detail = Github(context);

            detail.EditCommand.Execute(null);
            Enter(detail.NewPassword, _typed);
            detail.SaveCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        Assert.Equal(_typed, Reread("github"));
    }

    /// <summary>
    /// An empty replacement field is how somebody says they are not touching the password, so an
    /// edit to another field must not blank it.
    /// </summary>
    [Fact]
    public void An_edit_that_leaves_the_password_field_empty_keeps_the_password()
    {
        using (var context = New())
        {
            var detail = Github(context);

            detail.EditCommand.Execute(null);
            detail.DraftUsername = "someone-else";
            detail.SaveCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        Assert.Equal("the-old-one", Reread("github"));
    }

    [Fact]
    public void Turning_generation_back_on_discards_what_was_typed()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.GeneratePassword = false;
        Enter(context.Entries.NewPassword, _typed);

        context.Entries.GeneratePassword = true;

        Assert.False(context.Entries.NewPassword.HasValue);
        Assert.True(context.Entries.NewPassword.IsZeroed);
    }

    [Fact]
    public void The_buffer_is_empty_after_the_entry_is_saved()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "svc/api";
        context.Entries.GeneratePassword = false;
        Enter(context.Entries.NewPassword, _typed);
        context.Entries.ConfirmAddCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.True(context.Entries.NewPassword.IsZeroed);
    }

    [Fact]
    public void The_buffer_is_empty_after_the_add_form_is_cancelled()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.GeneratePassword = false;
        Enter(context.Entries.NewPassword, _typed);
        context.Entries.CancelAddCommand.Execute(null);

        Assert.True(context.Entries.NewPassword.IsZeroed);
    }

    [Fact]
    public void The_buffer_is_empty_after_an_edit_is_cancelled()
    {
        using var context = New();
        var detail = Github(context);

        detail.EditCommand.Execute(null);
        Enter(detail.NewPassword, _typed);
        detail.CancelCommand.Execute(null);

        Assert.True(detail.NewPassword.IsZeroed);
    }

    [Fact]
    public void The_buffers_are_empty_after_the_vault_locks()
    {
        using var context = New();
        var detail = Github(context);

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.GeneratePassword = false;
        Enter(context.Entries.NewPassword, _typed);

        detail.EditCommand.Execute(null);
        Enter(detail.NewPassword, _pasted);

        context.Session.Lock(VaultLockReason.Manual);
        context.Entries.Reload();

        Assert.True(context.Entries.NewPassword.IsZeroed);
        Assert.True(detail.NewPassword.IsZeroed);
    }

    [Fact]
    public void A_typed_variable_value_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            var project = Billing(context);

            project.BeginAddCommand.Execute(null);
            project.NewKey = "DATABASE_URL";
            project.GenerateValue = false;
            Enter(project.NewValue, _typed);
            project.ConfirmAddCommand.Execute(null);

            Assert.Null(context.EnvSets.Error);
        }

        Assert.Equal(_typed, Reread("env/billing/DATABASE_URL"));
    }

    [Fact]
    public async Task A_pasted_variable_value_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            context.Clipboard.Plant(_pasted);
            var project = Billing(context);

            project.BeginAddCommand.Execute(null);
            project.NewKey = "DATABASE_URL";
            project.GenerateValue = false;
            await project.NewValue.Paste();
            project.ConfirmAddCommand.Execute(null);

            Assert.Null(context.EnvSets.Error);
        }

        Assert.Equal(_pasted, Reread("env/billing/DATABASE_URL"));
    }

    [Fact]
    public void A_replacement_variable_value_is_what_the_vault_holds_afterwards()
    {
        using (var context = New())
        {
            var project = Billing(context);

            project.BeginReplace(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
            Enter(project.ReplacementValue, _typed);
            project.ConfirmReplaceCommand.Execute(null);

            Assert.Null(context.EnvSets.Error);
            Assert.False(project.IsReplacing);
        }

        Assert.Equal(_typed, Reread("env/billing/STRIPE_KEY"));
    }

    /// <summary>
    /// The mask on the row is redrawn from the value that was just written, rather than staying at
    /// the old length until something reloads the project.
    /// </summary>
    [Fact]
    public void A_replaced_row_shows_the_new_length()
    {
        using var context = New();
        var project = Billing(context);
        var row = project.Variables.Single(variable => variable.Key == "STRIPE_KEY");

        Assert.Equal("the-old-value".Length, row.MaskedLength);

        project.BeginReplace(row);
        Enter(project.ReplacementValue, _typed);
        project.ConfirmReplaceCommand.Execute(null);

        Assert.Equal(_typed.Length, row.MaskedLength);
    }

    [Fact]
    public void The_variable_buffers_are_empty_after_a_save_a_cancel_and_a_lock()
    {
        using var context = New();
        var project = Billing(context);

        project.BeginReplace(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Enter(project.ReplacementValue, _typed);
        project.ConfirmReplaceCommand.Execute(null);

        Assert.Null(context.EnvSets.Error);
        Assert.True(project.ReplacementValue.IsZeroed);

        project.BeginAddCommand.Execute(null);
        project.GenerateValue = false;
        Enter(project.NewValue, _typed);
        project.CancelAddCommand.Execute(null);

        Assert.True(project.NewValue.IsZeroed);

        project.BeginReplace(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Enter(project.ReplacementValue, _pasted);

        context.Session.Lock(VaultLockReason.Manual);
        context.EnvSets.Reload();

        Assert.True(project.ReplacementValue.IsZeroed);
        Assert.True(project.NewValue.IsZeroed);
    }

    /// <summary>
    /// The row a replace form was opened on can be gone by the time Replace is pressed, and a
    /// created variable is not the replacement that was asked for.
    /// </summary>
    [Fact]
    public void Replacing_a_variable_something_else_removed_says_so()
    {
        using var context = New();
        var project = Billing(context);
        var row = project.Variables.Single(variable => variable.Key == "STRIPE_KEY");

        project.BeginRemove(row);
        project.ConfirmRemoveCommand.Execute(null);

        project.BeginReplace(row);
        Enter(project.ReplacementValue, _typed);
        project.ConfirmReplaceCommand.Execute(null);

        Assert.Contains("was not in", context.EnvSets.Error ?? string.Empty, StringComparison.Ordinal);
    }

    private static void Enter(SecretField field, string value)
    {
        foreach (var c in value)
        {
            field.Type(c);
        }
    }

    private static EntryDetailViewModel Github(Context context)
    {
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        return context.Entries.Detail!;
    }

    private static EnvProjectViewModel Billing(Context context)
    {
        context.EnvSets.OpenCommand.Execute("billing");
        return context.EnvSets.OpenProject!;
    }

    /// <summary>Opens the file again, from nothing, and reads one entry's password.</summary>
    private string? Reread(string path)
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.Find(path)?.Password;
    }

    private Context New() => new(_vaultPath);

    /// <summary>An unlocked session with both screens on it, built the way the shell builds them.</summary>
    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock());
            Entries = new EntriesViewModel(Session, Countdown);
            EnvSets = new EnvSetsViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal EnvSetsViewModel EnvSets { get; }

        internal FakeClipboard Clipboard { get; }

        public void Dispose()
        {
            EnvSets.Dispose();
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
