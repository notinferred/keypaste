using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// V-N.10's rule: a verb that saves opens the vault through <c>VaultSession.OpenHeld</c>, which takes the
/// vault's claim before its password is read, so <c>VaultSession.Open</c> and <c>OpenThen</c> are called
/// only by verbs that never save.
/// </summary>
/// <remarks>
/// <para>
/// A text scan, since the compiler cannot say what a callback does. Each call's arguments are read with
/// strings and comments blanked out, and every method of the same file they call is followed, so a save
/// moved into a helper is still found. A save moved into another class is not; the verbs keep their
/// writes in their own files, and this test's controls would need a helper added if one stopped.
/// </para>
/// <para>
/// A save is a call that writes the vault file: <c>Save</c>, <c>ChangeAccess</c>, and the share service's
/// <c>CreateAsync</c> and <c>RevokeAsync</c>, which save the links' records.
/// </para>
/// </remarks>
public sealed class SavingVerbSourceRulesTests
{
    private static readonly string[] _saves = [".Save()", ".ChangeAccess(", ".CreateAsync(", ".RevokeAsync("];

    private static readonly Regex _opens = new(@"VaultSession\.(Open|OpenThen|OpenHeld)\s*(?:<[^>()]*>)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex _declared = new(
        @"(?m)^\s*(?:(?:private|internal|public|protected|static|async)\s+)+[\w<>\[\]?,\.() ]+?\s+(\w+)\s*(?:<[^>()]*>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex _called = new(@"(?<![\.\w])(\w+)\s*(?:<[^>()]*>)?\s*\(", RegexOptions.Compiled);

    [Fact]
    public void Only_OpenHeld_reaches_a_save()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"these verbs save through VaultSession.Open, which takes no claim: {string.Join("; ", offenders)}");
    }

    /// <summary>The tracer reaches the save of every verb that does take the claim, so a stale marker list or a missed helper fails here.</summary>
    [Fact]
    public void Every_OpenHeld_reaches_a_save()
    {
        var calls = Sources().SelectMany(source => Calls(source.Text).Where(call => call.Kind == "OpenHeld").Select(call => (source.Name, call.Traced))).ToList();

        Assert.True(calls.Count >= 15, $"only {calls.Count} OpenHeld calls were found");
        Assert.All(calls, call => Assert.True(_saves.Any(call.Traced.Contains), $"an OpenHeld call in {call.Name} reaches no save the scan knows"));
    }

    [Fact]
    public void A_save_added_through_Open_is_found()
    {
        var add = Read("Commands/AddCommand.cs");
        Assert.Contains("VaultSession.OpenHeld(", add, StringComparison.Ordinal);
        Assert.NotEmpty(Offenders("AddCommand.cs", add.Replace("VaultSession.OpenHeld(", "VaultSession.Open(", StringComparison.Ordinal)));

        var list = Read("Commands/ListCommand.cs");
        var open = list.IndexOf("VaultSession.Open(", StringComparison.Ordinal);
        var body = list.IndexOf('{', open) + 1;
        Assert.Empty(Offenders("ListCommand.cs", list));
        Assert.NotEmpty(Offenders("ListCommand.cs", list[..body] + " vault.Save(); " + list[body..]));
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        Calls(text)
            .Where(call => call.Kind is "Open" or "OpenThen" && _saves.Any(call.Traced.Contains))
            .Select(call => $"{name}:{call.Line}");

    /// <summary>Each vault-opening call, with its arguments and every same-file method they reach, strings and comments blanked.</summary>
    private static IEnumerable<(string Kind, int Line, string Traced)> Calls(string text)
    {
        var code = Blank(text);
        var methods = Methods(code);

        foreach (Match open in _opens.Matches(code))
        {
            var start = open.Index + open.Length - 1;
            var traced = new System.Text.StringBuilder(code[start..Close(code, start, '(', ')')]);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var grew = true; grew;)
            {
                grew = false;

                foreach (Match call in _called.Matches(traced.ToString()))
                {
                    var called = call.Groups[1].Value;

                    if (methods.TryGetValue(called, out var bodies) && seen.Add(called))
                    {
                        bodies.ForEach(bodyText => traced.Append(' ').Append(bodyText));
                        grew = true;
                    }
                }
            }

            yield return (open.Groups[1].Value, code[..open.Index].Count(c => c == '\n') + 1, traced.ToString());
        }
    }

    /// <summary>The body of every method the file declares, by name; overloads keep every body.</summary>
    private static Dictionary<string, List<string>> Methods(string code)
    {
        var methods = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (Match declared in _declared.Matches(code))
        {
            var parameters = declared.Index + declared.Length - 1;
            var after = Close(code, parameters, '(', ')');
            var rest = code[after..];
            var brace = rest.IndexOf('{', StringComparison.Ordinal);
            var arrow = rest.IndexOf("=>", StringComparison.Ordinal);
            var semicolon = rest.IndexOf(';', StringComparison.Ordinal);
            string body;

            if (brace >= 0 && (arrow < 0 || brace < arrow) && (semicolon < 0 || brace < semicolon))
            {
                body = code[(after + brace)..Close(code, after + brace, '{', '}')];
            }
            else if (arrow >= 0 && (semicolon < 0 || arrow < semicolon))
            {
                body = code[(after + arrow)..Statement(code, after + arrow)];
            }
            else
            {
                continue;
            }

            var name = declared.Groups[1].Value;

            if (!methods.TryGetValue(name, out var bodies))
            {
                methods[name] = bodies = [];
            }

            bodies.Add(body);
        }

        return methods;
    }

    /// <summary>The index just after the bracket that closes the one at <paramref name="open"/>.</summary>
    private static int Close(string code, int open, char opening, char closing)
    {
        var depth = 0;

        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == opening)
            {
                depth++;
            }
            else if (code[i] == closing && --depth == 0)
            {
                return i + 1;
            }
        }

        throw new Xunit.Sdk.XunitException($"no '{closing}' closes the '{opening}' at {open}");
    }

    /// <summary>The index just after the semicolon ending an expression body, outside any bracket.</summary>
    private static int Statement(string code, int start)
    {
        var depth = 0;

        for (var i = start; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '(' or '{' or '[': depth++; break;
                case ')' or '}' or ']': depth--; break;
                case ';' when depth == 0: return i + 1;
            }
        }

        return code.Length;
    }

    /// <summary>The text with every comment and the contents of every string and character literal replaced by spaces.</summary>
    private static string Blank(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;

        while (i < chars.Length)
        {
            i = Code(chars, i, stopAtBrace: false);
        }

        return new string(chars);
    }

    /// <summary>Scans code from <paramref name="i"/>, blanking what it passes, to the end or to an unmatched close brace.</summary>
    private static int Code(char[] chars, int i, bool stopAtBrace)
    {
        var depth = 0;

        while (i < chars.Length)
        {
            var c = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    chars[i++] = ' ';
                }
            }
            else if (c == '/' && next == '*')
            {
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/'))
                {
                    chars[i++] = ' ';
                }

                i = Math.Min(chars.Length, i + 2);
            }
            else if (c == '\'')
            {
                i = Literal(chars, i + 1, '\'', interpolated: false);
            }
            else if (c == '"' || (c == '$' && next == '"'))
            {
                i = Literal(chars, c == '$' ? i + 2 : i + 1, '"', interpolated: c == '$');
            }
            else if (c == '{')
            {
                depth++;
                i++;
            }
            else if (c == '}')
            {
                if (stopAtBrace && depth == 0)
                {
                    return i + 1;
                }

                depth--;
                i++;
            }
            else
            {
                i++;
            }
        }

        return i;
    }

    /// <summary>Blanks a literal's contents from <paramref name="i"/> to its closing quote, scanning an interpolation hole as code.</summary>
    private static int Literal(char[] chars, int i, char quote, bool interpolated)
    {
        while (i < chars.Length)
        {
            var c = chars[i];

            if (c == '\\')
            {
                chars[i] = ' ';

                if (i + 1 < chars.Length)
                {
                    chars[i + 1] = ' ';
                }

                i += 2;
            }
            else if (c == quote)
            {
                return i + 1;
            }
            else if (interpolated && c == '{' && i + 1 < chars.Length && chars[i + 1] == '{')
            {
                chars[i] = chars[i + 1] = ' ';
                i += 2;
            }
            else if (interpolated && c == '{')
            {
                i = Code(chars, i + 1, stopAtBrace: true);
            }
            else
            {
                chars[i++] = ' ';
            }
        }

        return i;
    }

    private static IEnumerable<(string Name, string Text)> Sources() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Keypaste.Cli"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Path.GetFileName(path) != "VaultSession.cs")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", relative));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}
