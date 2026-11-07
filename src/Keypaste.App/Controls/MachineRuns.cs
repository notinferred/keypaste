using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// Sets what is machine-readable inside a sentence in Fragment Mono, as runs of the same text (DESIGN Rule 5).
/// </summary>
/// <remarks>
/// <see cref="InTextProperty"/> finds what has a machine's shape, for sentences that come from core and the system: a path, a flag, a scope, a tag or a
/// keypaste command. <see cref="WordsProperty"/> names what has no such shape: a key, an item's path or a command with arguments, each set where it first
/// appears as a whole word. <see cref="TextBlock.Text"/> keeps the whole sentence, which is what a screen reader and a test read.
/// </remarks>
internal sealed partial class MachineRuns : AvaloniaObject
{
    public static readonly AttachedProperty<bool> InTextProperty =
        AvaloniaProperty.RegisterAttached<MachineRuns, TextBlock, bool>("InText");

    /// <summary>The words, separated by <c>|</c>. A word that itself holds one is set in its parts, the bar staying sans.</summary>
    public static readonly AttachedProperty<string?> WordsProperty =
        AvaloniaProperty.RegisterAttached<MachineRuns, TextBlock, string?>("Words");

    static MachineRuns()
    {
        InTextProperty.Changed.AddClassHandler<TextBlock>((block, _) => Split(block));
        WordsProperty.Changed.AddClassHandler<TextBlock>((block, _) => Split(block));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((block, _) => Split(block));
    }

    private MachineRuns()
    {
    }

    public static bool GetInText(TextBlock block) => block.GetValue(InTextProperty);

    public static void SetInText(TextBlock block, bool value) => block.SetValue(InTextProperty, value);

    public static string? GetWords(TextBlock block) => block.GetValue(WordsProperty);

    public static void SetWords(TextBlock block, string? value) => block.SetValue(WordsProperty, value);

    /// <summary>
    /// A path from the root, the home or a drive, a flag, a scope, a tag or a keypaste command, without the punctuation that ends a sentence. A space stays in
    /// a path when what follows it goes on being one: it holds a separator or ends in a file's extension (Application Support/Claude, my vault.kdbx).
    /// </summary>
    [GeneratedRegex(@"(?<![\w/.~:\\-])(?:(?:~?/|[A-Za-z]:\\)(?:[^\s,;]|(?<![.:)]) (?=[^\s,;]*[/\\]|[^\s,;/\\]+\.[A-Za-z0-9]{1,8}\b))*[^\s,;.:)]|(?:read:|env:|tag:|--[a-z])[^\s,;]*[^\s,;.:)]|keypaste (?:mcp|run|log verify)\b)")]
    private static partial Regex Machine();

    private static void Split(TextBlock block)
    {
        var words = GetWords(block);

        if (!GetInText(block) && string.IsNullOrEmpty(words))
        {
            return;
        }

        var text = block.Text ?? string.Empty;
        var found = (GetInText(block) ? Machine().Matches(text).Select(match => (match.Index, match.Length)) : [])
            .Concat(Named(text, words))
            .OrderBy(span => span.Index)
            .ThenByDescending(span => span.Length)
            .ToList();

        if (found.Count == 0)
        {
            block.Inlines = null;
            return;
        }

        var mono = Application.Current?.FindResource("KpMono") as FontFamily;
        var runs = new InlineCollection();
        var at = 0;

        foreach (var (index, length) in found)
        {
            if (index < at)
            {
                continue;
            }

            if (index > at)
            {
                runs.Add(new Run(text[at..index]));
            }

            runs.Add(new Run(text.Substring(index, length)) { FontFamily = mono ?? FontFamily.Default });
            at = index + length;
        }

        if (at < text.Length)
        {
            runs.Add(new Run(text[at..]));
        }

        block.Inlines = runs;
    }

    private static IEnumerable<(int Index, int Length)> Named(string text, string? words) =>
        from word in (words ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
        let match = Regex.Match(text, $@"(?<![\w]){Regex.Escape(word)}(?![\w])", RegexOptions.CultureInvariant)
        where match.Success
        select (match.Index, match.Length);
}
