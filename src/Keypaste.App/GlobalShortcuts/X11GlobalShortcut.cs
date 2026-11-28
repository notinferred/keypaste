using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Keypaste.App.GlobalShortcuts;

/// <summary>A system-wide chord through X11's <c>XGrabKey</c> on the root window, over a display connection of its own.</summary>
/// <remarks>
/// <para>
/// Avalonia's connection is its own, so this one is opened beside it and read on a thread of its own, which alone
/// calls Xlib on it: the UI thread hands it each grab and waits for the answer, and a pipe wakes it.
/// </para>
/// <para>
/// X answers a grab another client holds with BadAccess, after the call, so the error handler is this connection's
/// only while it grabs. A grab matches exact modifiers, so the chord is grabbed with Caps Lock and Num Lock on and off.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class X11GlobalShortcut : IGlobalShortcut
{
    private const string _x11 = "libX11.so.6";
    private const int _keyPress = 2;
    private const int _badAccess = 10;
    private const uint _shiftMask = 1;
    private const uint _lockMask = 2;
    private const uint _controlMask = 4;
    private const uint _mod1Mask = 8;
    private const uint _mod2Mask = 16;
    private const int _grabModeAsync = 1;
    private const ulong _keysymK = 0x6B;
    private const ulong _keysymSpace = 0x20;
    private const short _pollIn = 1;
    private const int _eventBytes = 192;
    private static readonly uint[] _lockStates = [0, _lockMask, _mod2Mask, _lockMask | _mod2Mask];
    private static readonly TimeSpan _answerBound = TimeSpan.FromSeconds(2);

    private readonly IntPtr _display;
    private readonly IntPtr _root;
    private readonly int[] _wake;
    private readonly Thread _reader;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _answered = new(0);

    // Held here so the collector leaves it alone while Xlib holds its function pointer.
    private readonly ErrorHandler _onError;
    private Func<bool>? _asked;
    private bool _answer;
    private bool _refused;
    private (uint KeyCode, uint Modifiers)? _grabbed;
    private volatile bool _stopping;

    private X11GlobalShortcut(IntPtr display, int[] wake)
    {
        _display = display;
        _wake = wake;
        _root = XDefaultRootWindow(display);
        _onError = OnError;
        _reader = new Thread(Read) { IsBackground = true, Name = "keypaste shortcut" };
        _reader.Start();
    }

    /// <summary>Opens the session's display, or says none opens.</summary>
    internal static IGlobalShortcut Open()
    {
        IntPtr display;

        try
        {
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return new NoGlobalShortcut(PlatformShortcut.NoDisplayReason);
        }

        if (display == IntPtr.Zero)
        {
            return new NoGlobalShortcut(PlatformShortcut.NoDisplayReason);
        }

        var wake = new int[2];

        if (Pipe(wake) != 0)
        {
            _ = XCloseDisplay(display);
            return new NoGlobalShortcut(PlatformShortcut.NoDisplayReason);
        }

        return new X11GlobalShortcut(display, wake);
    }

    public string? Unavailable => null;

    public event EventHandler? Pressed;

    public bool TryRegister(GlobalChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);

        return OnReader(() =>
        {
            Ungrab();

            var keyCode = XKeysymToKeycode(_display, chord.Key == ChordKey.K ? _keysymK : _keysymSpace);

            if (keyCode == 0)
            {
                return false;
            }

            var modifiers = Modifiers(chord.Modifiers);
            var before = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(_onError));
            _refused = false;

            foreach (var locks in _lockStates)
            {
                _ = XGrabKey(_display, keyCode, modifiers | locks, _root, 0, _grabModeAsync, _grabModeAsync);
            }

            _ = XSync(_display, 0);
            _ = XSetErrorHandler(before);
            _grabbed = (keyCode, modifiers);

            if (_refused)
            {
                Ungrab();
                return false;
            }

            return true;
        });
    }

    public void Unregister() => _ = OnReader(() =>
    {
        Ungrab();
        return true;
    });

    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        Unregister();
        _stopping = true;
        Wake();
        _ = _reader.Join(_answerBound);
        _ = Close(_wake[0]);
        _ = Close(_wake[1]);
        _ = XCloseDisplay(_display);
        _answered.Dispose();
    }

    private void Ungrab()
    {
        if (_grabbed is { } grabbed)
        {
            foreach (var locks in _lockStates)
            {
                _ = XUngrabKey(_display, grabbed.KeyCode, grabbed.Modifiers | locks, _root);
            }

            _ = XSync(_display, 0);
            _grabbed = null;
        }
    }

    // An error on Avalonia's own connection in the moment this one grabs is dropped, not handed on to Avalonia's handler.
    private int OnError(IntPtr display, IntPtr error)
    {
        if (display == _display && Marshal.ReadByte(error, ErrorCodeOffset) == _badAccess)
        {
            _refused = true;
        }

        return 0;
    }

    // XErrorEvent: int type, Display*, XID resourceid, unsigned long serial, then the error code.
    private static int ErrorCodeOffset => IntPtr.Size == 8 ? 32 : 16;

    private bool OnReader(Func<bool> act)
    {
        if (_stopping)
        {
            return false;
        }

        lock (_gate)
        {
            // An answer that came after an earlier ask gave up is that ask's.
            while (_answered.Wait(0))
            {
            }

            _asked = act;
            Wake();
            return _answered.Wait(_answerBound) && _answer;
        }
    }

    private void Wake() => _ = WriteTo(_wake[1], [1], 1);

    private void Read()
    {
        var polled = new PollFd[] { new(XConnectionNumber(_display), _pollIn, 0), new(_wake[0], _pollIn, 0) };
        var woken = new byte[64];
        var buffer = Marshal.AllocHGlobal(_eventBytes);

        try
        {
            while (!_stopping)
            {
                polled[0].Returned = 0;
                polled[1].Returned = 0;

                if (Poll(polled, 2, -1) < 0)
                {
                    continue;
                }

                if (polled[1].Returned != 0)
                {
                    _ = ReadFrom(_wake[0], woken, woken.Length);

                    if (Interlocked.Exchange(ref _asked, null) is { } asked)
                    {
                        _answer = asked();
                        _answered.Release();
                    }
                }

                while (!_stopping && XPending(_display) > 0)
                {
                    _ = XNextEvent(_display, buffer);

                    if (Marshal.ReadInt32(buffer) == _keyPress)
                    {
                        Dispatcher.UIThread.Post(() => Pressed?.Invoke(this, EventArgs.Empty));
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint Modifiers(ChordModifiers modifiers) =>
        (modifiers.HasFlag(ChordModifiers.Shift) ? _shiftMask : 0)
        | (modifiers.HasFlag(ChordModifiers.Control) ? _controlMask : 0)
        | (modifiers.HasFlag(ChordModifiers.Alt) ? _mod1Mask : 0);

    private delegate int ErrorHandler(IntPtr display, IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd(int descriptor, short events, short returned)
    {
        public int Descriptor = descriptor;
        public short Events = events;
        public short Returned = returned;
    }

    [DllImport(_x11)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(_x11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(_x11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(_x11)]
    private static extern int XConnectionNumber(IntPtr display);

    [DllImport(_x11)]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport(_x11)]
    private static extern int XGrabKey(IntPtr display, uint keyCode, uint modifiers, IntPtr window, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(_x11)]
    private static extern int XUngrabKey(IntPtr display, uint keyCode, uint modifiers, IntPtr window);

    [DllImport(_x11)]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport(_x11)]
    private static extern int XPending(IntPtr display);

    [DllImport(_x11)]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(_x11)]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int Pipe(int[] descriptors);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll([In, Out] PollFd[] descriptors, ulong count, int timeout);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint ReadFrom(int descriptor, byte[] buffer, nint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint WriteTo(int descriptor, byte[] buffer, nint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}
