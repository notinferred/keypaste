using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core.Policy;

namespace Keypaste.Core.Clients;

/// <summary>How an MCP client's requests are asked about.</summary>
public enum ClientPolicy
{
    /// <summary>A person may give a timed grant, and in <c>keypaste agent</c> a standing rule may apply: keypaste's default.</summary>
    SessionGrants = 0,

    /// <summary>Every request is asked about: no timed grant is offered or used and no standing rule applies.</summary>
    AskEveryTime = 1,

    /// <summary><c>request_credential</c> is refused unasked; <c>run</c> is allowed.</summary>
    InjectOnly = 2,
}

/// <summary>One row of <c>clients.toml</c>.</summary>
/// <param name="Label">The bridge's <c>--client-label</c>, or <see cref="ClientPolicies.AnyClient"/>.</param>
/// <param name="Policy">How it is asked.</param>
public sealed record ClientPolicyRow(string Label, ClientPolicy Policy);

/// <summary>
/// The per-client policies a person set in <c>~/.keypaste/clients.toml</c>, keyed by the
/// <c>--client-label</c> each bridge was started with (D-0360).
/// </summary>
/// <remarks>
/// <para>
/// Every policy only narrows: <see cref="ClientPolicy.SessionGrants"/> is exactly what keypaste did
/// before this file existed and the other two remove paths. So <see cref="AnyClient"/> covers a bridge
/// started with no label too, unlike <c>policy.toml</c>'s <c>*</c>, which releases and so matches no
/// unlabeled bridge.
/// </para>
/// <para>
/// A label is whatever the client's configuration says, and an agent that can edit that
/// configuration can change it (THREATS.md T-36): the strict policy belongs on <see cref="AnyClient"/>.
/// </para>
/// </remarks>
public sealed class ClientPolicies
{
    /// <summary>The row covering every client without one of its own.</summary>
    public const string AnyClient = "*";

    /// <summary>The most rows a file may hold.</summary>
    public const int MaximumRows = 256;

    /// <summary>The longest label, as <c>keypaste-mcp --client-label</c> accepts it.</summary>
    public const int MaximumLabelLength = 64;

    private const string _section = "client";
    private const string _labelKey = "label";
    private const string _policyKey = "policy";

    private static readonly TomlLimits _limits = TomlLimits.Policy with
    {
        Tables = MaximumRows,
        Lines = MaximumRows * 4 + 16,
        Bytes = 64 * 1024,
    };

    private static readonly string[] _header =
    [
        "# keypaste: how each MCP client is asked, by the --client-label its bridge was started with.",
        "# \"*\" is every other client. policy: \"session\" (Session grants up to 1h), \"ask\" (Ask every time),",
        "# \"inject-only\" (Inject only: keypaste never hands it a value; only commands you approve can read the values).",
    ];

    private ClientPolicies(IReadOnlyList<ClientPolicyRow> rows)
    {
        Rows = rows;
    }

    /// <summary>No rows: every client is <see cref="ClientPolicy.SessionGrants"/>.</summary>
    public static ClientPolicies Empty { get; } = new([]);

    /// <summary>The rows, in the order written.</summary>
    public IReadOnlyList<ClientPolicyRow> Rows { get; }

    /// <summary>The policy a bridge with this label is held to.</summary>
    /// <param name="label">The raw label, or null for a bridge started without one.</param>
    /// <returns>Its own row, else the <see cref="AnyClient"/> row, else <see cref="ClientPolicy.SessionGrants"/>.</returns>
    public ClientPolicy For(string? label)
    {
        if (label is { Length: > 0 } && !string.Equals(label, AnyClient, StringComparison.Ordinal)
            && Rows.FirstOrDefault(row => string.Equals(row.Label, label, StringComparison.Ordinal)) is { } own)
        {
            return own.Policy;
        }

        return Rows.FirstOrDefault(row => string.Equals(row.Label, AnyClient, StringComparison.Ordinal))?.Policy
            ?? ClientPolicy.SessionGrants;
    }

    /// <summary>These policies with one row replaced, or appended when the label has none.</summary>
    /// <param name="label">The label, or <see cref="AnyClient"/>.</param>
    /// <param name="policy">Its policy.</param>
    /// <returns>The new policies.</returns>
    /// <exception cref="ArgumentException">The label is not one a row may hold, or the file would hold too many rows.</exception>
    public ClientPolicies With(string label, ClientPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (!IsValidLabel(label, out var error))
        {
            throw new ArgumentException(error, nameof(label));
        }

        var rows = Rows.ToList();
        var index = rows.FindIndex(row => string.Equals(row.Label, label, StringComparison.Ordinal));

        if (index >= 0)
        {
            rows[index] = new ClientPolicyRow(label, policy);
        }
        else if (rows.Count == MaximumRows)
        {
            throw new ArgumentException($"a clients file holds at most {MaximumRows} rows", nameof(label));
        }
        else
        {
            rows.Add(new ClientPolicyRow(label, policy));
        }

        return new ClientPolicies(rows);
    }

    /// <summary>The file's text.</summary>
    /// <returns>The header comment and one <c>[[client]]</c> table per row.</returns>
    public string Format()
    {
        var text = new StringBuilder();

        foreach (var line in _header)
        {
            text.Append(line).Append('\n');
        }

        foreach (var row in Rows)
        {
            text.Append("[[").Append(_section).Append("]]\n")
                .Append(_labelKey).Append(" = \"").Append(row.Label).Append("\"\n")
                .Append(_policyKey).Append(" = \"").Append(Wire(row.Policy)).Append("\"\n");
        }

        return text.ToString();
    }

    /// <summary>Reads a file's bytes.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="policies">What it holds, on success.</param>
    /// <param name="problem">What is wrong with it, otherwise empty.</param>
    /// <returns>Whether every table is a well-formed row; anything else is malformed.</returns>
    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out ClientPolicies? policies, out string problem)
    {
        policies = null;

        if (!Toml.TryDecode(bytes, _limits, out var text, out problem)
            || !Toml.TryParse(text, _limits, out var document, out problem))
        {
            return false;
        }

        List<ClientPolicyRow> rows = [];

        foreach (var table in document.Tables)
        {
            if (!string.Equals(table.Name, _section, StringComparison.Ordinal))
            {
                problem = At(table.Line, $"[[{table.Name}]] is not a section this file has; use [[{_section}]]");
                return false;
            }

            if (table.Pairs.FirstOrDefault(pair => pair.Key is not (_labelKey or _policyKey)) is { } unknown)
            {
                problem = At(unknown.Line, $"'{unknown.Key}' is not a setting; a client has only {_labelKey} and {_policyKey}");
                return false;
            }

            if (!table.TryGet(_labelKey, out var label) || label.Value.Kind != TomlValueKind.Text
                || !table.TryGet(_policyKey, out var policy) || policy.Value.Kind != TomlValueKind.Text)
            {
                problem = At(table.Line, $"a client needs both {_labelKey} and {_policyKey} as strings");
                return false;
            }

            if (!IsValidLabel(label.Value.Text, out var labelError))
            {
                problem = At(label.Line, labelError);
                return false;
            }

            if (!TryParseWire(policy.Value.Text, out var parsed))
            {
                problem = At(policy.Line, $"'{policy.Value.Text}' is not a policy; use session, ask or inject-only");
                return false;
            }

            if (rows.Any(row => string.Equals(row.Label, label.Value.Text, StringComparison.Ordinal)))
            {
                problem = At(label.Line, $"'{label.Value.Text}' has two rows");
                return false;
            }

            rows.Add(new ClientPolicyRow(label.Value.Text, parsed));
        }

        policies = rows.Count == 0 ? Empty : new ClientPolicies(rows);
        problem = string.Empty;
        return true;
    }

    /// <summary>Reads the file.</summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="policies">What it holds; <see cref="Empty"/> when there is no file.</param>
    /// <param name="problem">Why it could not be used, otherwise empty.</param>
    /// <returns>Whether the file is absent or well formed.</returns>
    public static bool TryLoad(string path, [NotNullWhen(true)] out ClientPolicies? policies, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);

        policies = null;
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            policies = Empty;
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read: {ex.Message}";
            return false;
        }

        return TryParse(bytes, out policies, out problem);
    }

    /// <summary>Writes the file through a temporary file and a move, owner-only on Unix.</summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="policies">What to write.</param>
    /// <param name="error">Why it was not written, otherwise empty.</param>
    /// <returns>Whether it was written whole.</returns>
    public static bool TrySave(string path, ClientPolicies policies, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(policies);

        var full = Path.GetFullPath(path);
        var staged = full + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)) + ".tmp";

        try
        {
            if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(staged, options))
            {
                stream.Write(Encoding.UTF8.GetBytes(policies.Format()));
                stream.Flush(flushToDisk: true);
            }

            File.Move(staged, full, overwrite: true);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(staged);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The staged file is only ever a whole copy of what failed to land; leaving it costs nothing.
            }

            error = ex.Message;
            return false;
        }
    }

    /// <summary>Whether a label may head a row.</summary>
    /// <param name="label">The label.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>True for <see cref="AnyClient"/> and for 1 to 64 characters the entry-name sanitizer leaves unaltered.</returns>
    /// <remarks>The rule <c>keypaste setup</c> writes labels by, so every label it writes fits.</remarks>
    public static bool IsValidLabel(string label, out string error)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (string.Equals(label, AnyClient, StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        if (label.Length is 0 or > MaximumLabelLength
            || EntryNameSanitizer.Sanitize(label, MaximumLabelLength).WasAltered
            || label.Contains('"', StringComparison.Ordinal)
            || label.Contains('\\', StringComparison.Ordinal))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"a client label is \"*\" or 1 to {MaximumLabelLength} characters with no quote, backslash, slash or control character");
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The word a file and the CLI use for a policy.</summary>
    /// <param name="policy">The policy.</param>
    /// <returns><c>session</c>, <c>ask</c> or <c>inject-only</c>.</returns>
    public static string Wire(ClientPolicy policy) => policy switch
    {
        ClientPolicy.AskEveryTime => "ask",
        ClientPolicy.InjectOnly => "inject-only",
        _ => "session",
    };

    /// <summary>Reads a policy's word.</summary>
    /// <param name="text">The word.</param>
    /// <param name="policy">The policy, when it is one.</param>
    /// <returns>Whether it names a policy, exactly as <see cref="Wire"/> spells it.</returns>
    public static bool TryParseWire(string text, out ClientPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(text);

        (var known, policy) = text switch
        {
            "session" => (true, ClientPolicy.SessionGrants),
            "ask" => (true, ClientPolicy.AskEveryTime),
            "inject-only" => (true, ClientPolicy.InjectOnly),
            _ => (false, ClientPolicy.SessionGrants),
        };

        return known;
    }

    /// <summary>How a screen names a policy.</summary>
    /// <param name="policy">The policy.</param>
    /// <returns>The design's words.</returns>
    public static string Describe(ClientPolicy policy) => policy switch
    {
        ClientPolicy.AskEveryTime => "Ask every time",
        ClientPolicy.InjectOnly => "Inject only: keypaste never hands it a value; only commands you approve can read the values",
        _ => "Session grants up to 1h",
    };

    private static string At(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");
}
