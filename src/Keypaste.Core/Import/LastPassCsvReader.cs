using Keypaste.Infrastructure;

namespace Keypaste.Core.Import;

/// <summary>LastPass's CSV export, as its vault and its command line, <c>lpass export</c>, write it (D-0572).</summary>
/// <remarks>
/// <para>
/// The header names the columns: <c>url,username,password,totp,extra,name,grouping,fav</c> from the vault, and the same
/// without <c>totp</c> from <c>lpass</c>. A row is a site, whose <c>extra</c> is its notes, or, when its <c>url</c> is
/// <c>http://sn</c>, a secure note; <c>grouping</c> is its folder, a <c>\</c> between the names. A column keypaste does
/// not read is named on each item that has a value in it.
/// </para>
/// <para>
/// A secure note whose text starts <c>NoteType:</c> is read as LastPass's own <c>lpass</c> expands one
/// (<c>notes_expand</c>): each line is <c>name:value</c>, a line with no colon continues the value before it, as does
/// a line of an SSH key's private key whose name is not one of that note's, and <c>Notes:</c> takes the rest.
/// </para>
/// </remarks>
internal sealed class LastPassCsvReader : IExportReader
{
    private const string _secureNoteUrl = "http://sn";
    private const string _emptyFolderUrl = "http://group";
    private const string _noFolder = "(none)";
    private const string _noteType = "NoteType";
    private const string _notes = "Notes";
    private const string _privateKey = "Private Key";

    /// <summary>KeePassXC's tag for a favorite, which its Bitwarden and 1Password importers give one (D-0574).</summary>
    private const string _favoriteTag = "Favorite";

    private static readonly string[] _required = ["url", "username", "password", "extra", "name", "grouping"];

    private static readonly HashSet<string> _read = new(StringComparer.Ordinal) { "url", "username", "password", "totp", "extra", "name", "grouping", "fav" };

    /// <summary>The names of LastPass's SSH key note, any other of which continues its private key, as <c>lpass</c>'s <c>note_has_field</c> decides, and <c>Notes</c>.</summary>
    private static readonly HashSet<string> _sshKeyNames = new(StringComparer.Ordinal) { "Bit Strength", "Format", "Passphrase", _privateKey, "Public Key", "Hostname", "Date", _notes };

    public string Format => "LastPass CSV";

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
            else if (Cell("url") == _emptyFolderUrl)
            {
                unread.Add(new ExportOmission(CsvExport.Named("folder", Cell("grouping")), "LastPass lists an empty folder this way, and an import makes a group only for an item in it"));
            }
            else
            {
                items.Add(Item(Cell, CsvExport.UnreadColumns(columns, record, _read, "LastPass")));
            }
        }

        return new ExportContents(items, unread);
    }

    private static ExportItem Item(Func<string, string> cell, List<ExportOmission> omitted)
    {
        var title = cell("name");
        var username = cell("username");
        var password = cell("password");
        var url = cell("url");
        var notes = cell("extra");
        List<string> tags = [];
        List<ExportField> fields = [];

        if (url == _secureNoteUrl)
        {
            url = string.Empty;

            if (notes.StartsWith(_noteType + ":", StringComparison.Ordinal))
            {
                var (values, rest) = Expand(notes);
                notes = rest;

                foreach (var (name, value) in values.Where(pair => pair.Value.Length > 0))
                {
                    switch (name)
                    {
                        case _noteType:
                            tags.Add(value);
                            break;
                        case "Language":
                            omitted.Add(new ExportOmission("field Language", "it is the language of LastPass's form for the note, which keypaste does not keep"));
                            break;
                        case "Username" when username.Length == 0:
                            username = value;
                            break;
                        case "Password" when password.Length == 0:
                            password = value;
                            break;
                        case "URL" when url.Length == 0:
                            url = value;
                            break;
                        default:
                            fields.Add(new ExportField(name, value));
                            break;
                    }
                }
            }
        }

        switch (cell("fav"))
        {
            case "1":
                tags.Add(_favoriteTag);
                break;
            case "" or "0":
                break;
            default:
                omitted.Add(new ExportOmission("favorite mark", "LastPass marks a favorite with 1 and no other value"));
                break;
        }

        return new ExportItem
        {
            Title = title,
            Folder = Folder(cell("grouping")),
            Username = username,
            Password = password,
            Urls = url.Length == 0 ? [] : [url],
            Notes = notes,
            Fields = fields,
            Tags = tags,
            Totp = CsvExport.Totp(cell("totp"), title, username),
            Omitted = omitted,
        };
    }

    /// <summary>A typed secure note's <c>name:value</c> lines in order, and its notes, the text after <c>Notes:</c> less one closing line break.</summary>
    private static (List<(string Name, string Value)> Values, string Notes) Expand(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var sshKey = lines[0] == $"{_noteType}:SSH Key";
        List<(string Name, string Value)> values = [];

        for (var index = 0; index < count; index++)
        {
            var line = lines[index];
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var name = colon < 0 ? string.Empty : line[..colon];
            var continues = values.Count > 0
                && (colon < 0 || (sshKey && values[^1].Name == _privateKey && !_sshKeyNames.Contains(name)));

            if (continues)
            {
                values[^1] = (values[^1].Name, values[^1].Value + "\n" + line);
            }
            else if (name == _notes)
            {
                return (values, string.Join('\n', lines, index, count - index)[(colon + 1)..]);
            }
            else
            {
                values.Add((name, line[(colon + 1)..]));
            }
        }

        return (values, string.Empty);
    }

    private static string[] Folder(string grouping) =>
        grouping is "" or _noFolder ? [] : grouping.Split('\\', StringSplitOptions.RemoveEmptyEntries);
}
