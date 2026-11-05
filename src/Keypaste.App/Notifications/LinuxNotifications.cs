using System.Collections.Concurrent;
using Keypaste.Core.Approval;
using Tmds.DBus.Protocol;

namespace Keypaste.App.Notifications;

/// <summary>The desktop's notification server, <c>org.freedesktop.Notifications</c>, on the session bus.</summary>
/// <remarks>
/// A desktop with no server, or one whose server shows no actions, has nowhere to put Show the prompt or Open keypaste,
/// so it counts as unable to post. The bus carries the person's other programs too, and any of them may watch it, which is
/// one reason a notice holds only <see cref="SystemNotice"/>'s words.
/// </remarks>
/// <param name="address">The bus's address.</param>
internal sealed class LinuxNotifications(string address) : ISystemNotifications
{
    private const string _service = "org.freedesktop.Notifications";
    private const string _path = "/org/freedesktop/Notifications";
    private const string _appName = "keypaste";
    private const string _desktopEntry = "com.keypaste.app";
    private const string _clickKey = "default";
    private const string _buttonKey = "press";
    private const byte _urgencyNormal = 1;
    private const byte _urgencyCritical = 2;
    private const int _serverDefaultExpiry = -1;

    // A prompt lasts no longer, and a run that dies takes with it the number that could close the notice.
    private static readonly int _requestExpiry = (int)ApprovalLimits.Default.Window.TotalMilliseconds;

    private readonly SemaphoreSlim _connecting = new(1, 1);
    private readonly ConcurrentDictionary<uint, Action> _live = new();
    private DBusConnection? _connection;

    public async Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(notice);

        try
        {
            var connection = await ConnectedAsync().ConfigureAwait(false);

            var capabilities = await CapabilitiesAsync(connection).ConfigureAwait(false);

            if (!capabilities.Contains("actions", StringComparer.Ordinal))
            {
                return null;
            }

            var id = await NotifyAsync(connection, notice).ConfigureAwait(false);
            _live[id] = pressed;
            return new Posted(this, connection, id);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or ObjectDisposedException or IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _live.Clear();
        _connection?.Dispose();
        _connection = null;
        _connecting.Dispose();
    }

    private async Task<DBusConnection> ConnectedAsync()
    {
        await _connecting.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_connection is { } connection)
            {
                return connection;
            }

            var made = new DBusConnection(address);

            try
            {
                await made.ConnectAsync().ConfigureAwait(false);
                await made.WatchSignalAsync(
                    _service,
                    _path,
                    _service,
                    "ActionInvoked",
                    static (Message message, object? _) =>
                    {
                        var reader = message.GetBodyReader();
                        return reader.ReadUInt32();
                    },
                    OnActionInvoked,
                    ObserverFlags.None,
                    false,
                    null).ConfigureAwait(false);
            }
            catch
            {
                made.Dispose();
                throw;
            }

            _connection = made;
            return made;
        }
        finally
        {
            _connecting.Release();
        }
    }

    private void OnActionInvoked(Notification<uint> signal)
    {
        if (!signal.HasValue)
        {
            // A dropped bus takes its signals with it; the next post connects again.
            _connection = null;
        }
        else if (_live.TryGetValue(signal.Value, out var pressed))
        {
            pressed();
        }
    }

    private static Task<string[]> CapabilitiesAsync(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_service, _path, _service, "GetCapabilities");

        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return reader.ReadArrayOfString();
            },
            null);
    }

    private static Task<uint> NotifyAsync(DBusConnection connection, SystemNotice notice)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(_service, _path, _service, "Notify", "susssasa{sv}i");
        writer.WriteString(_appName);
        writer.WriteUInt32(0);
        writer.WriteString(string.Empty);
        writer.WriteString(notice.Title);
        writer.WriteString(notice.Body);

        // The specification lets a server keep the default action's label off the screen, so the same words go as a button too.
        writer.WriteArray(new[] { _clickKey, notice.Action, _buttonKey, notice.Action });

        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("urgency");
        writer.WriteVariant(VariantValue.Byte(notice.TimeSensitive ? _urgencyCritical : _urgencyNormal));
        writer.WriteDictionaryEntryStart();
        writer.WriteString("desktop-entry");
        writer.WriteVariant(VariantValue.String(_desktopEntry));
        writer.WriteDictionaryEnd(hints);

        writer.WriteInt32(notice.TimeSensitive ? _requestExpiry : _serverDefaultExpiry);

        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                return reader.ReadUInt32();
            },
            null);
    }

    private sealed class Posted(LinuxNotifications owner, DBusConnection connection, uint id) : IPostedNotice
    {
        public void Remove()
        {
            owner._live.TryRemove(id, out _);

            try
            {
                using var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(_service, _path, _service, "CloseNotification", "u", MessageFlags.NoReplyExpected);
                writer.WriteUInt32(id);
                connection.TrySendMessage(writer.CreateMessage());
            }
            catch (ObjectDisposedException)
            {
                // The app is quitting and the connection went first; a press now reaches nothing either way.
            }
        }
    }
}
