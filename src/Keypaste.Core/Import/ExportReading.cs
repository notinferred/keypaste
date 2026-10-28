namespace Keypaste.Core.Import;

/// <summary>Reads one password manager's export into items, for <see cref="ExportImport"/>: one class per format.</summary>
/// <remarks>
/// A reader is handed the file's bytes, read once into memory and zeroed when it returns, and touches no file
/// itself, so no plaintext copy of the export is written anywhere (PRODUCT §3.4). What it finds and cannot carry it
/// names, never by value: a record that is no item in <see cref="ExportContents.Unread"/>, a part of an item in
/// <see cref="ExportItem.Omitted"/>.
/// </remarks>
public interface IExportReader
{
    /// <summary>The format as a person names it, such as <c>Bitwarden JSON</c>.</summary>
    string Format { get; }

    /// <summary>The largest export it reads, in bytes; a larger file is refused before it is read.</summary>
    int MaximumBytes { get; }

    /// <summary>Whether a file is an export of this format, judged by its name and first bytes.</summary>
    /// <param name="fileName">The file's name, without its directory.</param>
    /// <param name="start">Its first <see cref="ExportImport.StartBytes"/> bytes, or the whole of a shorter file.</param>
    /// <returns><see langword="true"/> when this reader reads it.</returns>
    bool Recognizes(string fileName, ReadOnlySpan<byte> start);

    /// <summary>Reads every item of an export.</summary>
    /// <param name="file">The whole file, which the caller zeroes once this returns.</param>
    /// <returns>Its items, and the records that are none.</returns>
    /// <exception cref="ExportFormatException">The file is not an export this reader reads; the message names a place in it, never a value.</exception>
    ExportContents Read(ReadOnlySpan<byte> file);
}

/// <summary>A reader's refusal of a file, whose message names a place in it and never a value.</summary>
public sealed class ExportFormatException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public ExportFormatException()
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    /// <param name="message">What is wrong and where, never a value.</param>
    public ExportFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and cause.</summary>
    /// <param name="message">What is wrong and where, never a value.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ExportFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>What a reader read from one export.</summary>
/// <param name="Items">Its items, in the export's order.</param>
/// <param name="Unread">The records it holds that the reader read as no item, each named with why.</param>
public sealed record ExportContents(IReadOnlyList<ExportItem> Items, IReadOnlyList<ExportOmission> Unread);

/// <summary>Something an export holds that does not become part of an item, named with why and never by value.</summary>
/// <param name="Part">What it is, such as <c>passkey</c> or <c>card Visa</c>.</param>
/// <param name="Reason">Why it is not imported.</param>
public sealed record ExportOmission(string Part, string Reason);

/// <summary>One item of an export as its reader read it, before anything decides where it lands.</summary>
/// <remarks>Its values are strings, which cannot be zeroed (SECURITY.md); <see cref="ToString"/> gives the title alone.</remarks>
public sealed class ExportItem
{
    /// <summary>The item's name in the export.</summary>
    public required string Title { get; init; }

    /// <summary>The folder it is in, outermost first, each a name with no separator; empty for none.</summary>
    public IReadOnlyList<string> Folder { get; init; } = [];

    /// <summary>The user name, or empty.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The password, or empty.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Its web addresses: the first is the item's URL, and the rest land as <c>KP2A_URL_1</c>, <c>KP2A_URL_2</c> …, as KeePassXC keeps further ones.</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>The notes, or empty.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Its other named values.</summary>
    public IReadOnlyList<ExportField> Fields { get; init; } = [];

    /// <summary>Its tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Its TOTP as KeePassXC's <c>otp</c> field holds one, an <c>otpauth://totp/</c> URI or KeeOTP's <c>key=</c> form, or empty; it lands in <c>otp</c> when keypaste reads it as written.</summary>
    public string Totp { get; init; } = string.Empty;

    /// <summary>Its files.</summary>
    public IReadOnlyList<ExportAttachment> Attachments { get; init; } = [];

    /// <summary>Its earlier versions.</summary>
    public IReadOnlyList<ExportVersion> History { get; init; } = [];

    /// <summary>When it was created, or null when the export does not say; the import's time then.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When it was last changed, or null when the export does not say; the import's time then, and always a later second than every earlier version.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>What the reader found on it and could not carry, such as a passkey.</summary>
    public IReadOnlyList<ExportOmission> Omitted { get; init; } = [];

    /// <summary>The title only, so a log line or a debugger never shows a value.</summary>
    /// <returns>The title.</returns>
    public override string ToString() => Title;
}

/// <summary>One named value of an exported item beyond its standard fields.</summary>
/// <param name="Name">The name, as the export holds it.</param>
/// <param name="Value">The value.</param>
/// <param name="Protect">Whether the vault protects it, as it protects a password; a reader leaves it plain only for a value the export shows openly.</param>
public sealed record ExportField(string Name, string Value, bool Protect = true)
{
    /// <summary>The name only, so a log line or a debugger never shows the value.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}

/// <summary>One file of an exported item, whose bytes the <see cref="ExportSource"/> holding it zeroes when disposed.</summary>
public sealed class ExportAttachment
{
    private readonly byte[] _content;

    /// <summary>Creates an attachment that owns <paramref name="content"/>.</summary>
    /// <param name="name">The file's name, as the export holds it.</param>
    /// <param name="content">Its bytes, which are not copied and are zeroed with the source.</param>
    public ExportAttachment(string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(content);

        Name = name;
        _content = content;
    }

    /// <summary>The file's name, as the export holds it.</summary>
    public string Name { get; }

    /// <summary>Its length in bytes.</summary>
    public int Size => _content.Length;

    /// <summary>Its bytes, all zero once the source holding it is disposed.</summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>The bytes the vault copies.</summary>
    internal byte[] Bytes => _content;

    /// <summary>The name only.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;

    internal void Zero() => CryptographicOperations.ZeroMemory(_content);
}

/// <summary>An earlier version of an exported item: its standard fields as they were, and when.</summary>
/// <param name="Modified">When it was saved, which orders the versions.</param>
/// <param name="Username">The user name then.</param>
/// <param name="Password">The password then.</param>
/// <param name="Url">The main web address then.</param>
/// <param name="Notes">The notes then.</param>
public sealed record ExportVersion(DateTimeOffset Modified, string Username, string Password, string Url, string Notes)
{
    /// <summary>When it was current, never a value.</summary>
    /// <returns>The time, in ISO 8601.</returns>
    public override string ToString() => Modified.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
