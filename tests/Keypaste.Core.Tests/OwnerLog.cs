using System.Text.Json;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The audit log of an owner's home in a test's directory, open for it to record in, and the lines it holds.</summary>
internal sealed class OwnerLog : IDisposable
{
    /// <summary>Opens the log of a home.</summary>
    /// <param name="home">The owner's home directory.</param>
    internal OwnerLog(string home)
    {
        Path = KeypasteHome.AuditPath(home);
        Assert.True(AuditLog.TryOpen(Path, TimeProvider.System, out var log, out var error), error);
        Log = log;
    }

    internal string Path { get; }

    internal AuditLog Log { get; }

    /// <summary>Every line so far.</summary>
    internal List<JsonElement> Lines()
    {
        return [.. Text().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    /// <summary>The log's text, read as a reader must while the owner holds it open, or nothing before it exists.</summary>
    internal string Text()
    {
        if (!File.Exists(Path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public void Dispose() => Log.Dispose();
}
