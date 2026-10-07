using System.Globalization;

namespace Keypaste.Core.Infrastructure;

/// <summary>
/// Writes the subset of TOML that <see cref="Toml"/> reads, and nothing it would refuse or read back as something else.
/// </summary>
/// <remarks>
/// A string is written between double quotes with no escape, because the reader has none: one holding a quote, a
/// backslash, a control character or a lone surrogate, or longer than the limits allow, cannot be written, and a caller
/// asks <see cref="CanWrite"/> and leaves it out rather than writing a file the reader refuses whole. Keys, section names
/// and comments come from code, so an unwritable one is a programming error and throws.
/// </remarks>
/// <param name="limits">The ceilings the file will be read under.</param>
public sealed class TomlWriter(TomlLimits limits)
{
    private readonly TomlLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    private readonly StringBuilder _text = new();

    /// <summary>Whether <paramref name="value"/> can be written as <paramref name="key"/>'s string and read back as itself.</summary>
    /// <param name="key">The bare key it would be written under.</param>
    /// <param name="value">The string.</param>
    /// <param name="comment">The comment that would follow it on its line, or null.</param>
    /// <returns><see langword="true"/> when the line fits the limits and the string holds only characters the reader keeps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is null.</exception>
    public bool CanWrite(string key, string value, string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        return value.Length <= _limits.StringLength
            && IsWritable(value)
            && Line(key, value, comment).Length <= _limits.LineLength;
    }

    /// <summary>Writes a <c># comment</c> line.</summary>
    /// <param name="text">The comment, without its <c>#</c>.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The comment holds a control character or is longer than a line may be.</exception>
    public TomlWriter Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Append("# " + Checked(text, nameof(text)));
    }

    /// <summary>Writes a blank line.</summary>
    /// <returns>This writer.</returns>
    public TomlWriter BlankLine() => Append(string.Empty);

    /// <summary>Writes a <c>[[name]]</c> header.</summary>
    /// <param name="name">The bare section name.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The name is not a bare key.</exception>
    public TomlWriter Table(string name) => Append($"[[{Bare(name, nameof(name))}]]");

    /// <summary>Writes <c>key = "value"</c>.</summary>
    /// <param name="key">The bare key.</param>
    /// <param name="value">The string, which <see cref="CanWrite"/> accepts.</param>
    /// <param name="comment">A comment to follow it on its line, or null.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The key is not a bare key, or the value or the line could not be read back.</exception>
    public TomlWriter Text(string key, string value, string? comment = null)
    {
        Bare(key, nameof(key));

        if (!CanWrite(key, value, comment))
        {
            throw new ArgumentException("the value cannot be written so that it reads back as itself", nameof(value));
        }

        return Append(Line(key, value, comment));
    }

    /// <summary>Writes <c>key = number</c>.</summary>
    /// <param name="key">The bare key.</param>
    /// <param name="value">A non-negative whole number.</param>
    /// <param name="comment">A comment to follow it on its line, or null.</param>
    /// <returns>This writer.</returns>
    /// <exception cref="ArgumentException">The key is not a bare key, or the line is longer than a line may be.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public TomlWriter Number(string key, int value, string? comment = null)
    {
        Bare(key, nameof(key));
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return Append(WithComment(string.Create(CultureInfo.InvariantCulture, $"{key} = {value}"), comment));
    }

    /// <summary>The file's text, one LF after every line.</summary>
    /// <returns>The text written so far.</returns>
    public override string ToString() => _text.ToString();

    private static string Line(string key, string value, string? comment) =>
        WithComment($"{key} = \"{value}\"", comment);

    private static string WithComment(string line, string? comment) =>
        comment is null ? line : $"{line}  # {Checked(comment, nameof(comment))}";

    private static bool IsWritable(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c is '"' or '\\' || char.IsControl(c))
            {
                return false;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string Checked(string text, string parameter) =>
        text.Any(char.IsControl) ? throw new ArgumentException("a comment holds no control character", parameter) : text;

    private static string Bare(string name, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, parameter);

        return name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? name
            : throw new ArgumentException($"'{name}' is not a bare key", parameter);
    }

    private TomlWriter Append(string line)
    {
        if (line.Length > _limits.LineLength)
        {
            throw new ArgumentException("the line is longer than the reader accepts", nameof(line));
        }

        _text.Append(line).Append('\n');
        return this;
    }
}
