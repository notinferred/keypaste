using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;
using static System.FormattableString;

namespace Keypaste.App.Tests.Session;

/// <summary>Dates and sorting follow the culture, the words come from the resources, and right to left mirrors the app but no secret.</summary>
public sealed partial class LanguageAndRegionTests
{
#pragma warning disable CA2255
    /// <summary>Tests read fixed words and times whatever machine runs them; a test here names the culture it means.</summary>
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RunUnderNoCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    private static readonly DateTimeOffset _at = new(2026, 11, 5, 15, 4, 9, TimeSpan.Zero);

    private static string Under(string culture, Func<string> read)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_moment_reads_in_the_cultures_own_way()
    {
        Assert.Equal("2026-11-05 15:04", PersonTime.Moment(_at), StringComparer.Ordinal);
        Assert.Equal("05.11.2026 15:04", Under("de-DE", () => PersonTime.Moment(_at)), StringComparer.Ordinal);
        Assert.StartsWith("11/5/2026", Under("en-US", () => PersonTime.Moment(_at)), StringComparison.Ordinal);
        Assert.DoesNotContain("2026", Under("ar-SA", () => PersonTime.Moment(_at)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ar-SA", 1900, 4, 29)]
    [InlineData("ar-SA", 2077, 11, 17)]
    [InlineData("ar-SA", 9999, 12, 31)]
    [InlineData("ar-SA", 1, 1, 1)]
    [InlineData("fa-IR", 1, 1, 1)]
    public void A_date_outside_the_cultures_calendar_reads_as_iso(string culture, int year, int month, int day)
    {
        var at = new DateTimeOffset(year, month, day, 15, 4, 9, TimeSpan.Zero);
        var iso = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal(iso + " 15:04", Under(culture, () => PersonTime.Moment(at)), StringComparer.Ordinal);
        Assert.Equal(iso + " 15:04:09", Under(culture, () => PersonTime.MomentWithSeconds(at)), StringComparer.Ordinal);
        Assert.Equal(iso, Under(culture, () => PersonTime.Date(at)), StringComparer.Ordinal);
        Assert.Equal("15:04", Under(culture, () => PersonTime.Clock(at)), StringComparer.Ordinal);
        Assert.Equal("15:04:09", Under(culture, () => PersonTime.ClockWithSeconds(at)), StringComparer.Ordinal);
        Assert.Equal(iso, Under(culture, () => PersonTime.Day(at.DateTime, at.DateTime)), StringComparer.Ordinal);
    }

    [Fact]
    public void Every_culture_reads_every_date_its_calendar_holds_and_the_ends_of_time()
    {
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var calendar = culture.DateTimeFormat.Calendar;

            foreach (var at in new[] { DateTime.MinValue, calendar.MinSupportedDateTime, calendar.MaxSupportedDateTime, DateTime.MaxValue })
            {
                var moment = new DateTimeOffset(at, TimeSpan.Zero);

                Under(culture.Name, () => PersonTime.MomentWithSeconds(moment) + PersonTime.Moment(moment) + PersonTime.Date(moment) + PersonTime.Clock(moment) + PersonTime.ClockWithSeconds(moment) + PersonTime.Day(at, at));
            }
        }
    }

    [Fact]
    public void A_right_to_left_date_keeps_its_order_inside_left_to_right_text()
    {
        var moment = Under("ar-SA", () => PersonTime.MomentWithSeconds(_at));

        Assert.StartsWith("\u200F", moment, StringComparison.Ordinal);
        Assert.EndsWith("\u200F", moment, StringComparison.Ordinal);
        Assert.DoesNotContain("\u200F", Under("de-DE", () => PersonTime.MomentWithSeconds(_at)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_day_reads_in_the_culture_and_its_year_is_kept()
    {
        var day = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal("Monday, March 2", PersonTime.Day(day, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Unspecified)), StringComparer.Ordinal);
        Assert.Contains("März", Under("de-DE", () => PersonTime.Day(day, day)), StringComparison.Ordinal);
    }

    [Fact]
    public void Relative_times_come_from_the_resources()
    {
        Assert.Equal("4m ago", UseText.Ago(_at, _at.AddMinutes(4)), StringComparer.Ordinal);
        Assert.Equal("3h ago", UseText.Ago(_at, _at.AddHours(3)), StringComparer.Ordinal);
        Assert.Equal("2d ago", UseText.Ago(_at, _at.AddDays(2)), StringComparer.Ordinal);
        Assert.Equal(Strings.TimeJustNow, UseText.Ago(_at, _at), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("de-DE", "item 2", "item 10")]
    [InlineData("en-US", "item 2", "item 10")]
    [InlineData("de-DE", "é", "z")]
    [InlineData("en-US", "apple", "Banana")]
    [InlineData("tr-TR", "çay", "dağ")]
    public void A_table_sorts_as_the_culture_reads(string culture, string first, string second) =>
        Assert.Equal("before", Under(culture, () => TableSorting.CompareText(first, second) < 0 ? "before" : "after"), StringComparer.Ordinal);

    [Fact]
    public void Every_resource_has_words_and_every_label_is_used()
    {
        var root = RepoRoot();
        var sources = string.Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "Keypaste.App"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".axaml", StringComparison.Ordinal) || (path.EndsWith(".cs", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal)))
            .Select(File.ReadAllText));

        var labels = Labels();
        Assert.True(labels.Count >= 450, "the views' labels live in the resources");

        foreach (var (key, words) in labels)
        {
            Assert.False(string.IsNullOrWhiteSpace(words), key);
            Assert.True(sources.Contains("Strings." + key, StringComparison.Ordinal), key + " is used nowhere");
        }
    }

    [Fact]
    public void Every_label_with_a_hole_formats_with_every_hole_filled()
    {
        foreach (var (key, words) in Labels().Where(label => label.Value.Contains('{', StringComparison.Ordinal)))
        {
            var filled = string.Format(CultureInfo.InvariantCulture, words, 7, 8, 9);

            Assert.DoesNotContain("{", filled, StringComparison.Ordinal);
            Assert.True(filled.Contains('7', StringComparison.Ordinal), key);
        }
    }

    [Fact]
    public void Every_word_a_view_shows_comes_from_the_resources()
    {
        var kept = new HashSet<string>(StringComparer.Ordinal)
        {
            "keypaste", "API_KEY", "Work/github", "~/code/acme-api", "db.example.com:5432", "env:project:prod", "https://", "OPENAI_API_KEY", "STRIPE_SECRET_KEY", "billing-api", "npm start", "staging", "STRIPE_KEY", "FIELD_NAME", "ci-staging", "read:acme-api/staging/*",
        };
        var prompts = new[] { "ApprovalWindow", "ItemPickWindow", "EnvApprovalWindow", "RunApprovalWindow", "PairingWindow", "PairingNoticeWindow", "AccessChangeWindow", "TerminalEditWindow", "LockedNoticeWindow", "TermsWindow" };
        var app = Path.Combine(RepoRoot(), "src", "Keypaste.App");

        var literals = Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(view => !prompts.Contains(Path.GetFileNameWithoutExtension(view), StringComparer.Ordinal))
            .SelectMany(view => LiteralLabel().Matches(File.ReadAllText(view)).Select(found => (view: Path.GetFileName(view), words: found.Groups[1].Value)))
            .Where(found => !kept.Contains(found.words))
            .ToList();

        Assert.Empty(literals);
    }

    [Fact]
    public void No_view_asks_for_a_label_the_resources_do_not_have()
    {
        var keys = Labels().Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var view in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Keypaste.App"), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match used in StaticLabel().Matches(File.ReadAllText(view)))
            {
                Assert.Contains(used.Groups[1].Value, keys);
            }
        }
    }

    [Fact]
    public void The_generated_class_has_exactly_the_labels_in_the_file()
    {
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(property => property.Name);

        Assert.Equal(Labels().Keys.Order(StringComparer.Ordinal), properties.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_security_prompts_copy_stays_where_it_is()
    {
        var views = Path.Combine(RepoRoot(), "src", "Keypaste.App", "Views");

        foreach (var prompt in new[] { "ApprovalWindow", "EnvApprovalWindow", "RunApprovalWindow", "PairingWindow", "PairingNoticeWindow", "AccessChangeWindow", "TerminalEditWindow", "LockedNoticeWindow", "TermsWindow" })
        {
            Assert.DoesNotContain("res:Strings", File.ReadAllText(Path.Combine(views, prompt + ".axaml")), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_a_time_goes_through_a_resources_format()
    {
        var app = Path.Combine(RepoRoot(), "src", "Keypaste.App");

        var formatted = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && FormatOfALabel().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path)).ToList();

        Assert.Equal(["UseText.cs"], formatted);

        foreach (var view in Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match bound in BoundThroughALabel().Matches(File.ReadAllText(view)))
            {
                Assert.DoesNotMatch("(?i)value|password|secret|token", bound.Groups[1].Value);
            }
        }
    }

    [Fact]
    public Task Right_to_left_mirrors_the_shell_and_what_points_a_way_and_leaves_machine_text_alone() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        using var shell = new RenderedShell("keypaste-rtl-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Selected = items.Rows[0];
        RenderedShell.Drain();

        Assert.Equal(FlowDirection.RightToLeft, shell.Window.FlowDirection);

        var icons = shell.Window.GetVisualDescendants().OfType<KpIcon>().ToList();
        Assert.Contains(icons, icon => icon.Icon is "chevron-right" or "chevron-down" or "panel-left");
        Assert.All(icons.Where(icon => icon.Icon is "chevron-right" or "chevron-left" or "panel-left"), icon => Assert.True(icon.HasMirrorTransform == false, icon.Icon));
        Assert.All(icons.Where(icon => icon.Icon is "lock" or "key-round" or "search"), icon => Assert.True(icon.HasMirrorTransform, icon.Icon));

        Assert.All(shell.Window.GetVisualDescendants().OfType<TextBlock>(), text => Assert.Equal(FlowDirection.LeftToRight, text.FlowDirection));

        Assert.All(shell.Window.GetVisualDescendants().OfType<RevealedValue>(), value => Assert.Equal(FlowDirection.LeftToRight, value.FlowDirection));
        Assert.All(shell.Window.GetVisualDescendants().OfType<MaskedInput>(), input => Assert.Equal(FlowDirection.LeftToRight, input.FlowDirection));
    });

    [Fact]
    public Task A_prompt_window_keeps_left_to_right_under_a_right_to_left_culture() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        var notice = new NoticeWindow();
        notice.Show();
        Assert.Equal(FlowDirection.LeftToRight, notice.FlowDirection);
        Assert.All(notice.GetVisualDescendants().OfType<TextBlock>(), text => Assert.NotEqual(TextAlignment.Right, text.TextAlignment));
        notice.Close();
    });

    [Fact]
    public Task Typed_words_keep_their_order_and_a_mask_its_placeholder_under_right_to_left() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        using var shell = new RenderedShell("keypaste-rtl-input-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        var boxes = shell.Window.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(boxes);

        foreach (var box in boxes)
        {
            var mono = box.Classes.Contains("mono");
            Assert.Equal(mono ? TextAlignment.Start : TextAlignment.Right, box.TextAlignment);
            Assert.Equal(FlowDirection.LeftToRight, box.GetVisualDescendants().OfType<TextPresenter>().Single().FlowDirection);
        }

        Assert.All(
            shell.Window.GetVisualDescendants().OfType<MaskedInput>().SelectMany(input => input.GetVisualDescendants().OfType<TextBlock>()),
            text => Assert.Equal(TextAlignment.Start, text.TextAlignment));
    });

    [Fact]
    public Task A_selected_rows_bar_is_at_the_side_its_title_starts_under_right_to_left() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        using var shell = new RenderedShell("keypaste-rtl-bar-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Selected = items.Rows[0];
        RenderedShell.Drain();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);

        using var bitmap = shell.Window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var (pixels, width, _) = AmberElements.Read(bitmap);
        var row = shell.Window.GetVisualDescendants().OfType<ListBoxItem>().Last(item => item.IsSelected && item.IsEffectivelyVisible);
        var middle = (int)(row.Bounds.Height / 2);

        bool IsBar(double x)
        {
            var physical = row.TranslatePoint(new Point(x, middle), shell.Window)!.Value;
            var scaling = shell.Window.RenderScaling;
            var pixel = 4 * (((int)(physical.Y * scaling) * width) + (int)(physical.X * scaling));

            return pixels[pixel + 2] - pixels[pixel] > 60;
        }

        Assert.True(IsBar(1), "the bar is at the row's start");
        Assert.False(IsBar(row.Bounds.Width - 2), "and not at its end");
    });

    [Theory]
    [InlineData("chevron-left", true)]
    [InlineData("chevron-right", true)]
    [InlineData("file-input", true)]
    [InlineData("folder-input", true)]
    [InlineData("log-out", true)]
    [InlineData("lock", false)]
    public Task Only_an_icon_that_points_a_way_mirrors(string name, bool mirrors) => HeadlessSession.On(() =>
    {
        var icon = new KpIcon { FlowDirection = FlowDirection.RightToLeft };
        _ = new Border { FlowDirection = FlowDirection.RightToLeft, Child = icon };
        icon.Icon = name;

        Assert.Equal(mirrors, !icon.HasMirrorTransform);
    });

    [Fact]
    public Task The_grips_arrow_keys_follow_the_screen_under_right_to_left() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        using var shell = new RenderedShell("keypaste-rtl-grip-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        var grip = shell.Window.GetVisualDescendants().OfType<ColumnGrip>().Single(grip => grip.Column == TableStates.Title);
        var columns = grip.FindAncestorOfType<EntryColumns>()!;
        var before = columns.DrawnWidths[0];
        grip.Focus();
        shell.Window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        RenderedShell.Drain();

        Assert.True(columns.DrawnWidths[0] > before, "Left moves the edge to the left, which widens a column drawn from the right");
    });

    [Fact]
    public Task A_grip_drag_widens_a_column_drawn_from_the_right_as_the_pointer_moves_left() => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.RightToLeft();
        using var shell = new RenderedShell("keypaste-rtl-drag-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        var grip = shell.Window.GetVisualDescendants().OfType<ColumnGrip>().Single(grip => grip.Column == TableStates.Title);
        var columns = grip.FindAncestorOfType<EntryColumns>()!;
        var before = columns.DrawnWidths[0];
        var at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), shell.Window)!.Value;

        shell.Frame();
        shell.Window.MouseMove(at);
        shell.Window.MouseDown(at, MouseButton.Left);
        shell.Window.MouseMove(at + new Point(-40, 0));
        shell.Window.MouseUp(at + new Point(-40, 0), MouseButton.Left);
        RenderedShell.Drain();

        Assert.True(columns.DrawnWidths[0] > before + 30, Invariant($"{before} became {columns.DrawnWidths[0]}"));
    });

    [Fact]
    public void A_bidi_override_in_a_name_is_shown_inert() =>
        Assert.DoesNotContain('‮', EntryNameSanitizer.Sanitize("abc‮def").Text);

    internal static Dictionary<string, string> Labels()
    {
        // The manager keeps this set and goes on reading from it, so it is not disposed here.
#pragma warning disable CA2000
        var set = Strings.Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
#pragma warning restore CA2000
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "keypaste.app.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("the repository is not above the tests");
    }

    [GeneratedRegex(@"\b(?:Placeholder|PlaceholderText|Subject|Text|Content|Header|ToolTip\.Tip|AutomationProperties\.Name|AutomationProperties\.HelpText|Title|Label)=""([^""{]*[A-Za-z][^""{]*)""")]
    private static partial Regex LiteralLabel();

    [GeneratedRegex(@"res:Strings\.(\w+)")]
    private static partial Regex StaticLabel();

    [GeneratedRegex(@"Format\([^;]*Strings\.")]
    private static partial Regex FormatOfALabel();

    [GeneratedRegex(@"\{Binding (\w+)[^}]*StringFormat=\{x:Static")]
    private static partial Regex BoundThroughALabel();
}
