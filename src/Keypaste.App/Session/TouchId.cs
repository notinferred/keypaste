using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Keypaste.App.Session;

/// <summary>
/// Touch ID through the data protection keychain, as KeePassXC's quick unlock reaches it on macOS: each copy's key is
/// 32 random bytes in a keychain item that only a fingerprint enrolled when it was made releases, kept on this Mac
/// alone and never synced (D-0681, D-0682).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a signed app reaches it.</b> The data protection keychain admits a program only under the
/// <c>keychain-access-groups</c> entitlement a provisioning profile authorizes (Apple's TN3137), which only the Developer
/// ID build carries; any other build reads as having no Touch ID.
/// </para>
/// <para>
/// <b>Making a key asks.</b> An item is stored without asking, so a new key is read back at once, which asks the person
/// as Windows Hello asks on every keep (D-0683); a cancelled read deletes it again.
/// </para>
/// <para>
/// <b>Each home has its own service.</b> The app deletes every key under its home's service when it starts, since a key
/// left by a run that ended without forgetting it opens nothing: its copy lived only in that run's memory.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class TouchId : IQuickUnlockKeys
{
    /// <summary>A keychain call's status: the call succeeded.</summary>
    internal const int Succeeded = 0;

    /// <summary>The person cancelled the prompt.</summary>
    internal const int UserCanceled = -128;

    /// <summary>No item matched, such as one the system removed when the enrolled fingerprints changed.</summary>
    internal const int ItemNotFound = -25300;

    /// <summary>An item with that service and account exists already.</summary>
    internal const int DuplicateItem = -25299;

    /// <summary>The keychain may not ask the person now.</summary>
    internal const int InteractionNotAllowed = -25308;

    /// <summary>No keychain is available, such as outside a person's login session.</summary>
    internal const int NotAvailable = -25291;

    /// <summary>The program lacks the entitlement the data protection keychain admits it by.</summary>
    internal const int MissingEntitlement = -34018;

    private const string _security = "/System/Library/Frameworks/Security.framework/Security";
    private const string _coreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string _localAuthentication = "/System/Library/Frameworks/LocalAuthentication.framework/LocalAuthentication";
    private const string _objc = "/usr/lib/libobjc.A.dylib";
    private const string _servicePrefix = "com.keypaste.app.quick-unlock.";
    private const string _probeAccount = "probe";
    private const string _makeReason = "let Touch ID unlock this vault after a lock";
    private const string _answerReason = "unlock your vault";
    private const int _keyLength = 32;
    private const nint _biometricsPolicy = 1;
    private const nuint _biometryCurrentSet = 1 << 3;
    private const uint _utf8 = 0x08000100;

    private readonly string _service;
    private readonly Action<Exception, string>? _unexpected;
    private readonly Lazy<bool> _admitted;

    /// <summary>Touch ID for the app under <paramref name="home"/>, deleting what an earlier run under it left.</summary>
    /// <param name="home">The keypaste home.</param>
    /// <param name="unexpected">Keeps a report of a status no outcome names, or of a failure to reach the system, at <see cref="CrashReporting.TouchIdSite"/>.</param>
    internal TouchId(string home, Action<Exception, string>? unexpected = null)
    {
        ArgumentNullException.ThrowIfNull(home);
        _service = ServiceFor(home);
        _unexpected = unexpected;
        _admitted = new Lazy<bool>(() => Probe() == Succeeded);
        Call(() => Delete(account: null));
    }

    public string Name => MachineQuickUnlock.TouchIdName;

    /// <summary>The keychain service an app under <paramref name="home"/> keeps its keys under.</summary>
    internal static string ServiceFor(string home) =>
        _servicePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)))))[..16];

    /// <summary>What a keychain call's <paramref name="status"/> comes to as an outcome; null for one no outcome names.</summary>
    internal static KeyOutcome? Outcome(int status) => status switch
    {
        Succeeded => KeyOutcome.Released,
        UserCanceled => KeyOutcome.Cancelled,
        ItemNotFound => KeyOutcome.Gone,
        MissingEntitlement or InteractionNotAllowed or NotAvailable => KeyOutcome.Unavailable,
        _ => null,
    };

    /// <summary>What storing a plain item answers, deleted again at once, which asks the person nothing.</summary>
    /// <returns><see cref="Succeeded"/> where the keychain admits this program, <see cref="MissingEntitlement"/> where it does not.</returns>
    /// <remarks>A lookup proves nothing: the keychain answers one from a program it would not let store an item.</remarks>
    internal int Probe() => Call(
        () =>
        {
            Delete(_probeAccount);
            var stored = AddPlain(_probeAccount);
            Delete(_probeAccount);
            return stored;
        },
        fallback: MissingEntitlement);

    // Touch ID set up on this Mac, and a keychain that admits this build.
    public bool IsAvailable() => Call(CanEvaluateBiometrics, fallback: false) && _admitted.Value;

    public KeyOutcome Make(byte[] challenge, out byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        key = null;
        var account = Account(challenge);
        var made = RandomNumberGenerator.GetBytes(_keyLength);

        try
        {
            var added = Call(() => Add(account, made), fallback: NotAvailable);

            if (added == DuplicateItem)
            {
                Call(() => Delete(account));
                added = Call(() => Add(account, made), fallback: NotAvailable);
            }

            if (added != Succeeded)
            {
                return Report(added);
            }

            var answered = Read(account, _makeReason, out var read);

            if (answered != KeyOutcome.Released || read is null || !CryptographicOperations.FixedTimeEquals(read, made))
            {
                CryptographicOperations.ZeroMemory(read);
                Call(() => Delete(account));
                return answered == KeyOutcome.Released ? KeyOutcome.Failed : answered;
            }

            key = read;
            return KeyOutcome.Released;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(made);
        }
    }

    public KeyOutcome Answer(byte[] challenge, out byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Read(Account(challenge), _answerReason, out key);
    }

    public void Forget(byte[] challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var account = Account(challenge);
        Call(() => Delete(account));
    }

    private KeyOutcome Read(string account, string reason, out byte[]? key)
    {
        byte[]? read = null;
        var status = Call(() => CopyData(account, reason, out read), fallback: NotAvailable);
        key = null;

        if (status != Succeeded)
        {
            CryptographicOperations.ZeroMemory(read);
            return Report(status);
        }

        if (read is not { Length: _keyLength })
        {
            CryptographicOperations.ZeroMemory(read);
            return KeyOutcome.Failed;
        }

        key = read;
        return KeyOutcome.Released;
    }

    // A status no outcome names is reported by its number alone, which says what failed and holds nothing of the vault's.
    private KeyOutcome Report(int status)
    {
        if (Outcome(status) is { } known)
        {
            return known;
        }

        _unexpected?.Invoke(new InvalidOperationException("A keychain call failed.") { HResult = status }, CrashReporting.TouchIdSite);
        return KeyOutcome.Failed;
    }

    private void Call(Action call) => Call(() => { call(); return 0; }, fallback: 0);

    // Any failure to reach the system's frameworks is Touch ID that is not there.
    private T Call<T>(Func<T> call, T fallback)
    {
        var pool = AutoreleasePoolPush();

        try
        {
            return call();
        }
        catch (Exception ex)
        {
            _unexpected?.Invoke(ex, CrashReporting.TouchIdSite);
            return fallback;
        }
        finally
        {
            AutoreleasePoolPop(pool);
        }
    }

    private static string Account(byte[] challenge) => Convert.ToHexStringLower(SHA256.HashData(challenge));

    private static bool CanEvaluateBiometrics()
    {
        var context = NewContext(reason: null);

        try
        {
            return SendForBool(context, Sel("canEvaluatePolicy:error:"), _biometricsPolicy, 0) != 0;
        }
        finally
        {
            _ = Send(context, Sel("release"));
        }
    }

    private int Add(string account, byte[] key)
    {
        var access = SecAccessControlCreateWithFlags(0, Constant(_security, "kSecAttrAccessibleWhenUnlockedThisDeviceOnly"), _biometryCurrentSet, out var error);

        if (access == 0)
        {
            ReleaseIfAny(error);
            return NotAvailable;
        }

        var pinned = GCHandle.Alloc(key, GCHandleType.Pinned);

        try
        {
            using var item = Query(account);
            var data = item.Own(CFDataCreateWithBytesNoCopy(0, pinned.AddrOfPinnedObject(), key.Length, Constant(_coreFoundation, "kCFAllocatorNull")));
            item.Set("kSecValueData", data);
            item.Set("kSecAttrSynchronizable", Constant(_coreFoundation, "kCFBooleanFalse"));
            item.Set("kSecAttrAccessControl", item.Own(access));
            return SecItemAdd(item.Handle, 0);
        }
        finally
        {
            pinned.Free();
        }
    }

    private int AddPlain(string account)
    {
        var probe = new byte[1];
        var pinned = GCHandle.Alloc(probe, GCHandleType.Pinned);

        try
        {
            using var item = Query(account);
            item.Set("kSecValueData", item.Own(CFDataCreateWithBytesNoCopy(0, pinned.AddrOfPinnedObject(), probe.Length, Constant(_coreFoundation, "kCFAllocatorNull"))));
            item.Set("kSecAttrSynchronizable", Constant(_coreFoundation, "kCFBooleanFalse"));
            item.Set("kSecAttrAccessible", Constant(_security, "kSecAttrAccessibleWhenUnlockedThisDeviceOnly"));
            return SecItemAdd(item.Handle, 0);
        }
        finally
        {
            pinned.Free();
        }
    }

    private int CopyData(string account, string reason, out byte[]? data)
    {
        data = null;
        var context = NewContext(reason);

        try
        {
            using var query = Query(account);

            query.Set("kSecReturnData", Constant(_coreFoundation, "kCFBooleanTrue"));

            query.Set("kSecUseAuthenticationContext", context);
            var status = SecItemCopyMatching(query.Handle, out var result);

            if (status == Succeeded && result != 0)
            {
                try
                {
                    var length = (int)CFDataGetLength(result);
                    data = new byte[length];
                    Marshal.Copy(CFDataGetBytePtr(result), data, 0, length);
                }
                finally
                {
                    CFRelease(result);
                }
            }

            return status;
        }
        finally
        {
            _ = Send(context, Sel("release"));
        }
    }

    private void Delete(string? account)
    {
        using var query = Query(account);
        _ = SecItemDelete(query.Handle);
    }

    // Every query names the data protection keychain, its generic password class and this home's service.
    private KeychainQuery Query(string? account)
    {
        var query = new KeychainQuery();
        query.Set("kSecClass", Constant(_security, "kSecClassGenericPassword"));
        query.Set("kSecUseDataProtectionKeychain", Constant(_coreFoundation, "kCFBooleanTrue"));
        query.Set("kSecAttrService", query.Own(CFString(_service)));

        if (account is not null)
        {
            query.Set("kSecAttrAccount", query.Own(CFString(account)));
        }

        return query;
    }

    // An LAContext, which the caller sends release; a reason completes the system's "keypaste is trying to …".
    private static nint NewContext(string? reason)
    {
        _ = NativeLibrary.Load(_localAuthentication);
        var context = Send(Send(Class("LAContext"), Sel("alloc")), Sel("init"));

        if (context == 0)
        {
            throw new InvalidOperationException("LocalAuthentication made no context.");
        }

        if (reason is not null)
        {
            var text = CFString(reason);

            try
            {
                _ = Send(context, Sel("setLocalizedReason:"), text);
            }
            finally
            {
                CFRelease(text);
            }
        }

        return context;
    }

    private static void ReleaseIfAny(nint value)
    {
        if (value != 0)
        {
            CFRelease(value);
        }
    }

    private static nint Constant(string framework, string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(framework), name));

    private static nint CFString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var made = CFStringCreateWithCString(0, bytes, _utf8);
        return made != 0 ? made : throw new InvalidOperationException("CoreFoundation made no string.");
    }

    private static nint Class(string name) => GetClass(Encoding.UTF8.GetBytes(name + "\0"));

    private static nint Sel(string name) => Selector(Encoding.UTF8.GetBytes(name + "\0"));

    /// <summary>A keychain query or item: a mutable CoreFoundation dictionary of Security's keys, releasing it and the values it was given to own.</summary>
    private sealed class KeychainQuery : IDisposable
    {
        private readonly List<nint> _owned = [];

        internal KeychainQuery()
        {
            var foundation = NativeLibrary.Load(_coreFoundation);
            Handle = CFDictionaryCreateMutable(
                0, 0, NativeLibrary.GetExport(foundation, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(foundation, "kCFTypeDictionaryValueCallBacks"));

            if (Handle == 0)
            {
                throw new InvalidOperationException("CoreFoundation made no dictionary.");
            }
        }

        internal nint Handle { get; }

        internal void Set(string securityKey, nint value) => CFDictionarySetValue(Handle, Constant(_security, securityKey), value);

        internal nint Own(nint value)
        {
            if (value == 0)
            {
                throw new InvalidOperationException("CoreFoundation made no value.");
            }

            _owned.Add(value);
            return value;
        }

        public void Dispose()
        {
            CFRelease(Handle);

            foreach (var value in _owned)
            {
                CFRelease(value);
            }
        }
    }

    [DllImport(_security)]
    private static extern nint SecAccessControlCreateWithFlags(nint allocator, nint protection, nuint flags, out nint error);

    [DllImport(_security)]
    private static extern int SecItemAdd(nint attributes, nint result);

    [DllImport(_security)]
    private static extern int SecItemCopyMatching(nint query, out nint result);

    [DllImport(_security)]
    private static extern int SecItemDelete(nint query);

    [DllImport(_coreFoundation)]
    private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallBacks, nint valueCallBacks);

    [DllImport(_coreFoundation)]
    private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);

    [DllImport(_coreFoundation)]
    private static extern nint CFDataCreateWithBytesNoCopy(nint allocator, nint bytes, nint length, nint bytesDeallocator);

    [DllImport(_coreFoundation)]
    private static extern nint CFDataGetLength(nint data);

    [DllImport(_coreFoundation)]
    private static extern nint CFDataGetBytePtr(nint data);

    [DllImport(_coreFoundation)]
    private static extern nint CFStringCreateWithCString(nint allocator, byte[] text, uint encoding);

    [DllImport(_coreFoundation)]
    private static extern void CFRelease(nint value);

    [DllImport(_objc, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(byte[] name);

    [DllImport(_objc, EntryPoint = "sel_registerName")]
    private static extern nint Selector(byte[] name);

    [DllImport(_objc, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern nint AutoreleasePoolPush();

    [DllImport(_objc, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void AutoreleasePoolPop(nint pool);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(_objc, EntryPoint = "objc_msgSend")]
    private static extern byte SendForBool(nint receiver, nint selector, nint policy, nint error);
}
