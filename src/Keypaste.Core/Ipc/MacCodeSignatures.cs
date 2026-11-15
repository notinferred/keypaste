using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>A program's code signature on disk, through Security.framework's static code checks, and its team ID.</summary>
/// <remarks>
/// <para>
/// The program as its file stands, not as the process runs: a process that changed its code after it started
/// is not seen. Without a requirement, a valid signature says only that the code is intact under it; the team
/// ID says whose it is, and an ad hoc signature names nobody.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every
/// signature is blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacCodeSignatures : ICodeSignatures
{
    private const string _security = "/System/Library/Frameworks/Security.framework/Security";
    private const string _coreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int _unsigned = -67062;
    private const uint _signingInformation = 1 << 1;
    private const int _adHocFlag = 0x2;
    private const int _sInt32 = 3;
    private const uint _utf8 = 0x08000100;
    private const int _teamBytes = 256;

    private static readonly Lazy<(nint Flags, nint Team)> _keys = new(() =>
    {
        var security = NativeLibrary.Load(_security);
        return (Marshal.ReadIntPtr(NativeLibrary.GetExport(security, "kSecCodeInfoFlags")), Marshal.ReadIntPtr(NativeLibrary.GetExport(security, "kSecCodeInfoTeamIdentifier")));
    });

    /// <inheritdoc/>
    public (CallerSignature State, string? Signer) Check(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var bytes = System.Text.Encoding.UTF8.GetBytes(executable);
        var url = CFURLCreateFromFileSystemRepresentation(0, bytes, bytes.Length, isDirectory: false);
        nint code = 0;
        nint information = 0;

        try
        {
            if (url == 0 || SecStaticCodeCreateWithPath(url, 0, out code) != 0)
            {
                return (CallerSignature.Unknown, null);
            }

            var status = SecStaticCodeCheckValidity(code, 0, 0);

            if (status != 0)
            {
                return (status == _unsigned ? CallerSignature.NotSigned : CallerSignature.Invalid, null);
            }

            if (SecCodeCopySigningInformation(code, _signingInformation, out information) != 0 || information == 0)
            {
                return (CallerSignature.Valid, null);
            }

            var (flagsKey, teamKey) = _keys.Value;
            var flags = CFDictionaryGetValue(information, flagsKey);

            if (flags != 0 && CFNumberGetValue(flags, _sInt32, out var value) && (value & _adHocFlag) != 0)
            {
                return (CallerSignature.AdHoc, null);
            }

            return (CallerSignature.Valid, TextOf(CFDictionaryGetValue(information, teamKey)));
        }
        finally
        {
            Release(information);
            Release(code);
            Release(url);
        }
    }

    private static string? TextOf(nint text)
    {
        var buffer = new byte[_teamBytes];

        return text != 0 && CFStringGetCString(text, buffer, buffer.Length, _utf8)
            ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
            : null;
    }

    private static void Release(nint reference)
    {
        if (reference != 0)
        {
            CFRelease(reference);
        }
    }

    [DllImport(_coreFoundation)]
    private static extern nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte[] buffer, nint length, [MarshalAs(UnmanagedType.U1)] bool isDirectory);

    [DllImport(_coreFoundation)]
    private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(_coreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(nint number, int type, out int value);

    [DllImport(_coreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(nint text, [Out] byte[] buffer, nint size, uint encoding);

    [DllImport(_coreFoundation)]
    private static extern void CFRelease(nint reference);

    [DllImport(_security)]
    private static extern int SecStaticCodeCreateWithPath(nint path, uint flags, out nint code);

    [DllImport(_security)]
    private static extern int SecStaticCodeCheckValidity(nint code, uint flags, nint requirement);

    [DllImport(_security)]
    private static extern int SecCodeCopySigningInformation(nint code, uint flags, out nint information);
}
