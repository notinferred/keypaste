using System.Globalization;
using System.Resources;
using Avalonia;
using Avalonia.Styling;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// A language no translation exists for yet: right to left, as Arabic lays out with the English words, or long, every label half as long again, which is the honest test of "nothing cut off".
/// </summary>
internal sealed class PseudoLocale : IDisposable
{
    private readonly ResourceManager _original = Strings.Manager;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly IStyle? _style;

    private PseudoLocale(ResourceManager? manager, CultureInfo? culture, IStyle? style)
    {
        if (manager is not null)
        {
            Strings.Manager = manager;
        }

        if (culture is not null)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        _style = style;

        if (style is not null)
        {
            Application.Current!.Styles.Insert(0, style);
        }
    }

    internal static PseudoLocale RightToLeft() => new(null, CultureInfo.GetCultureInfo("ar-SA"), ReadingDirection.RightToLeftWindows());

    internal static PseudoLocale Long() => new(new LongManager(), null, null);

    /// <summary>A culture people really read in, with its dates and the English words as they stand.</summary>
    internal static PseudoLocale Real(string culture) => new(null, CultureInfo.GetCultureInfo(culture), null);

    internal static PseudoLocale Named(string locale) => locale switch
    {
        "rtl" => RightToLeft(),
        "long" => Long(),
        _ => Real(locale),
    };

    public void Dispose()
    {
        if (_style is not null)
        {
            Application.Current!.Styles.Remove(_style);
        }

        Strings.Manager = _original;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    /// <summary>Each word half as long again, in the accented letters a real translation brings, with the format's holes kept.</summary>
    /// <remarks>Only for the thread drawing: every other test runs alongside and reads the same static manager.</remarks>
    private sealed class LongManager() : ResourceManager("Keypaste.App.Resources.Strings", typeof(Strings).Assembly)
    {
        private readonly int _drawingThread = Environment.CurrentManagedThreadId;

        public override string? GetString(string name, CultureInfo? culture)
        {
            var text = base.GetString(name, culture);

            return text is null || Environment.CurrentManagedThreadId != _drawingThread ? text : string.Join(' ', text.Split(' ').Select(Lengthen));
        }

        private static string Lengthen(string word)
        {
            if (word.Contains('{', StringComparison.Ordinal) || !word.Any(char.IsLetter))
            {
                return word;
            }

            var kept = word.Replace('a', 'á').Replace('e', 'é').Replace('o', 'ö').Replace('u', 'ü').Replace('i', 'í');
            return kept + new string('ñ', Math.Max(1, word.Length / 2));
        }
    }
}
