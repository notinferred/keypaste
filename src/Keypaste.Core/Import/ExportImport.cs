namespace Keypaste.Core.Import;

/// <summary>Whom an item's landing name is already taken by.</summary>
public enum ExportCollision
{
    /// <summary>Nothing: the item lands under its own name.</summary>
    None = 0,

    /// <summary>An item already in the vault.</summary>
    VaultItem = 1,

    /// <summary>An earlier item of the same export.</summary>
    ExportItem = 2,
}

/// <summary>What happens to an item whose landing name is taken.</summary>
public enum CollisionDecision
{
    /// <summary>It lands too, under the first of <c>title (2)</c>, <c>title (3)</c> … that is free; the default.</summary>
    KeepBoth = 0,

    /// <summary>It is not imported.</summary>
    Skip = 1,
}

/// <summary>One item of an export and where it lands.</summary>
/// <param name="Index">Which item of <see cref="ExportSource.Items"/> it is.</param>
/// <param name="Item">The item as the export names it, its folders and title joined with <c>/</c>, for display.</param>
/// <param name="Lands">Where it lands, or null when it is skipped.</param>
/// <param name="Collision">Whom its name is already taken by.</param>
/// <param name="Decision">What was decided, which only a collision reads.</param>
/// <param name="Note">Why it lands under another name or not at all, naming names and never a value; null when it lands as named.</param>
public sealed record ExportRow(int Index, string Item, EntryName? Lands, ExportCollision Collision, CollisionDecision Decision, string? Note);

/// <summary>Something of an export that does not land, named with why and never by value.</summary>
/// <param name="Index">The item it belongs to, or -1 for a record the reader read as no item.</param>
/// <param name="Item">That item as <see cref="ExportRow.Item"/> names it, or empty.</param>
/// <param name="Part">What does not land, such as <c>TOTP</c>, <c>field PIN</c> or <c>attachment scan.pdf</c>.</param>
/// <param name="Reason">Why.</param>
public sealed record ExportLoss(int Index, string Item, string Part, string Reason);

/// <summary>What importing an export would do, worked out against the vault before anything is written.</summary>
/// <remarks>
/// Every item has a row, landing or skipped, and every part of a landing item that does not land, and every
/// record the reader read as no item, is a loss: nothing of the export is left out without being named.
/// </remarks>
public sealed class ExportPlan
{
    internal ExportPlan(string into, IReadOnlyList<ExportRow> rows, IReadOnlyList<ExportLoss> losses, IReadOnlyList<string> projects, string? refusal)
    {
        Into = into;
        Rows = rows;
        Losses = losses;
        Projects = projects;
        Refusal = refusal;
    }

    /// <summary>The group everything lands under.</summary>
    public string Into { get; }

    /// <summary>One row per item, in the export's order.</summary>
    public IReadOnlyList<ExportRow> Rows { get; }

    /// <summary>What does not land.</summary>
    public IReadOnlyList<ExportLoss> Losses { get; }

    /// <summary>The projects landing items join through their tags, ordinal-sorted.</summary>
    public IReadOnlyList<string> Projects { get; }

    /// <summary>Why nothing can be imported, naming groups and never a value; null when the plan can be committed.</summary>
    public string? Refusal { get; }

    /// <summary>How many items land.</summary>
    public int Landing => Rows.Count(row => row.Lands is not null);

    /// <summary>The decision each colliding row was planned with, to plan again by.</summary>
    internal IReadOnlyDictionary<int, CollisionDecision> Decisions =>
        Rows.Where(row => row.Collision != ExportCollision.None).ToDictionary(row => row.Index, row => row.Decision);

    /// <summary>Whether two plans would write the same.</summary>
    internal bool SameAs(ExportPlan other) =>
        string.Equals(Into, other.Into, StringComparison.Ordinal)
        && string.Equals(Refusal, other.Refusal, StringComparison.Ordinal)
        && Rows.SequenceEqual(other.Rows)
        && Losses.SequenceEqual(other.Losses)
        && Projects.SequenceEqual(other.Projects, StringComparer.Ordinal);
}

/// <summary>Reading another password manager's export to copy its items into the vault in use.</summary>
/// <remarks>
/// The export is plaintext. It is read once, into one buffer that is zeroed when its reader returns, and nothing
/// writes it or any part of it to disk outside the vault; the items it held are strings in memory until the
/// <see cref="ExportSource"/> is dropped. A refusal names the file and never a value.
/// </remarks>
public static class ExportImport
{
    /// <summary>How many of a file's first bytes <see cref="IExportReader.Recognizes"/> is shown.</summary>
    public const int StartBytes = 4096;

    /// <summary>The formats keypaste reads, each by its own reader; the first that recognizes a file reads it.</summary>
    public static IReadOnlyList<IExportReader> Readers { get; } = [new LastPassCsvReader()];

    /// <summary>Reads an export with the first of <paramref name="readers"/> that recognizes it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="readers">The readers to ask, in order.</param>
    /// <returns>What it holds, which the caller disposes; null when no reader recognizes the file.</returns>
    /// <exception cref="VaultException">
    /// The file could not be read, is larger than its reader takes, changed while it was read, or its reader refused it.
    /// </exception>
    public static ExportSource? Open(string path, IReadOnlyList<IExportReader> readers)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(readers);

        if (readers.Count == 0)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        var fileName = Path.GetFileName(full);

        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Recognized(stream, fileName, readers) is { } reader ? Read(stream, fileName, reader) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultException($"{fileName} could not be read: {ex.Message}");
        }
    }

    private static IExportReader? Recognized(FileStream stream, string fileName, IReadOnlyList<IExportReader> readers)
    {
        var start = new byte[StartBytes];

        try
        {
            var length = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);

            foreach (var reader in readers)
            {
                if (reader.Recognizes(fileName, start.AsSpan(0, length)))
                {
                    return reader;
                }
            }

            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(start);
        }
    }

    private static ExportSource Read(FileStream stream, string fileName, IExportReader reader)
    {
        if (stream.Length > reader.MaximumBytes)
        {
            throw new VaultException(
                $"{fileName} is larger than {new EntryAttachment(fileName, reader.MaximumBytes).SizeText}, the most keypaste reads of a {reader.Format} export.");
        }

        stream.Position = 0;

        // One byte past the length measured, so a file that grew while it was read fills the buffer and is refused rather than cut short.
        var buffer = new byte[(int)stream.Length + 1];

        try
        {
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            if (length == buffer.Length)
            {
                throw new VaultException($"{fileName} changed while keypaste read it. Export it again, or wait until it is written.");
            }

            ExportContents contents;

            try
            {
                contents = reader.Read(buffer.AsSpan(0, length));
            }
            catch (ExportFormatException ex)
            {
                throw new VaultException($"{fileName} is not a {reader.Format} export keypaste reads: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The reader's own message may quote the export, so only the format is said.
                throw new VaultException($"{fileName} could not be read as a {reader.Format} export.");
            }

            return new ExportSource(fileName, reader.Format, contents);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
