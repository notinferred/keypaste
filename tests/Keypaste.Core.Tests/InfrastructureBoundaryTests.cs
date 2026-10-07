using System.Text;
using System.Text.RegularExpressions;
using Keypaste.Core.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// <c>Keypaste.Core.Infrastructure</c> holds the pieces that know nothing about vaults, and no file in it uses another
/// Keypaste namespace.
/// </summary>
/// <remarks>
/// Read from the source: a file there reaches the types of <c>Keypaste.Core</c> itself without a using, and a constant
/// it reads leaves no reference in the compiled code. So each file's usings, its qualified names and every name its
/// code and its <c>cref</c>s mention are checked against the types <c>Keypaste.Core</c> declares.
/// </remarks>
public sealed partial class InfrastructureBoundaryTests
{
    private const string _namespace = "Keypaste.Core.Infrastructure";

    private static readonly HashSet<string> _parentTypes = typeof(Toml).Assembly.GetTypes()
        .Where(type => !type.IsNested && type.Namespace is "Keypaste" or "Keypaste.Core" && !type.Name.Contains('<', StringComparison.Ordinal))
        .Select(type => type.Name.Split('`')[0])
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void No_file_uses_another_keypaste_namespace()
    {
        var directory = Path.Combine(VendoredWordList.RepoRoot(), "src", "Keypaste.Core", "Infrastructure");
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);

        Assert.True(files.Length >= 5, $"{directory} holds {files.Length} source files; it was not read");

        var offences = files.SelectMany(file => Offences(Path.GetFileName(file), File.ReadAllText(file))).ToList();

        Assert.True(offences.Count == 0, string.Join('\n', offences));
    }

    /// <summary>The check is live: each way a file can reach outside is caught, and literal text is not mistaken for code.</summary>
    [Fact]
    public void Each_way_out_is_caught_and_text_is_not()
    {
        Assert.Contains(nameof(Vault), _parentTypes);
        Assert.DoesNotContain(nameof(Toml), _parentTypes);

        const string Source = """
            using System.Text;
            using Keypaste.Core.Policy;

            namespace Keypaste.Core.Infrastructure;

            /// <summary>Reads like <see cref="DotEnv"/>.</summary>
            internal static class Probe
            {
                private const string _quoted = "a Vault in a string";
                private static readonly char _mark = DotEnv.ByteOrderMark;
                private static string Shown(int count) => $"{count} in {Keypaste.Core.Audit.KeypasteHome.EnvironmentVariable}";
            }
            """;

        Assert.Equal(
            [
                "Probe.cs: using Keypaste.Core.Policy",
                "Probe.cs: names Keypaste.Core.Audit.KeypasteHome.EnvironmentVariable",
                "Probe.cs: names DotEnv",
                "Probe.cs: names DotEnv",
            ],
            Offences("Probe.cs", Source));
    }

    private static List<string> Offences(string file, string source)
    {
        List<string> offences = [];

        foreach (Match directive in UsingDirective().Matches(source))
        {
            var named = directive.Groups[1].Value;

            if (named is not "System" && !named.StartsWith("System.", StringComparison.Ordinal))
            {
                offences.Add($"{file}: using {named}");
            }
        }

        if (!source.Contains($"namespace {_namespace};", StringComparison.Ordinal))
        {
            offences.Add($"{file}: not in {_namespace}");
        }

        var code = Code(UsingDirective().Replace(source, string.Empty).Replace($"namespace {_namespace};", string.Empty, StringComparison.Ordinal));

        offences.AddRange(QualifiedName().Matches(code).Select(name => $"{file}: names {name.Value}"));
        offences.AddRange(Identifier().Matches(code).Where(name => _parentTypes.Contains(name.Value)).Select(name => $"{file}: names {name.Value}"));

        return offences;
    }

    /// <summary>The source without its comments and literal text, keeping each <c>cref</c> and each interpolation's expressions.</summary>
    private static string Code(string source)
    {
        var code = new StringBuilder();
        var at = 0;

        while (at < source.Length)
        {
            var rest = source.AsSpan(at);

            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                var end = source.IndexOf('\n', at) is var newline and >= 0 ? newline : source.Length;

                foreach (Match cref in Cref().Matches(source[at..end]))
                {
                    code.Append(' ').Append(cref.Groups[1].Value).Append(' ');
                }

                at = end;
            }
            else if (rest.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = source.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? source.Length : end + 2;
            }
            else if (rest.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                var end = source.IndexOf("\"\"\"", at + 3, StringComparison.Ordinal);
                at = end < 0 ? source.Length : end + 3;
            }
            else if (rest[0] is '"' or '\'' || LiteralPrefix().IsMatch(rest))
            {
                at = SkipLiteral(source, at, code);
            }
            else
            {
                code.Append(source[at]);
                at++;
            }
        }

        return code.ToString();
    }

    private static int SkipLiteral(string source, int at, StringBuilder code)
    {
        var interpolated = false;
        var verbatim = false;

        while (source[at] is '$' or '@')
        {
            interpolated |= source[at] == '$';
            verbatim |= source[at] == '@';
            at++;
        }

        var close = source[at++];

        while (at < source.Length)
        {
            var c = source[at];

            if (c == '\\' && !verbatim)
            {
                at += 2;
            }
            else if (c == close && verbatim && at + 1 < source.Length && source[at + 1] == close)
            {
                at += 2;
            }
            else if (c == close)
            {
                return at + 1;
            }
            else if (interpolated && c == '{' && at + 1 < source.Length && source[at + 1] == '{')
            {
                at += 2;
            }
            else if (interpolated && c == '{')
            {
                at = KeepHole(source, at + 1, code);
            }
            else
            {
                at++;
            }
        }

        return at;
    }

    private static int KeepHole(string source, int at, StringBuilder code)
    {
        for (var depth = 1; at < source.Length; at++)
        {
            depth += source[at] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (depth == 0)
            {
                code.Append(' ');
                return at + 1;
            }

            code.Append(source[at]);
        }

        return at;
    }

    [GeneratedRegex(@"^(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?([\w.]+)\s*;\s*$", RegexOptions.Multiline)]
    private static partial Regex UsingDirective();

    [GeneratedRegex(@"\bKeypaste(?:\.\w+)+")]
    private static partial Regex QualifiedName();

    [GeneratedRegex(@"\b[A-Za-z_]\w*\b")]
    private static partial Regex Identifier();

    [GeneratedRegex("cref=\"([^\"]+)\"")]
    private static partial Regex Cref();

    [GeneratedRegex("^(?:\\$@|@\\$|\\$|@)\"")]
    private static partial Regex LiteralPrefix();
}
