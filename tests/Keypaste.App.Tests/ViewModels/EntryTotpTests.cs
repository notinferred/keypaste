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
/// code only at a hold or a copy, and holds neither the code nor the seed (9.2b).
/// </summary>
public sealed class EntryTotpTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    // RFC 6238 Appendix B's SHA-1 seed, whose 8-digit codes are 07081804 at 1111111109 s, with 1 s left, and 14050471 at 1111111111 s.
    private const string _seed = "12345678901234567890";
    private const string _codeAt09 = "07081804";
    private const string _codeAt11 = "14050471";

    private static readonly EntryName _github = new("Work", "GitHub");
    private static readonly EntryName _broken = new("Work", "Broken");
    private static readonly EntryName _plain = new("Work", "Plain");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-entry-totp-").FullName;
    private readonly string _vaultPath;

    public EntryTotpTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);

        foreach (var name in new[] { _github, _broken, _plain })
        {
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "SENTINEL-9-2B-APP-PASSWORD" });
        }

        Assert.True(vault.SetFields(_github, [new FieldWrite("TimeOtp-Secret", _seed), new FieldWrite("TimeOtp-Length", "8")]));
        vault.SetFieldsUnchecked(_broken, [new FieldWrite(TotpSpec.OtpField, "otpauth://hotp/Broken?secret=GEZDGNBVGY3TQOJQ&counter=1")]);
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

        detail.EditCommand.Execute(null);
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
    public Task A_held_code_is_redrawn_as_the_next_code_when_its_period_ends() => HeadlessSession.On(() =>
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

        await totp.CopyCommand.ExecuteAsync();

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
