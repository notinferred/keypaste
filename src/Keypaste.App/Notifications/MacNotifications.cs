using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Keypaste.App.Notifications;

/// <summary>macOS's <c>UNUserNotificationCenter</c>, which Avalonia and .NET offer no call for, reached through the Objective-C runtime as <see cref="MacDock"/> is.</summary>
/// <remarks>
/// <para>
/// Only a process in an app bundle may ask for the center, which raises an Objective-C exception, ending the process,
/// in any other; <see cref="InBundle"/> keeps a development build from asking. Only <see cref="AskPermission"/> asks the
/// person, never a notice, so the system's dialog does not appear beside a prompt. Permission not yet given, permission
/// refused, alerts turned off in the system's settings and an add the system answers with an error all count as unable to post.
/// </para>
/// <para>
/// The system shows a notice marked time-sensitive as an ordinary one: the entitlement that would let it pass Focus does not exist (Q.14).
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacNotifications : ISystemNotifications
{
    private const string _objc = "/usr/lib/libobjc.A.dylib";
    private const string _framework = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";
    private const nuint _alert = 1 << 2;
    private const nuint _foreground = 1 << 2;
    private const nuint _presentAsBanner = 1 << 4;
    private const nuint _presentInList = 1 << 3;
    private const nuint _interruptionActive = 1;
    private const nuint _interruptionTimeSensitive = 2;
    private const string _actionId = "press";
    private const int _blockIsGlobal = 1 << 28;
    private const nint _authorizationNotDetermined = 0;
    private const nint _authorizationDenied = 1;
    private const nint _settingEnabled = 2;
    private const nint _alertStyleNone = 0;

    // Held here so the collector leaves them alone while Objective-C holds their function pointers.
    private static readonly Authorized _onAuthorized = OnAuthorized;
    private static readonly SettingsRead _onSettings = OnSettings;
    private static readonly Added _onAdded = OnAdded;
    private static readonly DelegateMethod _onResponse = OnResponse;
    private static readonly DelegateMethod _onPresent = OnPresent;

    // The system answers each call and every answer in a burst says the same, so one block serves them all.
    private static readonly ConcurrentQueue<TaskCompletionSource<Permission>> _reading = new();
    private static readonly ConcurrentQueue<TaskCompletionSource<bool>> _adding = new();
    private static readonly ConcurrentDictionary<string, Action> _live = new(StringComparer.Ordinal);
    private static readonly Lazy<nint> _delegate = new(MakeDelegate);
    private static readonly Lazy<nint> _authorizedBlock = new(AuthorizedBlock);
    private static readonly Lazy<nint> _settingsBlock = new(SettingsBlock);
    private static readonly Lazy<nint> _addedBlock = new(AddedBlock);
    private static readonly Lock _preparing = new();

    private static readonly string[] _centerSelectors =
    [
        "setDelegate:",
        "setNotificationCategories:",
        "requestAuthorizationWithOptions:completionHandler:",
        "getNotificationSettingsWithCompletionHandler:",
        "addNotificationRequest:withCompletionHandler:",
        "removeAllDeliveredNotifications",
        "removeDeliveredNotificationsWithIdentifiers:",
        "removePendingNotificationRequestsWithIdentifiers:",
    ];

    private static readonly string[] _settingsSelectors = ["authorizationStatus", "alertSetting", "alertStyle"];

    private static bool _prepared;

    /// <summary>Whether this process runs from an app bundle, the one place the center may be asked for.</summary>
    internal static bool InBundle()
    {
        var bundle = Send(Class("NSBundle"), Sel("mainBundle"));
        return bundle != 0
            && Send(bundle, Sel("bundleIdentifier")) != 0
            && Read(Send(bundle, Sel("bundlePath"))).EndsWith(".app", StringComparison.Ordinal);
    }

    /// <summary>
    /// The system's record of the person's choice, which asks them nothing, having set up all a notice needs, so
    /// <c>--selftest</c> holds a real bundle to reaching the center, where a mistake ends the process.
    /// </summary>
    internal static async Task<string> ReadPermissionAsync()
    {
        PrepareOnce();

        return (await ReadSettingsAsync().ConfigureAwait(false)).Authorization switch
        {
            _authorizationNotDetermined => "not asked",
            _authorizationDenied => "denied",
            _ => "allowed",
        };
    }

    /// <summary>
    /// Whether the system took a notice given while the person has not allowed them, which it answers showing nothing, so
    /// <c>--selftest</c> holds a real bundle to the add's answer, which only a person's permission otherwise reaches.
    /// </summary>
    internal static async Task<bool> AddWithoutPermissionAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var accepted = await AddAsync(SystemNotice.RequestWaiting, id).ConfigureAwait(false);

        if (accepted)
        {
            new Posted(id).Remove();
        }

        return accepted;
    }

    public async Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(notice);

        PrepareOnce();

        if (!(await ReadSettingsAsync().ConfigureAwait(false)).ShowsAlerts)
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("N");
        _live[id] = pressed;

        if (!await AddAsync(notice, id).ConfigureAwait(false))
        {
            _live.TryRemove(id, out _);
            return null;
        }

        return new Posted(id);
    }

    public void AskPermission()
    {
        PrepareOnce();
        InPool(() => _ = Send(Center(), Sel("requestAuthorizationWithOptions:completionHandler:"), _alert, _authorizedBlock.Value));
    }

    public void Dispose()
    {
        _live.Clear();

        lock (_preparing)
        {
            if (_prepared)
            {
                var pool = PoolPush();
                _ = Send(Center(), Sel("removeAllDeliveredNotifications"));
                _ = Send(Center(), Sel("setDelegate:"), (nint)0);
                PoolPop(pool);
                _prepared = false;
            }
        }
    }

    /// <summary>
    /// What a request for the notice holds as Objective-C reads it back, and whichever of the center's messages the class
    /// does not answer, each of which would end the process, so a test can hold both without a bundle or a person.
    /// </summary>
    internal static (string Identifier, string Title, string Body, string Kind, nuint Level, IReadOnlyList<string> Unanswered) Composed(SystemNotice notice, string id)
    {
        _ = NativeLibrary.Load(_framework);
        var pool = PoolPush();

        try
        {
            var request = Compose(notice, id);
            var content = Send(request, Sel("content"));
            var center = Class("UNUserNotificationCenter");
            var settings = Class("UNNotificationSettings");

            return (
                Read(Send(request, Sel("identifier"))),
                Read(Send(content, Sel("title"))),
                Read(Send(content, Sel("body"))),
                Read(Send(content, Sel("categoryIdentifier"))),
                (nuint)Send(content, Sel("interruptionLevel")),
                [
                    .. _centerSelectors.Where(selector => SendForByte(center, Sel("instancesRespondToSelector:"), Sel(selector)) == 0),
                    .. _settingsSelectors.Where(selector => SendForByte(settings, Sel("instancesRespondToSelector:"), Sel(selector)) == 0),
                ]);
        }
        finally
        {
            PoolPop(pool);
        }
    }

    private static Task<Permission> ReadSettingsAsync()
    {
        var read = new TaskCompletionSource<Permission>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reading.Enqueue(read);
        InPool(() => _ = Send(Center(), Sel("getNotificationSettingsWithCompletionHandler:"), _settingsBlock.Value));
        return read.Task;
    }

    private static Task<bool> AddAsync(SystemNotice notice, string id)
    {
        PrepareOnce();

        var added = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _adding.Enqueue(added);
        InPool(() => _ = Send(Center(), Sel("addNotificationRequest:withCompletionHandler:"), Compose(notice, id), _addedBlock.Value));
        return added.Task;
    }

    // A thread-pool thread has no autorelease pool of its own to drain what these calls return.
    private static void InPool(Action send)
    {
        var pool = PoolPush();

        try
        {
            send();
        }
        finally
        {
            PoolPop(pool);
        }
    }

    private static nint Compose(SystemNotice notice, string id)
    {
        var content = Send(Send(Class("UNMutableNotificationContent"), Sel("alloc")), Sel("init"));
        _ = Send(content, Sel("setTitle:"), String(notice.Title));
        _ = Send(content, Sel("setBody:"), String(notice.Body));
        _ = Send(content, Sel("setCategoryIdentifier:"), String(notice.Kind));
        _ = Send(content, Sel("setInterruptionLevel:"), notice.TimeSensitive ? _interruptionTimeSensitive : _interruptionActive);

        var request = Send(Class("UNNotificationRequest"), Sel("requestWithIdentifier:content:trigger:"), String(id), content, (nint)0);
        _ = Send(content, Sel("release"));
        return request;
    }

    // One action per notice, and whatever a run that died left delivered goes first.
    private static void PrepareOnce()
    {
        lock (_preparing)
        {
            if (!_prepared)
            {
                _prepared = true;
                InPool(Prepare);
            }
        }
    }

    private static void Prepare()
    {
        var center = Center();
        _ = Send(center, Sel("setDelegate:"), _delegate.Value);
        _ = Send(center, Sel("removeAllDeliveredNotifications"));

        var categories = Send(Send(Class("NSMutableSet"), Sel("alloc")), Sel("init"));

        foreach (var notice in SystemNotice.All)
        {
            var action = Send(Class("UNNotificationAction"), Sel("actionWithIdentifier:title:options:"), String(_actionId), String(notice.Action), _foreground);
            var category = Send(
                Class("UNNotificationCategory"),
                Sel("categoryWithIdentifier:actions:intentIdentifiers:options:"),
                String(notice.Kind),
                Send(Class("NSArray"), Sel("arrayWithObject:"), action),
                Send(Class("NSArray"), Sel("array")),
                (nuint)0);
            _ = Send(categories, Sel("addObject:"), category);
        }

        _ = Send(center, Sel("setNotificationCategories:"), categories);
        _ = Send(categories, Sel("release"));
    }

    private static nint Center()
    {
        _ = NativeLibrary.Load(_framework);
        return Send(Class("UNUserNotificationCenter"), Sel("currentNotificationCenter"));
    }

    private static nint MakeDelegate()
    {
        var made = AllocateClass(Class("NSObject"), "KeypasteNotificationDelegate\0"u8.ToArray(), 0);

        _ = AddMethod(made, Sel("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"), Marshal.GetFunctionPointerForDelegate(_onResponse), "v@:@@@\0"u8.ToArray());
        _ = AddMethod(made, Sel("userNotificationCenter:willPresentNotification:withCompletionHandler:"), Marshal.GetFunctionPointerForDelegate(_onPresent), "v@:@@@\0"u8.ToArray());
        RegisterClass(made);

        return Send(Send(made, Sel("alloc")), Sel("init"));
    }

    private static nint AuthorizedBlock() => GlobalBlock(Marshal.GetFunctionPointerForDelegate(_onAuthorized));

    private static nint SettingsBlock() => GlobalBlock(Marshal.GetFunctionPointerForDelegate(_onSettings));

    private static nint AddedBlock() => GlobalBlock(Marshal.GetFunctionPointerForDelegate(_onAdded));

    // A block with no captures, which Block_copy and Block_release leave alone; each lives as long as the process.
    private static nint GlobalBlock(nint invoke)
    {
        var descriptor = Marshal.AllocHGlobal(2 * IntPtr.Size);
        Marshal.WriteIntPtr(descriptor, 0, 0);
        Marshal.WriteIntPtr(descriptor, IntPtr.Size, 4 * IntPtr.Size);

        var block = Marshal.AllocHGlobal(4 * IntPtr.Size);
        Marshal.WriteIntPtr(block, 0, NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock"));
        Marshal.WriteInt32(block, IntPtr.Size, _blockIsGlobal);
        Marshal.WriteInt32(block, IntPtr.Size + sizeof(int), 0);
        Marshal.WriteIntPtr(block, 2 * IntPtr.Size, invoke);
        Marshal.WriteIntPtr(block, 3 * IntPtr.Size, descriptor);
        return block;
    }

    // The next post reads the settings, which hold the answer.
    private static void OnAuthorized(nint block, byte granted, nint error)
    {
    }

    private static void OnSettings(nint block, nint settings)
    {
        if (_reading.TryDequeue(out var read))
        {
            read.TrySetResult(new Permission(Send(settings, Sel("authorizationStatus")), Send(settings, Sel("alertSetting")), Send(settings, Sel("alertStyle"))));
        }
    }

    private static void OnAdded(nint block, nint error)
    {
        if (_adding.TryDequeue(out var added))
        {
            added.TrySetResult(error == 0);
        }
    }

    private static void OnResponse(nint self, nint selector, nint center, nint response, nint completion)
    {
        try
        {
            var request = Send(Send(response, Sel("notification")), Sel("request"));

            if (_live.TryGetValue(Read(Send(request, Sel("identifier"))), out var pressed))
            {
                pressed();
            }
        }
        catch (Exception)
        {
            // Nothing managed may unwind into Objective-C, and a press that fails has only failed to bring keypaste forward.
        }

        Marshal.GetDelegateForFunctionPointer<Completed>(Marshal.ReadIntPtr(completion, 2 * IntPtr.Size))(completion);
    }

    // The system shows nothing for an app in front unless told to, and a bridge can knock while the unlock screen is in front.
    private static void OnPresent(nint self, nint selector, nint center, nint notification, nint completion) =>
        Marshal.GetDelegateForFunctionPointer<PresentCompleted>(Marshal.ReadIntPtr(completion, 2 * IntPtr.Size))(completion, _presentAsBanner | _presentInList);

    private static nint String(string text) =>
        Send(Class("NSString"), Sel("stringWithUTF8String:"), Encoding.UTF8.GetBytes(text + '\0'));

    private static string Read(nint text) =>
        text == 0 ? string.Empty : Marshal.PtrToStringUTF8(Send(text, Sel("UTF8String"))) ?? string.Empty;

    private static nint Class(string name) => GetClass(Encoding.UTF8.GetBytes(name + '\0'));

    private static nint Sel(string name) => Selector(Encoding.UTF8.GetBytes(name + '\0'));

    [DllImport(_objc, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(byte[] name);

    [DllImport(_objc, EntryPoint = "sel_registerName")]
    private static extern nint Selector(byte[] name);

    [DllImport(_objc, EntryPoint = "objc_allocateClassPair")]
    private static extern nint AllocateClass(nint superclass, byte[] name, nuint extraBytes);

    [DllImport(_objc, EntryPoint = "objc_registerClassPair")]
    private static extern void RegisterClass(nint made);

    [DllImport(_objc, EntryPoint = "class_addMethod")]
    private static extern byte AddMethod(nint made, nint selector, nint implementation, byte[] types);

    [DllImport(_objc, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern nint PoolPush();

    [DllImport(_objc, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void PoolPop(nint pool);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern byte SendForByte(nint receiver, nint selector, nint argument);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nuint argument);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, byte[] argument);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nuint first, nint second);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second, nint third);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second, nuint third);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second, nint third, nuint fourth);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Authorized(nint block, byte granted, nint error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SettingsRead(nint block, nint settings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Added(nint block, nint error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DelegateMethod(nint self, nint selector, nint center, nint argument, nint completion);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Completed(nint block);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PresentCompleted(nint block, nuint options);

    /// <summary>The system's record of the person's choice.</summary>
    private readonly record struct Permission(nint Authorization, nint AlertSetting, nint AlertStyle)
    {
        /// <summary>Whether a notice would reach the screen: "None" in the system's settings keeps it from there with permission still granted.</summary>
        internal bool ShowsAlerts => Authorization > _authorizationDenied && AlertSetting == _settingEnabled && AlertStyle != _alertStyleNone;
    }

    private sealed class Posted(string id) : IPostedNotice
    {
        public void Remove()
        {
            _live.TryRemove(id, out _);

            var pool = PoolPush();
            var ids = Send(Class("NSArray"), Sel("arrayWithObject:"), String(id));
            _ = Send(Center(), Sel("removeDeliveredNotificationsWithIdentifiers:"), ids);
            _ = Send(Center(), Sel("removePendingNotificationRequestsWithIdentifiers:"), ids);
            PoolPop(pool);
        }
    }
}
