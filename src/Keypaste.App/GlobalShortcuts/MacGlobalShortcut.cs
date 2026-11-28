using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Keypaste.App.GlobalShortcuts;

/// <summary>A system-wide chord through Carbon's <c>RegisterEventHotKey</c>, which Avalonia offers no call for.</summary>
/// <remarks>
/// A hot key is the one way macOS gives an app a chord pressed in another app without the Accessibility permission,
/// since the system delivers only that chord and the app reads no other key. It is registered exclusively, so a chord
/// another app holds is refused rather than shared, and the system delivers its press on the main thread.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacGlobalShortcut : IGlobalShortcut
{
    private const string _carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint _keyboardClass = 0x6B657962; // 'keyb'
    private const uint _hotKeyPressed = 5;
    private const uint _exclusive = 1;
    private const uint _signature = 0x6B707374; // 'kpst'
    private const uint _command = 1 << 8;
    private const uint _shift = 1 << 9;
    private const uint _option = 1 << 11;
    private const uint _control = 1 << 12;
    private const uint _keyK = 0x28;
    private const uint _keySpace = 0x31;

    // Held here so the collector leaves it alone while Carbon holds its function pointer.
    private readonly EventHandlerProc _onEvent;
    private IntPtr _handler;
    private IntPtr _hotKey;

    internal MacGlobalShortcut() => _onEvent = OnEvent;

    public string? Unavailable => null;

    public event EventHandler? Pressed;

    public bool TryRegister(GlobalChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);

        Unregister();

        if (_handler == IntPtr.Zero
            && InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(_onEvent), 1, [new EventTypeSpec(_keyboardClass, _hotKeyPressed)], IntPtr.Zero, out _handler) != 0)
        {
            _handler = IntPtr.Zero;
            return false;
        }

        if (RegisterEventHotKey(KeyCode(chord.Key), Modifiers(chord.Modifiers), new EventHotKeyId(_signature, 1), GetApplicationEventTarget(), _exclusive, out _hotKey) != 0)
        {
            _hotKey = IntPtr.Zero;
            return false;
        }

        return true;
    }

    public void Unregister()
    {
        if (_hotKey != IntPtr.Zero)
        {
            _ = UnregisterEventHotKey(_hotKey);
            _hotKey = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Unregister();

        if (_handler != IntPtr.Zero)
        {
            _ = RemoveEventHandler(_handler);
            _handler = IntPtr.Zero;
        }
    }

    private int OnEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        Dispatcher.UIThread.Post(() => Pressed?.Invoke(this, EventArgs.Empty));
        return 0;
    }

    private static uint KeyCode(ChordKey key) => key == ChordKey.K ? _keyK : _keySpace;

    private static uint Modifiers(ChordModifiers modifiers) =>
        (modifiers.HasFlag(ChordModifiers.Command) ? _command : 0)
        | (modifiers.HasFlag(ChordModifiers.Shift) ? _shift : 0)
        | (modifiers.HasFlag(ChordModifiers.Alt) ? _option : 0)
        | (modifiers.HasFlag(ChordModifiers.Control) ? _control : 0);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct EventTypeSpec(uint EventClass, uint EventKind);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct EventHotKeyId(uint Signature, uint Id);

    private delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(_carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(_carbon)]
    private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint numTypes, EventTypeSpec[] list, IntPtr userData, out IntPtr handlerRef);

    [DllImport(_carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(_carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKeyRef);

    [DllImport(_carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
