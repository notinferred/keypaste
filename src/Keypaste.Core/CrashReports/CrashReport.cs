namespace Keypaste.Core.CrashReports;

/// <summary>
/// A crash report as <see cref="CrashScrubber"/> wrote it: the exact bytes the outbox keeps and the sender posts, with nothing
/// added later (D-0558).
/// </summary>
public sealed class CrashReport
{
    private readonly byte[] _json;

    internal CrashReport(byte[] json, int count, string identity, DateOnly occurredOn)
    {
        _json = json;
        Count = count;
        Identity = identity;
        OccurredOn = occurredOn;
    }

    /// <summary>The report as UTF-8 JSON.</summary>
    public ReadOnlyMemory<byte> Json => _json;

    /// <summary>The same JSON as text, to show the person what would be sent.</summary>
    public string Text => Encoding.UTF8.GetString(_json);

    /// <summary>How many identical failures this report stands for.</summary>
    public int Count { get; }

    /// <summary>The lowercase hex SHA-256 of the report with its count left out, which identical reports share.</summary>
    internal string Identity { get; }

    /// <summary>The UTC date it happened.</summary>
    internal DateOnly OccurredOn { get; }
}
