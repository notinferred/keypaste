using System.Text;
using System.Text.Json;

namespace Keypaste.Cli.Output;

/// <summary>The one shape every <c>--json</c> takes: a single-line array of objects on stdout.</summary>
/// <remarks>The default encoder escapes control and non-ASCII characters, so a hostile name reaches a terminal as text.</remarks>
internal static class CliJson
{
    internal const string Option = "json";

    // Written out in chunks of about this size, so a long log is never held whole as bytes and again as a string.
    private const int _chunkBytes = 64 * 1024;

    internal static void WriteArray<T>(TextWriter writer, IEnumerable<T> items, Action<Utf8JsonWriter, T> write)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(write);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var item in items)
            {
                json.WriteStartObject();
                write(json, item);
                json.WriteEndObject();

                // Only between objects, so a chunk always ends on a whole character.
                if (json.BytesPending >= _chunkBytes)
                {
                    json.Flush();
                    Drain(buffer, writer);
                }
            }

            json.WriteEndArray();
        }

        Drain(buffer, writer);
        writer.WriteLine();
    }

    private static void Drain(MemoryStream buffer, TextWriter writer)
    {
        writer.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        buffer.SetLength(0);
    }
}
