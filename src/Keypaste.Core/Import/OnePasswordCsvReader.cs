using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Import;

/// <summary>1Password's CSV export: 1Password 8's, of its Login and Password items, and the common fields earlier versions write with their labels (D-0576).</summary>
/// <remarks>
/// 1Password 8 names its columns <c>Title,Url,Username,Password,OTPAuth,Favorite,Archived,Tags,Notes</c>, and earlier
/// versions' common fields are <c>notes,password,title,type,url,username</c>. A header naming title, url, username,
/// password and notes, in any order and any case, and no column outside those two sets is recognized, so neither another
/// manager's CSV nor 1Password 7's export of all fields is. The export names no vault or folder, so every item lands in
/// the import's own group.
/// </remarks>
internal sealed class OnePasswordCsvReader : IExportReader
{
    /// <summary>KeePassXC's tag for a favorite (D-0574).</summary>
    private const string _favoriteTag = "Favorite";

    /// <summary>KeePassXC's tag for an archived item, which its 1Password importer gives one.</summary>
    private const string _archivedTag = "Archived";

    private static readonly string[] _required = ["title", "url", "username", "password", "notes"];

    private static readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase) { "title", "url", "username", "password", "notes", "otpauth", "favorite", "archived", "tags", "type" };

    /// <summary>The separators of a tag list, as KeePass splits one.</summary>
    private static readonly char[] _tagSeparators = [',', ';'];

    public string Format => "1Password CSV";

    public int MaximumBytes => 32 * 1024 * 1024;

    public bool Recognizes(string fileName, ReadOnlySpan<byte> start) =>
        Csv.FirstRecord(start) is { } header
        && header.All(column => column.Length == 0 || _read.Contains(column))
        && _required.All(column => header.Contains(column, StringComparer.OrdinalIgnoreCase));

    public ExportContents Read(ReadOnlySpan<byte> file)
    {
        var records = CsvExport.Records(file);
        var columns = CsvExport.Columns(records[0].Fields, _required, StringComparer.OrdinalIgnoreCase);
        List<ExportItem> items = [];
        List<ExportOmission> unread = [];

        foreach (var record in records.Skip(1))
        {
            string Cell(string column) => columns.TryGetValue(column, out var index) ? record.Fields[index] : string.Empty;

            if (CsvExport.Empty(record) is { } empty)
            {
                unread.Add(empty);
            }
            else
            {
                items.Add(Item(Cell, CsvExport.UnreadColumns(columns, record, _read, "1Password")));
            }
        }

        return new ExportContents(items, unread);
    }

    private static ExportItem Item(Func<string, string> cell, List<ExportOmission> omitted)
    {
        var title = cell("title");
        var username = cell("username");
        var url = cell("url");
        List<string> tags = [.. cell("tags").Split(_tagSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

        if (cell("type") is { Length: > 0 } type)
        {
            tags.Add(type);
        }

        if (Marked(cell("favorite"), "favorite mark", omitted))
        {
            tags.Add(_favoriteTag);
        }

        if (Marked(cell("archived"), "archived mark", omitted))
        {
            tags.Add(_archivedTag);
        }

        return new ExportItem
        {
            Title = title,
            Username = username,
            Password = cell("password"),
            Urls = url.Length == 0 ? [] : [url],
            Notes = cell("notes"),
            Tags = tags,
            Totp = CsvExport.Totp(cell("otpauth"), title, username),
            Omitted = omitted,
        };
    }

    /// <summary>Whether 1Password's <c>true</c> marks the item; a value other than <c>true</c>, <c>false</c> or none is named as not imported.</summary>
    private static bool Marked(string value, string part, List<ExportOmission> omitted)
    {
        switch (value)
        {
            case "true":
                return true;
            case "" or "false":
                return false;
            default:
                omitted.Add(new ExportOmission(part, "1Password writes true or false here and no other value"));
                return false;
        }
    }
}
