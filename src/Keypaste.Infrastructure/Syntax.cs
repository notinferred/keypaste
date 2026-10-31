using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;

namespace Keypaste.Infrastructure;

/// <summary>A keypaste command: the CLI's verbs, their groups and the bridge.</summary>
/// <param name="name">The word that names it.</param>
/// <param name="description">Its summary line, then any overview after a blank line.</param>
/// <param name="tail">
/// What its usage ends with past the flags, as <c>run</c>'s <c>-- &lt;command&gt; [args...]</c>: everything after the
/// line's first <c>--</c>, which its last argument collects and <see cref="Syntax.Passed"/> gives back verbatim.
/// </param>
public class Verb(string name, string description, string? tail = null) : Command(name, description), IEnumerable<Symbol>
{
    /// <summary>What its usage ends with past the flags.</summary>
    public string? Tail { get; } = tail;

    /// <summary>Adds options several commands share, as one entry of a command's initializer.</summary>
    public void Add(IEnumerable<Option> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var option in options)
        {
            Options.Add(option);
        }
    }

    /// <summary>Its options, arguments and subcommands.</summary>
    public IEnumerator<Symbol> GetEnumerator() => Children.GetEnumerator();
}

/// <summary>keypaste's command-line syntax over System.CommandLine, one for every verb and the bridge (D-0678).</summary>
/// <remarks>
/// Response files and POSIX bundling are off, so an argument starting with <c>@</c> and one like <c>-abc</c> stay
/// arguments. <see cref="Problem"/> refuses what the library would otherwise accept: a first argument in brackets, which
/// it reads as a directive; an undeclared <c>--option</c> an operand could take; a repeated flag; and a flag given a value.
/// </remarks>
public static partial class Syntax
{
    private const int _width = 80;

    /// <summary>Where descriptions start at most; a longer label puts its description on the next line.</summary>
    private const int _column = 30;

    /// <summary>The parser's settings for every keypaste command line.</summary>
    public static ParserConfiguration Parsing { get; } = new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null };

    /// <summary><c>-h</c> and <c>--help</c>, added once at a root and recognized under every command below it.</summary>
    public static Option<bool> Help { get; } = new("--help", "-h")
    {
        Description = "help for this command",
        Arity = ArgumentArity.Zero,
        Recursive = true,
    };

    /// <summary>An option that takes no value.</summary>
    public static Option<bool> Flag(string name, string description, params string[] aliases) =>
        new(name, aliases) { Description = description, Arity = ArgumentArity.Zero };

    /// <summary>An option that takes one value, its value taken whole even when it starts with <c>-</c>.</summary>
    public static Option<string> Value(string name, string valueName, string description, params string[] aliases) =>
        new(name, aliases) { HelpName = valueName, Description = description };

    /// <summary>An option that may be given more than once, each time with one value, kept in order.</summary>
    public static Option<string[]> Values(string name, string valueName, string description) =>
        new(name) { HelpName = valueName, Description = description, Arity = ArgumentArity.OneOrMore };

    /// <summary>An operand that must be given.</summary>
    public static Argument<string> Operand(string name, string description) =>
        new(name) { Description = description };

    /// <summary>An operand that may be left out.</summary>
    public static Argument<string?> Optional(string name, string description) =>
        new(name) { Description = description, Arity = ArgumentArity.ZeroOrOne };

    /// <summary>Any number of operands, at least <paramref name="minimum"/>.</summary>
    public static Argument<string[]> Operands(string name, string description, int minimum = 0) =>
        new(name) { Description = description, Arity = new ArgumentArity(minimum, 100_000) };

    /// <summary>A word that may follow the operands, shown as itself rather than as a placeholder.</summary>
    public static Argument<string?> Keyword(string word, string description) =>
        new(word) { HelpName = word, Description = description, Arity = ArgumentArity.ZeroOrOne };

    /// <summary>The command a line names, as a person types it: <c>keypaste env ls</c>.</summary>
    public static string Path(ParseResult line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var names = new List<string>();
        for (SymbolResult? result = line.CommandResult; result is not null; result = result.Parent)
        {
            if (result is CommandResult command)
            {
                names.Insert(0, command.Command.Name);
            }
        }

        return string.Join(' ', names);
    }

    /// <summary>The arguments after the line's first <c>--</c>, verbatim, or null when it has none.</summary>
    public static string[]? Passed(ParseResult line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var separator = line.Tokens.ToList().FindIndex(token => token.Type == TokenType.DoubleDash);
        return separator < 0 ? null : [.. line.Tokens.Skip(separator + 1).Select(token => token.Value)];
    }

    /// <summary>
    /// What is wrong with <paramref name="line"/>, in the words the CLI prints after <c>keypaste &lt;verb&gt;:</c>, or null.
    /// Once its options parse, a line asking for help has nothing wrong with its operands (D-0467), and a group named
    /// without one of its commands is its caller's to answer, by showing the group's help or running it.
    /// </summary>
    /// <param name="line">The parsed line.</param>
    /// <param name="args">The arguments it was parsed from, which still show whether a flag was written with <c>=</c>.</param>
    public static string? Problem(ParseResult line, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(args);

        var command = line.CommandResult;
        Token? previous = null;

        foreach (var token in line.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
            {
                break;
            }

            if (token.Type == TokenType.Directive)
            {
                return $"unknown command '{token.Value}'";
            }

            var takesIt = previous is { Type: TokenType.Option } && Declared(command, previous.Value) is { Arity.MaximumNumberOfValues: > 0 };
            if (token is { Type: TokenType.Argument, Value: ['-', '-', _, ..] } && !takesIt)
            {
                return $"unknown option '{token.Value.Split('=', 2)[0]}'";
            }

            previous = token;
        }

        foreach (var arg in args.TakeWhile(arg => arg != "--").Where(arg => arg.StartsWith("--", StringComparison.Ordinal)))
        {
            if (arg.IndexOfAny(['=', ':']) is > 2 and var at && Declared(command, arg[..at]) is { Arity.MaximumNumberOfValues: 0 })
            {
                return $"option '{arg[..at]}' does not take a value";
            }
        }

        foreach (var option in command.Children.OfType<OptionResult>().Where(option => !option.Implicit))
        {
            var name = option.Option.Name;

            if (option.IdentifierTokenCount > 1 && option.Option.Arity.MaximumNumberOfValues <= 1)
            {
                return $"option '{name}' given more than once";
            }

            if (option.Tokens.Count < option.Option.Arity.MinimumNumberOfValues)
            {
                return $"option '{name}' needs a value";
            }
        }

        if (line.GetValue(Help))
        {
            return null;
        }

        if (line.UnmatchedTokens.Count > 0)
        {
            var unmatched = line.UnmatchedTokens[0];
            return command.Command.Subcommands.Count == 0 ? $"unexpected argument '{unmatched}'"
                : command.Parent is null ? $"unknown command '{unmatched}'"
                : $"unknown subcommand '{unmatched}'";
        }

        foreach (var argument in command.Children.OfType<ArgumentResult>())
        {
            if (argument.Tokens.Count < argument.Argument.Arity.MinimumNumberOfValues)
            {
                return $"expected {Placeholder(argument.Argument)}";
            }
        }

        return line.Errors.FirstOrDefault(error => error.SymbolResult is not CommandResult { Command.Subcommands.Count: > 0 })?.Message;
    }

    /// <summary>
    /// Writes <paramref name="line"/>'s command's help: its description, then USAGE, COMMANDS, ARGUMENTS and FLAGS, in
    /// the layout of keypaste's own help, within 80 columns.
    /// </summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="line">The parsed line, which names the command.</param>
    /// <param name="accent">Paints a command's name.</param>
    /// <param name="muted">Paints a heading.</param>
    public static void WriteHelp(TextWriter writer, ParseResult line, Func<string, string> accent, Func<string, string> muted)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(accent);
        ArgumentNullException.ThrowIfNull(muted);

        var command = line.CommandResult.Command;
        var subcommands = command.Subcommands.Where(sub => !sub.Hidden).Select(sub => (sub.Name, Summary(sub))).ToList();
        var arguments = command.Arguments.Where(argument => !argument.Hidden && argument.Description is not null)
            .Select(argument => (Placeholder(argument), argument.Description!)).ToList();
        var flags = command.Options.Where(option => !option.Hidden).Append(Help).Select(option => (Label(option), option.Description ?? "")).ToList();
        var column = Math.Min(subcommands.Concat(arguments).Concat(flags).Max(row => row.Item1.Length) + 4, _column);

        foreach (var (paragraph, index) in (command.Description ?? "").Split("\n\n").Select((paragraph, index) => (paragraph, index)))
        {
            if (index > 0)
            {
                writer.WriteLine();
            }

            Wrap(writer, "", paragraph, 0);
        }

        writer.WriteLine();
        writer.WriteLine(muted("USAGE"));
        writer.WriteLine($"  {Usage(Path(line), command)}");

        Section(writer, muted("COMMANDS"), subcommands, column, accent);
        Section(writer, muted("ARGUMENTS"), arguments, column, label => label);
        Section(writer, muted("FLAGS"), flags, column, label => label);
    }

    private static Option? Declared(CommandResult command, string name)
    {
        for (SymbolResult? result = command; result is not null; result = result.Parent)
        {
            if (result is CommandResult { Command: var declaring }
                && declaring.Options.FirstOrDefault(option => option.Name == name || option.Aliases.Contains(name)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Usage(string path, Command command)
    {
        var words = new List<string> { path };

        // A verb that also runs on its own lists its commands below rather than asking for one.
        if (command.Subcommands.Count > 0 && command.Arguments.Count == 0 && command.Options.Count == 0)
        {
            words.Add("<command>");
        }

        foreach (var argument in command.Arguments.Where(argument => !argument.Hidden))
        {
            var placeholder = Placeholder(argument);
            // A verb with a tail collects it into its last argument, which takes one value of its own.
            var repeats = argument.Arity.MaximumNumberOfValues > 1 && command is not Verb { Tail: not null } ? "..." : "";
            words.Add(argument.Arity.MinimumNumberOfValues == 0 ? $"[{placeholder}{repeats}]" : placeholder + repeats);
        }

        if (command.Options.Any(option => !option.Hidden))
        {
            words.Add("[flags]");
        }

        if (command is Verb { Tail: { } tail })
        {
            words.Add(tail);
        }

        return string.Join(' ', words);
    }

    private static string Placeholder(Argument argument) => argument.HelpName ?? $"<{argument.Name}>";

    private static string Summary(Command command) => (command.Description ?? "").Split('\n')[0];

    private static string Label(Option option)
    {
        var names = string.Join(", ", option.Aliases.Order(StringComparer.Ordinal).Append(option.Name));
        return option.Arity.MaximumNumberOfValues == 0 ? names : $"{names} <{option.HelpName ?? "value"}>";
    }

    private static void Section(
        TextWriter writer, string heading, List<(string Label, string Text)> rows, int column, Func<string, string> paint)
    {
        if (rows.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine(heading);

        foreach (var (label, text) in rows)
        {
            if (label.Length + 4 > column)
            {
                writer.WriteLine($"  {paint(label)}");
                Wrap(writer, new string(' ', column), text, column);
                continue;
            }

            Wrap(writer, $"  {paint(label)}{new string(' ', column - label.Length - 2)}", text, column);
        }
    }

    /// <summary>The words a line may break between: a <c>`code`</c> span is one, so a command it names stays on one line.</summary>
    [GeneratedRegex("(?:`[^`]*`|[^ `]|`)+")]
    private static partial Regex Words();

    private static void Wrap(TextWriter writer, string lead, string text, int indent)
    {
        var line = new StringBuilder(lead);
        var length = Math.Max(indent, 0);
        var first = true;

        foreach (var word in Words().Matches(text).Select(match => match.Value))
        {
            if (!first && length + 1 + word.Length > _width)
            {
                writer.WriteLine(line.ToString());
                line.Clear().Append(' ', indent);
                length = indent;
                first = true;
            }

            line.Append(first ? "" : " ").Append(word);
            length += (first ? 0 : 1) + word.Length;
            first = false;
        }

        writer.WriteLine(line.ToString());
    }
}
