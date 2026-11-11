using System.Reflection;
using Keypaste.App.Clipboard;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The entry pane shows an item's TOTP as a masked code and the seconds until the next one, computes the
/// code only at a hold or a copy, and holds neither the code nor the seed (9.2b); its one-time code is added,
/// replaced and removed through core's TOTP write.
/// </summary>
public sealed class EntryTotpTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    // RFC 6238 Appendix B's SHA-1 seed, whose 8-digit codes are 07081804 at 1111111109 s, with 1 s left, and 14050471 at 1111111111 s.
    private const string _seed = "12345678901234567890";
    private const string _codeAt09 = "07081804";
    private const string _codeAt11 = "14050471";
    private const string _seedBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly EntryName _github = new("Work", "GitHub");
    private static readonly EntryName _broken = new("Work", "Broken");
    private static readonly EntryName _plain = new("Work", "Plain");
    private static readonly EntryName _keePassXc = new("Work", "KeePassXC");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-entry-totp-").FullName;
    private readonly string _vaultPath;

    public EntryTotpTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);

        foreach (var name in new[] { _github, _broken, _plain, _keePassXc })
        {
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "SENTINEL-9-2B-APP-PASSWORD" });
        }

        Assert.True(vault.SetFields(_github, [new FieldWrite("TimeOtp-Secret", _seed), new FieldWrite("TimeOtp-Length", "8")]));
        vault.SetFieldsUnchecked(_broken, [new FieldWrite(TotpSpec.OtpField, "otpauth://hotp/Broken?secret=GEZDGNBVGY3TQOJQ&counter=1")]);
        Assert.True(vault.SetTotp(_keePassXc, _seedBase32));
        vault.SetFields(_keePassXc, [new FieldWrite("PIN", "4321")]);
        vault.Save();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_item_with_a_TOTP_shows_a_masked_code_and_its_countdown_and_holds_neither_code_nor_seed()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_github);
        var totp = detail.Totp!;

        Assert.True(detail.ShowsTotp);
        Assert.False(detail.ShowsTotpProblem);
        Assert.Equal(8, totp.MaskedLength);
        Assert.Equal(1, totp.SecondsLeft);
        Assert.Equal("1s", totp.Countdown);

        Assert.All(Strings(detail).Concat(Strings(totp)), text =>
        {
            Assert.DoesNotContain(_codeAt09, text, StringComparison.Ordinal);
            Assert.DoesNotContain(_seed, text, StringComparison.Ordinal);
        });

        Assert.Equal(_codeAt09, totp.Reveal());

        detail.EditCommand.Press();
        Assert.False(detail.ShowsTotp);
    }

    [Fact]
    public void The_countdown_follows_the_clock_and_the_next_hold_reads_the_next_code()
    {
        using var context = new Context(_vaultPath);
        var totp = context.Open(_github).Totp!;
        List<string?> raised = [];
        totp.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        context.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(29, totp.SecondsLeft);
        Assert.Equal("29s", totp.Countdown);
        Assert.Contains(nameof(EntryTotpRow.Countdown), raised);
        Assert.Equal(_codeAt11, totp.Reveal());
    }

    [Fact]
    public void The_row_says_its_code_moved_on_when_the_period_ends_and_not_before()
    {
        using var context = new Context(_vaultPath);
        var totp = context.Open(_github).Totp!;
        var changes = 0;
        totp.Changed += (_, _) => changes++;

        context.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, changes);
        Assert.Equal(30, totp.SecondsLeft);

        context.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, changes);
        Assert.Equal(_codeAt11, totp.Reveal());
    }

    [Fact]
    public Task A_shown_code_is_redrawn_as_the_next_code_when_its_period_ends() => HeadlessSession.On(() =>
    {
        using var context = new Context(_vaultPath);
        var totp = context.Open(_github).Totp!;
        var cell = new RevealedValue { Source = totp, MaskedLength = totp.MaskedLength };

        cell.BeginReveal();
        Assert.Equal(_codeAt09, cell.Rendered);

        context.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(_codeAt11, cell.Rendered);

        cell.EndReveal();
        Assert.False(cell.IsRevealed);
    });

    [Fact]
    public async Task Copy_puts_the_current_code_through_the_clipboard_countdown()
    {
        using var context = new Context(_vaultPath);
        var totp = context.Open(_github).Totp!;

        await totp.CopyCommand.PressAsync();

        Assert.Equal(_codeAt09, context.Clipboard.Content);
        Assert.True(context.Clipboard.ContentWasSetAsASecret);
        Assert.True(context.Countdown.IsCounting);
        Assert.Equal("TOTP code", context.Countdown.Label);
    }

    [Fact]
    public void A_TOTP_keypaste_refuses_says_why_and_offers_no_code()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_broken);

        Assert.Null(detail.Totp);
        Assert.False(detail.ShowsTotp);
        Assert.True(detail.ShowsTotpProblem);
        Assert.Equal(
            "keypaste can't show this item's TOTP code. The otp field holds a counter-based HOTP, which keypaste does not compute. Correct that field to see the code here.",
            detail.TotpProblem);
    }

    [Fact]
    public void An_item_with_a_readable_otp_lists_no_otp_custom_field_and_keeps_its_other_fields()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_keePassXc);

        Assert.True(detail.ShowsTotp);
        Assert.Equal(["PIN"], detail.ListedFields.Select(row => row.Name));
        Assert.True(detail.HasListedFields);
        Assert.Contains(detail.Fields, row => row.Name == TotpSpec.OtpField);
        Assert.Contains(context.Session.Unlocked!.Fields(_keePassXc)!, field => field.Name == TotpSpec.OtpField);
        Assert.True(detail.CanRemoveTotp);

        var keePass2 = context.Open(_github);

        Assert.True(keePass2.ShowsTotp);
        Assert.Empty(keePass2.ListedFields);
        Assert.False(keePass2.HasListedFields);
    }

    [Fact]
    public void An_item_whose_otp_cannot_be_read_keeps_the_explanation_and_lists_its_otp_field()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_broken);

        Assert.True(detail.ShowsTotpProblem);
        Assert.Equal([TotpSpec.OtpField], detail.ListedFields.Select(row => row.Name));
    }

    [Fact]
    public void An_item_without_a_TOTP_has_no_TOTP_row()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_plain);

        Assert.Null(detail.Totp);
        Assert.False(detail.ShowsTotp);
        Assert.False(detail.ShowsTotpProblem);
    }

    [Fact]
    public void Letting_go_of_the_pane_stops_the_countdown()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_github);
        var totp = detail.Totp!;

        detail.Dispose();
        context.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(detail.Totp);
        Assert.Equal(1, totp.SecondsLeft);
    }

    [Fact]
    public void Add_one_time_code_sets_up_an_item_without_one_and_its_code_shows_at_once()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_plain);

        Assert.True(detail.CanAddTotp);
        detail.BeginSetTotpCommand.Press();
        Assert.True(detail.IsSettingTotp);
        Assert.Equal("this one-time code", Typed(detail, _seedBase32));
        Assert.All(Strings(detail), text => Assert.DoesNotContain(_seedBase32, text, StringComparison.Ordinal));

        detail.ConfirmSetTotpCommand.Press();

        Assert.False(detail.IsSettingTotp);
        Assert.True(detail.NewTotp.IsZeroed);
        Assert.False(detail.CanAddTotp);
        Assert.True(detail.ShowsTotp);
        Assert.Equal(6, detail.Totp!.MaskedLength);
        Assert.Equal(_codeAt09[2..], detail.Totp.Reveal());

        using var vault = Vault.Open(_vaultPath, _master);
        Assert.Equal($"otpauth://totp/Plain?secret={_seedBase32}&period=30&digits=6&issuer=Plain", vault.ReadField(_plain, TotpSpec.OtpField));
        Assert.Single(vault.ReadHistory(_plain)!);
    }

    [Fact]
    public void Replace_writes_a_new_one_time_code_over_one_keypaste_refused()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_broken);

        Assert.False(detail.CanAddTotp);
        detail.BeginSetTotpCommand.Press();
        Typed(detail, $"otpauth://totp/Broken?secret={_seedBase32}&digits=8");
        detail.ConfirmSetTotpCommand.Press();

        Assert.Null(detail.TotpProblem);
        Assert.Equal(_codeAt09, detail.Totp!.Reveal());
    }

    [Fact]
    public void A_setup_core_refuses_says_why_keeps_the_form_open_and_writes_nothing()
    {
        var before = File.ReadAllBytes(_vaultPath);
        using var context = new Context(_vaultPath);
        var detail = context.Open(_plain);

        detail.BeginSetTotpCommand.Press();
        Typed(detail, "otpauth://hotp/Plain?secret=GEZDGNBVGY3TQOJQ&counter=1");
        detail.ConfirmSetTotpCommand.Press();

        Assert.True(detail.IsSettingTotp);
        Assert.True(detail.HasTotpSetupProblem);
        Assert.Equal("The link holds a counter-based HOTP, which keypaste does not compute.", detail.TotpSetupProblem);
        Assert.Null(detail.Totp);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));

        detail.CancelSetTotpCommand.Press();

        Assert.False(detail.IsSettingTotp);
        Assert.False(detail.HasTotpSetupProblem);
        Assert.True(detail.NewTotp.IsZeroed);
    }

    [Fact]
    public void Remove_asks_first_and_then_removes_the_one_time_code_keeping_it_in_the_history()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_broken);

        detail.BeginRemoveTotpCommand.Press();
        Assert.True(detail.IsRemovingTotp);
        detail.CancelRemoveTotpCommand.Press();
        Assert.False(detail.IsRemovingTotp);
        Assert.NotNull(detail.TotpProblem);

        detail.BeginRemoveTotpCommand.Press();
        detail.ConfirmRemoveTotpCommand.Press();

        Assert.False(detail.IsRemovingTotp);
        Assert.Null(detail.TotpProblem);
        Assert.True(detail.CanAddTotp);
        Assert.DoesNotContain(detail.Fields, row => row.Name == TotpSpec.OtpField);

        using var vault = Vault.Open(_vaultPath, _master);
        Assert.Null(vault.ReadField(_broken, TotpSpec.OtpField));
        Assert.Contains(vault.ReadHistory(_broken)!, revision => revision.CustomFields.Any(field => field.Name == TotpSpec.OtpField));
    }

    [Fact]
    public void Remove_is_offered_only_for_the_otp_fields_it_removes_and_not_for_KeePass_2s_alone()
    {
        using var context = new Context(_vaultPath);
        var keePass2 = context.Open(_github);

        Assert.True(keePass2.ShowsTotp);
        Assert.False(keePass2.CanRemoveTotp);
        Assert.False(keePass2.BeginRemoveTotpCommand.CanExecute(null));
        Assert.True(keePass2.BeginSetTotpCommand.CanExecute(null));

        var keePassXc = context.Open(_broken);

        Assert.True(keePassXc.CanRemoveTotp);
        Assert.True(keePassXc.BeginRemoveTotpCommand.CanExecute(null));

        var plain = context.Open(_plain);

        Assert.False(plain.CanRemoveTotp);
        plain.BeginSetTotpCommand.Press();
        Typed(plain, _seedBase32);
        plain.ConfirmSetTotpCommand.Press();

        Assert.True(plain.CanRemoveTotp);
    }

    [Fact]
    public void Opening_another_field_form_closes_the_one_time_code_form_and_clears_what_was_typed()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_plain);

        detail.BeginSetTotpCommand.Press();
        Typed(detail, _seedBase32);
        Assert.True(detail.HasOwnPrimary);

        detail.BeginAddFieldCommand.Press();

        Assert.True(detail.IsAddingField);
        Assert.False(detail.IsSettingTotp);
        Assert.True(detail.NewTotp.IsZeroed);

        detail.BeginSetTotpCommand.Press();

        Assert.False(detail.IsAddingField);
        Assert.True(detail.IsSettingTotp);
    }

    [Fact]
    public void Editing_offers_no_one_time_code_change()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(_plain);

        detail.EditCommand.Press();

        Assert.False(detail.CanAddTotp);
        Assert.False(detail.BeginSetTotpCommand.CanExecute(null));
        Assert.False(detail.BeginRemoveTotpCommand.CanExecute(null));
    }

    /// <summary>Types a setup into the open one-time code form, and answers what leaving the form would lose.</summary>
    private static string? Typed(EntryDetailViewModel detail, string setup)
    {
        foreach (var c in setup)
        {
            detail.NewTotp.Type(c);
        }

        return detail.UnsavedDraft;
    }

    /// <summary>Every string property a model answers, skipping one that refuses in its state.</summary>
    private static IEnumerable<string> Strings(object model)
    {
        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            string? text;

            try
            {
                text = (string?)property.GetValue(model);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            yield return text ?? string.Empty;
        }
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(Clock);

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(Clipboard, new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown);
        }

        /// <summary>The session's clock, at RFC 6238's 1111111109 s.</summary>
        internal ManualClock Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(1111111109));

        internal AppVaultSession Session { get; }

        internal FakeClipboard Clipboard { get; } = new();

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
