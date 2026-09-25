using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>A <c>kp://</c> reference: where a value lives in a vault, never the value.</summary>
/// <remarks>Records compare by what they name; <see cref="ToString"/> is the one canonical spelling.</remarks>
public abstract record KpReference
{
    /// <summary>The reference, percent-encoded as <see cref="KpReferences"/> writes it.</summary>
    /// <returns>The canonical text.</returns>
    public abstract override string ToString();
}

/// <summary>One variable of one profile of a project: <c>kp://&lt;project&gt;/&lt;profile&gt;/&lt;KEY&gt;</c>.</summary>
/// <param name="Project">The project.</param>
/// <param name="Profile">The profile.</param>
/// <param name="Key">The variable name.</param>
public sealed record EnvReference(string Project, string Profile, string Key) : KpReference
{
    /// <inheritdoc/>
    public override string ToString() => KpReferences.For(Project, Profile, Key);
}

/// <summary>One field of one vault entry: <c>kp:///&lt;group…&gt;/&lt;title&gt;[#field]</c>.</summary>
/// <param name="Entry">The entry, by group and title.</param>
/// <param name="Field">One of <see cref="CredentialFields.All"/>, <c>password</c> when the reference names none.</param>
public sealed record EntryReference(EntryName Entry, string Field) : KpReference
{
    /// <inheritdoc/>
    public override string ToString() => KpReferences.For(Entry, Field);
}

/// <summary>Reads and writes <c>kp://</c> references.</summary>
/// <remarks>
/// A non-empty authority names an env value and an empty one (<c>kp:///</c>) a vault entry. Every
/// byte outside <c>ALPHA / DIGIT / - . _ ~</c> is percent-encoded, so a written reference never holds
/// whitespace, a quote or a separator of its own, and a title holding <c>/</c> stays one segment.
/// </remarks>
public static class KpReferences
{
    /// <summary>The scheme and separator every reference starts with, lowercase.</summary>
    public const string Scheme = "kp://";

    /// <summary>The longest reference read.</summary>
    public const int MaximumLength = 2048;

    private const string _defaultField = "password";

    /// <summary>Parses a reference.</summary>
    /// <param name="text">The reference as written.</param>
    /// <param name="reference">The reference, when this returns true.</param>
    /// <param name="error">Why it is not one, otherwise empty.</param>
    /// <returns>Whether <paramref name="text"/> is a well-formed reference.</returns>
    public static bool TryParse(string text, [NotNullWhen(true)] out KpReference? reference, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        reference = null;

        if (text.Length > MaximumLength)
        {
            error = $"a reference has at most {MaximumLength} characters";
            return false;
        }

        if (!text.StartsWith(Scheme, StringComparison.Ordinal))
        {
            error = $"a reference starts with {Scheme}";
            return false;
        }

        var rest = text[Scheme.Length..];

        return rest.StartsWith('/')
            ? TryParseEntry(rest[1..], out reference, out error)
            : TryParseEnv(rest, out reference, out error);
    }

    /// <summary>The reference to one variable of one profile.</summary>
    /// <param name="project">The project.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="key">The variable name.</param>
    /// <returns>The canonical reference.</returns>
    public static string For(string project, string profile, string key)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(key);

        return Scheme + Encode(project) + "/" + Encode(profile) + "/" + Encode(key);
    }

    /// <summary>The reference to one field of one vault entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="field">The field; the password is written without a fragment.</param>
    /// <returns>The canonical reference.</returns>
    public static string For(EntryName entry, string field = _defaultField)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);

        var path = new StringBuilder(Scheme).Append('/');

        if (entry.GroupPath.Length > 0)
        {
            foreach (var segment in entry.GroupPath.Split('/'))
            {
                path.Append(Encode(segment)).Append('/');
            }
        }

        path.Append(Encode(entry.Title));

        return string.Equals(field, _defaultField, StringComparison.Ordinal) ? path.ToString() : path.Append('#').Append(field).ToString();
    }

    /// <summary>How a screen names an entry: its env reference where it is a variable, else its entry reference.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The reference, or null for an entry no reference resolves: untitled, or in a reserved group.</returns>
    public static string? ForEntry(EntryName entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Title.Length == 0 || ReservedGroups.IsReserved(entry.GroupPath))
        {
            return null;
        }

        var prefix = EnvConvention.RootGroup + "/";

        if (entry.GroupPath.StartsWith(prefix, StringComparison.Ordinal) && EnvConvention.IsValidKey(entry.Title, out _))
        {
            var segments = entry.GroupPath[prefix.Length..].Split('/');

            if (EnvConvention.IsValidProject(segments[0], out _))
            {
                if (segments.Length == 1)
                {
                    return For(segments[0], EnvProfileNames.Default, entry.Title);
                }

                if (segments.Length == 2
                    && !string.Equals(segments[1], EnvProfileNames.Default, StringComparison.Ordinal)
                    && EnvProfileNames.IsValid(segments[1], out _))
                {
                    return For(segments[0], segments[1], entry.Title);
                }
            }
        }

        return For(entry);
    }

    private static bool TryParseEnv(string rest, [NotNullWhen(true)] out KpReference? reference, out string error)
    {
        reference = null;

        if (rest.Contains('#', StringComparison.Ordinal))
        {
            error = "a reference to an env value takes no #field";
            return false;
        }

        var segments = rest.Split('/');

        if (segments.Length != 3)
        {
            error = $"a reference to an env value is {Scheme}<project>/<profile>/<KEY>";
            return false;
        }

        if (!TryDecode(segments[0], out var project, out error))
        {
            return false;
        }

        if (!EnvConvention.IsValidProject(project, out error)
            || !EnvProfileNames.IsValid(segments[1], out error)
            || !EnvConvention.IsValidKey(segments[2], out error))
        {
            return false;
        }

        reference = new EnvReference(project, segments[1], segments[2]);
        return true;
    }

    private static bool TryParseEntry(string rest, [NotNullWhen(true)] out KpReference? reference, out string error)
    {
        reference = null;

        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        var field = hash < 0 ? _defaultField : rest[(hash + 1)..];
        var segments = (hash < 0 ? rest : rest[..hash]).Split('/');

        if (!CredentialFields.IsReleasable(field))
        {
            error = $"'{field}' is not a field; use {string.Join(", ", CredentialFields.All)}";
            return false;
        }

        var decoded = new string[segments.Length];

        for (var i = 0; i < segments.Length; i++)
        {
            if (!TryDecode(segments[i], out decoded[i], out error))
            {
                return false;
            }

            if (i < segments.Length - 1 && decoded[i].Contains('/', StringComparison.Ordinal))
            {
                error = "a group name in a reference cannot hold '/'";
                return false;
            }
        }

        reference = new EntryReference(new EntryName(string.Join('/', decoded[..^1]), decoded[^1]), field);
        error = string.Empty;
        return true;
    }

    /// <summary>Decodes one segment: non-empty, only unreserved characters and well-formed UTF-8 escapes, no control character.</summary>
    private static bool TryDecode(string segment, out string decoded, out string error)
    {
        decoded = string.Empty;

        if (segment.Length == 0)
        {
            error = "a reference has an empty segment";
            return false;
        }

        var bytes = new List<byte>(segment.Length);

        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];

            if (c == '%')
            {
                if (i + 2 >= segment.Length
                    || !byte.TryParse(segment.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var escaped))
                {
                    error = "a reference has a '%' that is not followed by two hex digits";
                    return false;
                }

                bytes.Add(escaped);
                i += 2;
            }
            else if (IsUnreserved(c))
            {
                bytes.Add((byte)c);
            }
            else
            {
                error = $"a reference cannot hold '{c}' unencoded";
                return false;
            }
        }

        try
        {
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            error = "a reference has an escape that is not UTF-8";
            return false;
        }

        if (decoded.Any(char.IsControl))
        {
            decoded = string.Empty;
            error = "a reference cannot name a control character";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string Encode(string segment)
    {
        var encoded = new StringBuilder(segment.Length);

        foreach (var b in Encoding.UTF8.GetBytes(segment))
        {
            if (b < 0x80 && IsUnreserved((char)b))
            {
                encoded.Append((char)b);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
}
