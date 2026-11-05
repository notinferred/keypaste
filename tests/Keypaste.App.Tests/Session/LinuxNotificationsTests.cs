using System.Diagnostics;
using Keypaste.App.Notifications;
using Keypaste.Core.Approval;
using Tmds.DBus.Protocol;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// keypaste's notifications over a real message bus, a private <c>dbus-daemon</c> with a stand-in for the desktop's
/// notification server on it, reading what actually crosses the bus (Q.14).
/// </summary>
public sealed class LinuxNotificationsTests
{
    private const string _service = "org.freedesktop.Notifications";
    private const string _path = "/org/freedesktop/Notifications";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_address_nothing_listens_on_posts_nothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no Unix socket path to name.");

        using var notifications = new LinuxNotifications("unix:path=/nonexistent/keypaste-q14-bus");

        Assert.Null(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }).WaitAsync(_wait, Token));
    }

    [Fact]
    public async Task A_bus_with_no_notification_server_posts_nothing()
    {
        using var bus = PrivateBus.Start();
        using var notifications = new LinuxNotifications(bus.Address);

        Assert.Null(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }).WaitAsync(_wait, Token));
    }

    [Fact]
    public async Task A_server_that_shows_no_actions_posts_nothing()
    {
        using var bus = PrivateBus.Start();
        using var server = await FakeServer.StartAsync(bus.Address, ["body"]);
        using var notifications = new LinuxNotifications(bus.Address);

        Assert.Null(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }).WaitAsync(_wait, Token));
        Assert.Empty(server.Notified);
    }

    [Fact]
    public async Task What_crosses_the_bus_is_the_fixed_words_and_a_press_of_that_notice_alone_reaches_keypaste()
    {
        using var bus = PrivateBus.Start();
        using var server = await FakeServer.StartAsync(bus.Address, ["actions", "body"]);
        using var notifications = new LinuxNotifications(bus.Address);
        using var pressed = new SemaphoreSlim(0);

        var posted = await notifications.PostAsync(SystemNotice.RequestWaiting, () => pressed.Release()).WaitAsync(_wait, Token);

        Assert.NotNull(posted);
        var notified = Assert.Single(server.Notified);
        Assert.Equal("keypaste", notified.App);
        Assert.Equal(string.Empty, notified.Icon);
        Assert.Equal(SystemNotice.RequestWaiting.Title, notified.Title);
        Assert.Equal(SystemNotice.RequestWaiting.Body, notified.Body);
        Assert.Equal(["default", SystemNotice.RequestWaiting.Action, "press", SystemNotice.RequestWaiting.Action], notified.Actions);
        Assert.Equal(2, notified.Urgency);
        Assert.Equal(ApprovalLimits.Default.Window.TotalMilliseconds, notified.Expiry);

        server.Invoke(notified.Id + 1);
        await Task.Delay(200, Token);
        Assert.Equal(0, pressed.CurrentCount);

        server.Invoke(notified.Id);
        Assert.True(await pressed.WaitAsync(_wait, Token), "a press of the notice never reached keypaste");

        server.Invoke(notified.Id, "press");
        Assert.True(await pressed.WaitAsync(_wait, Token), "a press of its button never reached keypaste");

        posted.Remove();
        await Until(() => server.Closed.Contains(notified.Id));

        server.Invoke(notified.Id);
        await Task.Delay(200, Token);
        Assert.Equal(0, pressed.CurrentCount);
    }

    [Fact]
    public async Task The_locked_notice_is_ordinary_where_a_request_is_urgent()
    {
        using var bus = PrivateBus.Start();
        using var server = await FakeServer.StartAsync(bus.Address, ["actions"]);
        using var notifications = new LinuxNotifications(bus.Address);

        Assert.NotNull(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }).WaitAsync(_wait, Token));

        var notified = Assert.Single(server.Notified);
        Assert.Equal(1, notified.Urgency);
        Assert.Equal(-1, notified.Expiry);
        Assert.Equal(["default", "Open keypaste", "press", "Open keypaste"], notified.Actions);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the bus never reached that state");
            await Task.Delay(20, Token);
        }
    }

    private sealed class PrivateBus : IDisposable
    {
        private readonly Process _daemon;

        private PrivateBus(Process daemon, string address)
        {
            _daemon = daemon;
            Address = address;
        }

        internal string Address { get; }

        internal static PrivateBus Start()
        {
            var program = new[] { "/usr/bin/dbus-daemon", "/bin/dbus-daemon" }.FirstOrDefault(File.Exists);
            Assert.SkipWhen(program is null, "No dbus-daemon to start a private bus with.");

            var start = new ProcessStartInfo(program!, "--session --nofork --print-address")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var daemon = Process.Start(start)!;
            var address = daemon.StandardOutput.ReadLine();
            Assert.False(string.IsNullOrEmpty(address), "dbus-daemon printed no address");
            return new PrivateBus(daemon, address!);
        }

        public void Dispose()
        {
            _daemon.Kill();
            _daemon.WaitForExit();
            _daemon.Dispose();
        }
    }

    private sealed record Notification(uint Id, string App, string Icon, string Title, string Body, string[] Actions, byte Urgency, int Expiry);

    /// <summary>What the desktop's notification server does on the bus, without a screen.</summary>
    private sealed class FakeServer : IDisposable, IPathMethodHandler
    {
        private readonly DBusConnection _connection;
        private readonly string[] _capabilities;
        private uint _lastId;

        private FakeServer(DBusConnection connection, string[] capabilities)
        {
            _connection = connection;
            _capabilities = capabilities;
        }

        public string Path => _path;

        public bool HandlesChildPaths => false;

        internal List<Notification> Notified { get; } = [];

        internal List<uint> Closed { get; } = [];

        internal static async Task<FakeServer> StartAsync(string address, string[] capabilities)
        {
            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            var server = new FakeServer(connection, capabilities);
            connection.AddMethodHandler(server);

            await RequestNameAsync(connection);
            return server;
        }

        private static Task RequestNameAsync(DBusConnection connection)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su");
            writer.WriteString(_service);
            writer.WriteUInt32(0);
            return connection.CallMethodAsync(writer.CreateMessage());
        }

        internal void Invoke(uint id, string key = "default")
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteSignalHeader(null, _path, _service, "ActionInvoked", "us");
            writer.WriteUInt32(id);
            writer.WriteString(key);
            _ = _connection.TrySendMessage(writer.CreateMessage());
        }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            switch (context.Request.MemberAsString)
            {
                case "GetCapabilities":
                    {
                        using var reply = context.CreateReplyWriter("as");
                        reply.WriteArray(_capabilities);
                        context.Reply(reply.CreateMessage());
                        break;
                    }

                case "Notify":
                    {
                        var reader = context.Request.GetBodyReader();
                        var app = reader.ReadString();
                        _ = reader.ReadUInt32();
                        var icon = reader.ReadString();
                        var title = reader.ReadString();
                        var body = reader.ReadString();
                        var actions = reader.ReadArrayOfString();
                        var hints = reader.ReadDictionaryOfStringToVariantValue();
                        var expiry = reader.ReadInt32();
                        var id = ++_lastId;
                        Notified.Add(new Notification(id, app, icon, title, body, actions, hints["urgency"].GetByte(), expiry));

                        using var reply = context.CreateReplyWriter("u");
                        reply.WriteUInt32(id);
                        context.Reply(reply.CreateMessage());
                        break;
                    }

                case "CloseNotification":
                    {
                        var reader = context.Request.GetBodyReader();
                        Closed.Add(reader.ReadUInt32());
                        break;
                    }

                default:
                    context.ReplyUnknownMethodError();
                    break;
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => _connection.Dispose();
    }
}
