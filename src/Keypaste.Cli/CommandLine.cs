namespace Keypaste.Cli;

/// <summary>Declares one option a verb accepts.</summary>
/// <param name="Name">The long name, without the leading dashes.</param>
/// <param name="TakesValue">Whether the option consumes a following value.</param>
internal readonly record struct OptionSpec(string Name, bool TakesValue);

/// <summary>
/// A hand-rolled parser for one verb's arguments.
/// </summary>
/// <remarks>
/// Hand-rolled because <c>System.CommandLine</c> is a NuGet package and <c>src/</c> carries no
/// dependencies (DECISIONS.md D-0004). It handles exactly what keypaste's five verbs need —
/// long options with or without values, <c>--</c>, and positional operands — and deliberately
/// does not grow into a framework. Short options other than <c>-h</c> are not supported, so
/// there is no bundling to get wrong.
/// </remarks>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options;
    private readonly List<string> _operands;

    private CommandLine(Dictionary<string, string?> options, List<string> operands)
    {
        _options = options;
        _operands = operands;
    }

    /// <summary>Positional arguments, in order.</summary>
    internal IReadOnlyList<string> Operands => _operands;

    /// <summary>Whether <c>--help</c> or <c>-h</c> was given.</summary>
    internal bool WantsHelp => _options.ContainsKey("help");

    /// <summary>Whether a valueless option was given.</summary>
    internal bool HasFlag(string name) => _options.ContainsKey(name);

    /// <summary>The value of an option, or <see langword="null"/> if it was not given.</summary>
    internal string? Value(string name) => _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Parses <paramref name="args"/> from <paramref name="start"/> against <paramref name="spec"/>.
    /// </summary>
    /// <returns><see langword="false"/> with <paramref name="error"/> set on any malformed input.</returns>
    internal static bool TryParse(
        string[] args,
        int start,
        IReadOnlyList<OptionSpec> spec,
        out CommandLine line,
        out string error)
    {
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
        List<string> operands = [];
        line = new CommandLine(options, operands);
        error = string.Empty;

        var operandsOnly = false;

        for (var i = start; i < args.Length; i++)
        {
            var token = args[i];

            if (operandsOnly)
            {
                operands.Add(token);
                continue;
            }

            if (string.Equals(token, "--", StringComparison.Ordinal))
            {
                operandsOnly = true;
                continue;
            }

            if (string.Equals(token, "-h", StringComparison.Ordinal)
                || string.Equals(token, "--help", StringComparison.Ordinal))
            {
                options["help"] = null;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                operands.Add(token);
                continue;
            }

            var name = token[2..];
            string? inlineValue = null;

            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                inlineValue = name[(equals + 1)..];
                name = name[..equals];
            }

            var declared = Find(spec, name);
            if (declared is null)
            {
                error = $"unknown option '--{name}'";
                return false;
            }

            if (options.ContainsKey(name))
            {
                error = $"option '--{name}' given more than once";
                return false;
            }

            if (!declared.Value.TakesValue)
            {
                if (inlineValue is not null)
                {
                    error = $"option '--{name}' does not take a value";
                    return false;
                }

                options[name] = null;
                continue;
            }

            if (inlineValue is not null)
            {
                options[name] = inlineValue;
                continue;
            }

            // A value is consumed positionally even if it looks like an option, so that
            // --notes '--not-a-flag' and passwords beginning with dashes are expressible.
            if (i + 1 >= args.Length)
            {
                error = $"option '--{name}' needs a value";
                return false;
            }

            options[name] = args[++i];
        }

        return true;
    }

    private static OptionSpec? Find(IReadOnlyList<OptionSpec> spec, string name)
    {
        foreach (var candidate in spec)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
