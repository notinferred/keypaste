using Tmds.DBus.Protocol;

namespace Keypaste.App;

/// <summary>Whether a Linux desktop reports light or dark, which Avalonia's own reading cannot say, since it draws "no preference" as light.</summary>
internal static class SystemAppearance
{
    private const string _service = "org.freedesktop.portal.Desktop";
    private const string _path = "/org/freedesktop/portal/desktop";
    private const string _interface = "org.freedesktop.portal.Settings";
    private const string _namespace = "org.freedesktop.appearance";
    private const string _key = "color-scheme";
    private const uint _prefersDark = 1;
    private const uint _prefersLight = 2;

    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(2);

    /// <summary>Asks the desktop's settings portal for the colour scheme it reports.</summary>
    /// <param name="address">The session bus, or null where there is none.</param>
    /// <returns>True only for a portal answering dark or light; no bus, no portal, no answer in time and its "no preference" are all false.</returns>
    internal static async Task<bool> LinuxReportsLightOrDarkAsync(string? address)
    {
        if (address is null)
        {
            return false;
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            return await ColorSchemeAsync(connection).WaitAsync(_bound).ConfigureAwait(false) is _prefersDark or _prefersLight;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or ObjectDisposedException or IOException or TimeoutException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static Task<uint> ColorSchemeAsync(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_service, _path, _interface, "Read", "ss");
        writer.WriteString(_namespace);
        writer.WriteString(_key);

        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return ReadUInt32(reader.ReadVariantValue());
            },
            null);
    }

    // Read wraps the value in a variant of its own on portals that predate ReadOne, and some answer with it bare.
    private static uint ReadUInt32(VariantValue value) =>
        value.Type == VariantValueType.Variant ? ReadUInt32(value.GetVariantValue()) : value.GetUInt32();
}
