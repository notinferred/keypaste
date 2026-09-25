using System.Text;
using System.Text.Json;

namespace Keypaste.Cli.Output;

/// <summary>The one shape every <c>--json</c> takes: a single-line array of objects on stdout.</summary>
/// <remarks>The default encoder escapes control and non-ASCII characters, so a hostile name reaches a terminal as text.</remarks>
internal static class CliJson
{
    internal const string Option = "json";

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
            }

            json.WriteEndArray();
        }

        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
