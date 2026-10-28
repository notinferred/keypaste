using System.Globalization;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Import;

/// <summary>KeePassXC's CSV export, as its <c>CsvExporter</c> writes one from 2.6 through 2.8 (D-0577).</summary>
/// <remarks>
/// The header is <c>"Group","Title","Username","Password","URL","Notes","TOTP","Icon","Last Modified","Created"</c>, every field
/// quoted, one row per item. <c>Group</c> is the item's group path, <c>/</c> between the names and the database's root group
/// first, which the import's own group stands in for. <c>TOTP</c> is an <c>otpauth://totp/</c> link KeePassXC builds from the
/// item's settings, and the times are UTC in ISO 8601. The export carries none of an item's custom fields, attachments,
/// history, tags or expiry, and holds the recycle bin's items too, which are left behind.
/// </remarks>
internal sealed class KeePassXcCsvReader : IExportReader
{
    /// <summary>The name KeePassXC gives its recycle bin, a group directly below the root.</summary>
    private const string _recycleBin = "Recycle Bin";

    private static readonly string[] _required = ["Group", "Title", "Username", "Password", "URL", "Notes"];

    private static readonly HashSet<string> _read = new(StringComparer.Ordinal) { "Group", "Title", "Username", "Password", "URL", "Notes", "TOTP", "Icon", "Last Modified", "Created" };

    /// <summary>Qt's <c>ISODate</c>, as KeePassXC writes a UTC time, or one with an offset.</summary>
    private static readonly string[] _timeFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"];

    public string Format => "KeePassXC CSV";

    public int MaximumBytes => 32 * 1024 * 1024;

    public bool Recognizes(string fileName, ReadOnlySpan<byte> start) =>
        Csv.FirstRecord(start) is { } header && _required.All(column => header.Contains(column, StringComparer.Ordinal));

    public ExportContents Read(ReadOnlySpan<byte> file)
    {
        var records = CsvExport.Records(file);
        var columns = CsvExport.Columns(records[0].Fields, _required, StringComparer.Ordinal);
        List<ExportItem> items = [];
        List<ExportOmission> unread = [];

        foreach (var record in records.Skip(1))
        {
            string Cell(string column) => columns.TryGetValue(column, out var index) ? record.Fields[index] : string.Empty;

            if (CsvExport.Empty(record) is { } empty)
            {
                unread.Add(empty);
            }
            else if (Folder(Cell("Group")) is [_recycleBin, ..] folder)
            {
                unread.Add(new ExportOmission($"item {string.Join('/', folder.Append(Cell("Title")))}", "it was in KeePassXC's recycle bin, which an import leaves behind, as it leaves a KeePass file's"));
            }
            else
            {
                items.Add(Item(Cell, CsvExport.UnreadColumns(columns, record, _read, "KeePassXC")));
            }
        }

        return new ExportContents(items, unread);
    }

    private static ExportItem Item(Func<string, string> cell, List<ExportOmission> omitted)
    {
        var title = cell("Title");
        var username = cell("Username");
        var url = cell("URL");

        if (cell("Icon") is not ("" or "0"))
        {
            omitted.Add(new ExportOmission("icon", "an imported item gets KeePass's first icon, the key"));
        }

        return new ExportItem
        {
            Title = title,
            Folder = Folder(cell("Group")),
            Username = username,
            Password = cell("Password"),
            Urls = url.Length == 0 ? [] : [url],
            Notes = cell("Notes"),
            Totp = CsvExport.Totp(cell("TOTP"), title, username),
            Modified = Time(cell("Last Modified"), "Last Modified", omitted),
            Created = Time(cell("Created"), "Created", omitted),
            Omitted = omitted,
        };
    }

    /// <summary>The group path less its first name, the root group's, split as KeePassXC's own CSV import splits one, an empty name skipped.</summary>
    private static string[] Folder(string group) => [.. group.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1)];

    /// <summary>A time as KeePassXC writes one; one that is not is named as not imported, and the import's time is the item's.</summary>
    private static DateTimeOffset? Time(string value, string column, List<ExportOmission> omitted)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            return time;
        }

        omitted.Add(new ExportOmission($"{column} time", "it is not a time as KeePassXC writes one, such as 2024-01-31T09:30:00Z, so the import's time is kept"));
        return null;
    }
}
