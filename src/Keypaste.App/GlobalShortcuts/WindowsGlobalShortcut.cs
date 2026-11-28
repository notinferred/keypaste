using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.App.GlobalShortcuts;

/// <summary>A system-wide chord through Windows's <c>RegisterHotKey</c>, on a message-only window of its own.</summary>
/// <remarks>
/// The window is made on the UI thread, whose message loop Avalonia runs, so <c>WM_HOTKEY</c> reaches it there. Windows
/// refuses a chord another program registered, and lets the program a hot key reaches take the foreground.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalShortcut : IGlobalShortcut
{
    private const int _id = 1;
    private const uint _hotKeyMessage = 0x0312;
    private const uint _alt = 0x1;
    private const uint _control = 0x2;
    private const uint _shift = 0x4;
    private const uint _noRepeat = 0x4000;
    private const uint _keyK = 0x4B;
    private const uint _keySpace = 0x20;
    private static readonly IntPtr _messageOnly = new(-3);
    private static int _classes;

    // Held here so the collector leaves it alone while Windows holds its function pointer.
    private readonly WindowProcedure _procedure;
    private IntPtr _className;
    private IntPtr _window;
    private bool _registered;

    internal WindowsGlobalShortcut() => _procedure = OnMessage;

    public string? Unavailable => null;

    /// <summary>The message-only window the hot key is registered to, once one has been.</summary>
    internal IntPtr Window => _window;

    public event EventHandler? Pressed;

    public bool TryRegister(GlobalChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);

        Unregister();

        if (_window == IntPtr.Zero && !TryMakeWindow())
        {
            return false;
        }

        _registered = RegisterHotKey(_window, _id, Modifiers(chord.Modifiers) | _noRepeat, chord.Key == ChordKey.K ? _keyK : _keySpace);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            _ = UnregisterHotKey(_window, _id);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();

        if (_window != IntPtr.Zero)
        {
            _ = DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_className != IntPtr.Zero)
        {
            _ = UnregisterClassW(_className, GetModuleHandleW(IntPtr.Zero));
            Marshal.FreeHGlobal(_className);
            _className = IntPtr.Zero;
        }
    }

    // Each instance has a class of its own, since a class's window procedure is fixed when it is registered.
    private bool TryMakeWindow()
    {
        var module = GetModuleHandleW(IntPtr.Zero);
        _className = Marshal.StringToHGlobalUni(string.Create(CultureInfo.InvariantCulture, $"keypaste.shortcut.{Environment.ProcessId}.{Interlocked.Increment(ref _classes)}"));

        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = module,
            ClassName = _className,
        };

        if (RegisterClassExW(ref windowClass) != 0)
        {
            _window = CreateWindowExW(0, _className, IntPtr.Zero, 0, 0, 0, 0, 0, _messageOnly, IntPtr.Zero, module, IntPtr.Zero);

            if (_window != IntPtr.Zero)
            {
                return true;
            }

            _ = UnregisterClassW(_className, module);
        }

        Marshal.FreeHGlobal(_className);
        _className = IntPtr.Zero;
        return false;
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _hotKeyMessage && wParam == _id)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private static uint Modifiers(ChordModifiers modifiers) =>
        (modifiers.HasFlag(ChordModifiers.Alt) ? _alt : 0)
        | (modifiers.HasFlag(ChordModifiers.Control) ? _control : 0)
        | (modifiers.HasFlag(ChordModifiers.Shift) ? _shift : 0);

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(IntPtr className, IntPtr instance);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle, IntPtr className, IntPtr windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(IntPtr moduleName);
}
