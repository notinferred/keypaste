using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Import;

/// <summary>What the CSV export readers share: the file's records through <see cref="Csv"/> (D-0573), its header's columns, the rows and columns that are no part of an item, and a TOTP as the <c>otp</c> field holds one (D-0574).</summary>
internal static class CsvExport
{
    /// <summary>Every record of the file, its header first.</summary>
    /// <exception cref="ExportFormatException">The file is not strict CSV, naming the line, or holds no record.</exception>
    internal static IReadOnlyList<CsvRecord> Records(ReadOnlySpan<byte> file)
    {
        if (!Csv.TryRead(file, out var records, out var error))
        {
            throw new ExportFormatException(error);
        }

        return records.Count == 0 ? throw new ExportFormatException("it is empty") : records;
    }

    /// <summary>Where each column of the header is, by name as <paramref name="comparer"/> matches names.</summary>
    /// <exception cref="ExportFormatException">The header names a column twice or lacks one of <paramref name="required"/>.</exception>
    internal static Dictionary<string, int> Columns(IReadOnlyList<string> header, IEnumerable<string> required, StringComparer comparer)
    {
        Dictionary<string, int> columns = new(comparer);

        for (var index = 0; index < header.Count; index++)
        {
            if (!columns.TryAdd(header[index], index))
            {
                throw new ExportFormatException($"its header names the {header[index]} column twice");
            }
        }

        if (required.FirstOrDefault(column => !columns.ContainsKey(column)) is { } missing)
        {
            throw new ExportFormatException($"its header has no {missing} column");
        }

        return columns;
    }

    /// <summary>A row with nothing in any column, which is no item, named by its line; null for any other.</summary>
    internal static ExportOmission? Empty(CsvRecord record) =>
        record.Fields.All(field => field.Length == 0) ? new ExportOmission(record.ToString(), "it holds nothing") : null;

    /// <summary>Each column of the row that its reader does not read and that holds a value, in the header's order.</summary>
    /// <param name="columns">The header's columns.</param>
    /// <param name="record">The row.</param>
    /// <param name="read">The columns the reader reads.</param>
    /// <param name="source">The program whose export it is, as the reason names it.</param>
    internal static List<ExportOmission> UnreadColumns(Dictionary<string, int> columns, CsvRecord record, IReadOnlySet<string> read, string source) =>
    [
        .. columns
            .Where(column => !read.Contains(column.Key) && record.Fields[column.Value].Length > 0)
            .OrderBy(column => column.Value)
            .Select(column => new ExportOmission(Named("column", column.Key), $"keypaste does not read this column of a {source} export")),
    ];

    /// <summary>The TOTP as the <c>otp</c> field holds one: a link as written, and a bare secret in the link KeePassXC's importers make of it (D-0574).</summary>
    internal static string Totp(string totp, string title, string username) =>
        string.IsNullOrWhiteSpace(totp) ? string.Empty
        : totp.StartsWith("otpauth://", StringComparison.Ordinal) ? totp
        : $"otpauth://totp/{Uri.EscapeDataString(title)}:{Uri.EscapeDataString(username)}?secret={Uri.EscapeDataString(totp)}";

    internal static string Named(string kind, string name) => name.Length == 0 ? $"a {kind} with no name" : $"{kind} {name}";
}
