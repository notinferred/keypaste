using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Every spacing, radius, type size and icon size written in the app is one DESIGN § Type, § Shape and motion or § Icons names.
/// </summary>
/// <remarks>
/// The compiler takes any number, and a frame off by a pixel or two reads as fine, so the numbers drift unless something reads them. A binding or a resource
/// is checked where it is defined, in the AXAML or in C#'s literals.
/// </remarks>
public sealed partial class DesignScaleTests
{
    private static readonly HashSet<double> _spacing = [0, 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 56];
    private static readonly HashSet<double> _radii = [0, 4, 6, 7, 10, 14];
    private static readonly HashSet<double> _icons = [14, 16, 20];

    // Display, heading, list header, dialog title, body and mono, label and helper line, and table header. The half sizes are inside DESIGN's ranges.
    private static readonly HashSet<double> _typeSizes = [54, 24, 20, 16, 15, 14, 13.5, 13, 12.5, 12, 11];

    private static readonly HashSet<string> _spacingProperties = ["Margin", "Padding", "Spacing", "RowSpacing", "ColumnSpacing", "ItemSpacing", "LineSpacing"];

    /// <summary>Every negative margin there is, and the room it gives back. Anything else moves the element rather than joining this list.</summary>
    private static readonly Dictionary<string, string> _negativeMargins = new()
    {
        ["ShellView -2"] = "A fold's 20 by 20 target in the tree's 16 column keeps the row's height.",
        ["ShellView 2,-2,-8,-2"] = "A place's fold, a 20 by 20 target, keeps the row's height and sits in its end padding.",
        ["Surfaces 0,-2,-6,-2"] = "A chip's remove, a 20 by 20 target, keeps the chip's height.",
        ["SharingView 0,-4"] = "A row's action, 24 tall, keeps a text line's height.",
        ["EnvSetsView 0,-4"] = "A row's actions, 28 tall, keep a text line's height.",
        ["LogView 12,-4,-6,-4"] = "Copy, 24 tall, keeps the verdict's line height and sits in the card's padding.",
        ["EnvSetsView -6,-2"] = "A cell's hover face stands out from its text, so the text does not move.",
        ["EnvSetsView 0,0,-32,0"] = "Fluent's ComboBox keeps a 32 column for its glyph, which the project's title hides and draws beside the name.",
        ["EnvSetsView 0,0,0,-8"] = "The selected environment's bar lies on the head's lower edge, across the head's padding.",
        ["KdbxImportView -8,0,0,0"] = "A destination is text until hovered, and its field's face stands out from that text.",
        ["LogView 0,0,-12,0"] = "A result may run into the row's end padding before it trims.",
        ["ShareDialogView -4,-4,-12,-4"] = "A scroll viewer leaves a focused field its glow and takes the card's padding for its bar.",
        ["KdbxImportView -4,-4,-12,-4"] = "A scroll viewer leaves a focused field its glow and takes the card's padding for its bar.",
        ["UnlockView -56,0"] = "The quieter ways in stay on one line, wider than the form's fixed 360 column.",
    };

    /// <summary>Doubles that are not on any of DESIGN's lists because Fluent's own control defines what they mean.</summary>
    private static readonly Dictionary<string, string> _chromeSizes = new()
    {
        ["ScrollBarSize"] = "The width of Fluent's scroll bar track, which no list of DESIGN's sizes.",
    };

    /// <summary>The mono text at 14: DESIGN's masks draw at 14 or larger.</summary>
    private static readonly HashSet<string> _masks = ["{Binding PasswordMask}"];

    [Fact]
    public void Spacing_is_on_the_scale() =>
        Assert.Empty(Offenders(_spacingProperties, _spacing, element => element.Name.LocalName == "Thickness" && !element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value.Contains("Thickness", StringComparison.Ordinal))));

    [Fact]
    public void A_negative_margin_is_one_the_list_names() =>
        Assert.Empty(
            from file in Files()
            from element in XDocument.Load(file).Descendants()
            from value in Values(element, ["Margin"], _ => false)
            where value.Contains('-', StringComparison.Ordinal) && !value.StartsWith('{')
            let site = $"{Path.GetFileNameWithoutExtension(file)} {value}"
            where !_negativeMargins.ContainsKey(site)
            select site);

    [Fact]
    public void Radii_are_on_the_list() =>
        Assert.Empty(Offenders(["CornerRadius", "RadiusX", "RadiusY"], _radii, element => element.Name.LocalName == "CornerRadius"));

    [Fact]
    public void Type_sizes_are_on_the_table() =>
        Assert.Empty(Offenders(["FontSize"], _typeSizes, _ => false));

    [Fact]
    public void A_double_resource_is_on_its_list() =>
        Assert.Empty(
            from file in Files()
            from element in XDocument.Load(file).Descendants()
            where element.Name.LocalName == "Double"
            let key = element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value
            where !_chromeSizes.ContainsKey(key)
            let allowed = key.Contains("FontSize", StringComparison.Ordinal) ? _typeSizes : key.StartsWith("KpGap", StringComparison.Ordinal) || key.StartsWith("KpSpace", StringComparison.Ordinal) ? _spacing : []
            where !Number(element.Value).All(allowed.Contains)
            select $"{Path.GetFileName(file)}: {key} {element.Value}");

    [Fact]
    public void Icons_are_the_three_sizes() =>
        Assert.Empty(
            from file in Files()
            from element in XDocument.Load(file).Descendants()
            let size = element.Name.LocalName == "KpIcon"
                ? (string?)element.Attribute("Size")
                : element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Size" ? (string?)element.Attribute("Value") : null
            where size is not null && !Number(size).All(_icons.Contains)
            select $"{Path.GetFileName(file)}: {element}");

    [Fact]
    public void A_view_sets_a_size_through_a_class() =>
        Assert.Empty(
            from file in Files().Where(file => Path.GetFileName(Path.GetDirectoryName(file)) == "Views")
            from element in XDocument.Load(file).Descendants()
            from value in Values(element, ["FontSize"], _ => false)
            select $"{Path.GetFileName(file)}: <{element.Name.LocalName}> {value}");

    [Fact]
    public void Mono_is_12_to_13_outside_a_mask() =>
        Assert.Empty(
            from file in Files()
            from element in XDocument.Load(file).Descendants()
            where element.Name.LocalName != "MaskedInput" && IsMono(element)
            where SizeClasses(element).Any(size => size is > 13) && !_masks.Contains((string?)element.Attribute("Text") ?? string.Empty)
            select $"{Path.GetFileName(file)}: {element.Attribute("Classes")} {element.Attribute("Text")}");

    [Fact]
    public void Sans_at_12_is_a_label() =>
        Assert.Empty(
            from file in Files()
            let document = XDocument.Load(file)
            let monoOrLabel = ClassesSetting("FontFamily", "{DynamicResource KpMono}").Concat(ClassesSetting("FontWeight", "Medium")).ToHashSet()
            from element in document.Descendants()
            where element.Name.LocalName is "TextBlock" or "SelectableTextBlock"
            where SizeClasses(element).Contains(12)
            where !IsMono(element) && !Classes(element).Overlaps(monoOrLabel) && !Classes(element).Overlaps(["medium", "strong"]) && (string?)element.Attribute("FontWeight") is null
            where !element.Ancestors().Any(ancestor => Classes(ancestor).Overlaps(["prompt-call", "prompt-list", "terminal"]))
            select $"{Path.GetFileName(file)}: {element.Attribute("Classes")} {element.Attribute("Text")}");

    [Fact]
    public void Each_title_ranks_where_DESIGN_puts_it()
    {
        Assert.Equal(24, ClassSize("h1"));
        Assert.Equal(24, ClassSize("title"));
        Assert.Equal(20, ClassSize("list-title"));
        Assert.Equal(16, ClassSize("dialog-title"));
    }

    [Fact]
    public void A_helper_line_is_not_larger_than_its_label() =>
        Assert.True(ClassSize("caption") <= ClassSize("label"));

    [Fact]
    public void An_icon_is_20_only_on_the_rail_in_a_tile_or_as_an_empty_marks()
    {
        Assert.Empty(
            from file in Directory.EnumerateFiles(Path.Combine(App(), "Views"), "*.axaml")
            from icon in XDocument.Load(file).Descendants().Where(element => element.Name.LocalName == "KpIcon" && (string?)element.Attribute("Size") == "20")
            where !icon.Ancestors().Any(ancestor => (string?)ancestor.Attribute("Name") == "Rail" || ((string?)ancestor.Attribute("Classes"))?.Contains("tile", StringComparison.Ordinal) == true)
                && !(Path.GetFileName(file) is "TrashView.axaml" or "LogView.axaml")
            select $"{Path.GetFileName(file)}: {icon}");
    }

    [Fact]
    public void A_sidebar_row_icon_is_16()
    {
        var shell = XDocument.Load(Path.Combine(App(), "Views", "ShellView.axaml"));

        Assert.All(
            shell.Descendants().Where(element => element.Name.LocalName == "KpIcon" && ((string?)element.Attribute("Icon"))?.Contains("{Binding Icon}", StringComparison.Ordinal) == true && !element.Ancestors().Any(ancestor => (string?)ancestor.Attribute("Name") == "Rail")),
            icon => Assert.Equal("16", (string?)icon.Attribute("Size")));
    }

    [Fact]
    public void A_literal_in_csharp_is_on_its_list()
    {
        Assert.Contains(DepthToIndent.Step, _spacing);

        Assert.Empty(
            from file in Sources()
            from match in LiteralSpacing().Matches(File.ReadAllText(file))
            where !match.Groups["values"].Value.Split(',').Select(part => part.Trim()).Where(IsLiteral).Select(part => double.Parse(part.TrimEnd('d'), CultureInfo.InvariantCulture)).All(_spacing.Contains)
            select $"{Path.GetFileName(file)}: {match.Value}");

        Assert.Empty(
            from file in Sources()
            from match in LiteralRadius().Matches(File.ReadAllText(file))
            where !_radii.Contains(double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture))
            select $"{Path.GetFileName(file)}: {match.Value}");

        Assert.Empty(
            from file in Sources()
            from match in LiteralIcon().Matches(File.ReadAllText(file))
            where !_icons.Contains(double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture))
            select $"{Path.GetFileName(file)}: {match.Value}");
    }

    [GeneratedRegex(@"new Thickness\((?<values>[^()]*)\)|\bSpacing = (?<values>\d+)")]
    private static partial Regex LiteralSpacing();

    [GeneratedRegex(@"new CornerRadius\((?<value>\d+(?:\.\d+)?)\)")]
    private static partial Regex LiteralRadius();

    [GeneratedRegex(@"new\(\) \{ Size = (?<value>\d+)|new KpIcon \{[^}]*\bSize = (?<value>\d+)")]
    private static partial Regex LiteralIcon();

    private static bool IsLiteral(string part) => part.Length > 0 && char.IsAsciiDigit(part[0]) && part.TrimEnd('d').All(character => char.IsAsciiDigit(character) || character == '.');

    private static double ClassSize(string name) =>
        (from style in XDocument.Load(Path.Combine(App(), "Theme", "Typography.axaml")).Descendants().Where(element => element.Name.LocalName == "Style")
         where (string?)style.Attribute("Selector") == $":is(TextBlock).{name}"
         from setter in style.Elements()
         where (string?)setter.Attribute("Property") == "FontSize"
         select double.Parse((string)setter.Attribute("Value")!, CultureInfo.InvariantCulture)).Single();

    private static IEnumerable<string> Offenders(HashSet<string> properties, HashSet<double> allowed, Func<XElement, bool> isResource) =>
        from file in Files()
        from element in XDocument.Load(file).Descendants()
        from value in Values(element, properties, isResource)
        where !Number(value).All(allowed.Contains)
        select $"{Path.GetFileName(file)}: <{element.Name.LocalName}> {value}";

    private static IEnumerable<string> Values(XElement element, HashSet<string> properties, Func<XElement, bool> isResource)
    {
        if (isResource(element))
        {
            yield return element.Value;
        }

        if (element.Name.LocalName == "Setter" && properties.Contains(Unowned((string?)element.Attribute("Property") ?? string.Empty)))
        {
            yield return (string?)element.Attribute("Value") ?? string.Empty;
        }

        foreach (var attribute in element.Attributes().Where(attribute => properties.Contains(Unowned(attribute.Name.LocalName))))
        {
            yield return attribute.Value;
        }
    }

    /// <summary>The property's name without its owner: <c>TextElement.FontSize</c> is a size as <c>FontSize</c> is.</summary>
    private static string Unowned(string property) => property[(property.LastIndexOf('.') + 1)..];

    /// <summary>The magnitudes a literal holds; a markup extension holds none.</summary>
    private static IEnumerable<double> Number(string value) =>
        value.StartsWith('{')
            ? []
            : value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(part => Math.Abs(double.Parse(part, CultureInfo.InvariantCulture)));

    private static HashSet<string> Classes(XElement element) =>
        [.. ((string?)element.Attribute("Classes") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private static IEnumerable<double> SizeClasses(XElement element) =>
        Classes(element).Where(name => name.StartsWith("size-", StringComparison.Ordinal))
            .Select(name => double.Parse(name["size-".Length..].Replace('-', '.'), CultureInfo.InvariantCulture));

    private static bool IsMono(XElement element) =>
        Classes(element).Contains("mono") || (string?)element.Attribute("FontFamily") == "{DynamicResource KpMono}";

    /// <summary>The classes a style names as it sets a property to a value.</summary>
    private static HashSet<string> ClassesSetting(string property, string value) =>
        [.. from file in Files()
            from style in XDocument.Load(file).Descendants().Where(element => element.Name.LocalName == "Style")
            where style.Elements().Any(setter => (string?)setter.Attribute("Property") == property && (string?)setter.Attribute("Value") == value)
            from selector in ((string?)style.Attribute("Selector") ?? string.Empty).Split(',')
            let name = selector.Trim().Split(' ')[^1]
            where name.Contains('.', StringComparison.Ordinal)
            select name[(name.LastIndexOf('.') + 1)..]];

    private static string App()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.app.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("keypaste.app.slnx is not above the tests.");
        }

        return Path.Combine(directory, "src", "Keypaste.App");
    }

    private static IEnumerable<string> Files() =>
        Directory.EnumerateFiles(Path.Combine(App(), "Views"), "*.axaml").Concat(Directory.EnumerateFiles(Path.Combine(App(), "Theme"), "*.axaml"));

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(App(), "Views"), "*.cs", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(Path.Combine(App(), "Controls"), "*.cs", SearchOption.AllDirectories));
}
