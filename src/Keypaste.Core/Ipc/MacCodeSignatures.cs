using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>A running program's code signature, through Security.framework's dynamic code checks, and its team ID.</summary>
/// <remarks>
/// <para>
/// The process is found by its audit token, and its check covers the code the system holds it running under as
/// well as its file. Without a requirement, a valid signature says only that the code is intact under it; the team
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

    private static readonly Lazy<(nint Flags, nint Team, nint Audit, nint KeyCallBacks, nint ValueCallBacks)> _symbols = new(() =>
    {
        var security = NativeLibrary.Load(_security);
        var coreFoundation = NativeLibrary.Load(_coreFoundation);
        return (
            Marshal.ReadIntPtr(NativeLibrary.GetExport(security, "kSecCodeInfoFlags")),
            Marshal.ReadIntPtr(NativeLibrary.GetExport(security, "kSecCodeInfoTeamIdentifier")),
            Marshal.ReadIntPtr(NativeLibrary.GetExport(security, "kSecGuestAttributeAudit")),
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"));
    });

    /// <inheritdoc/>
    public (CallerSignature State, string? Signer) Check(string executable, MacAuditToken? running)
    {
        if (running is not { } token)
        {
            return (CallerSignature.Unknown, null);
        }

        var symbols = _symbols.Value;
        var audit = CFDataCreate(0, ref token, Marshal.SizeOf<MacAuditToken>());
        var attributes = audit == 0 ? 0 : CFDictionaryCreate(0, [symbols.Audit], [audit], 1, symbols.KeyCallBacks, symbols.ValueCallBacks);
        nint code = 0;
        nint information = 0;

        try
        {
            if (attributes == 0 || SecCodeCopyGuestWithAttributes(0, attributes, 0, out code) != 0)
            {
                return (CallerSignature.Unknown, null);
            }

            var status = SecCodeCheckValidity(code, 0, 0);

            if (status != 0)
            {
                return (status == _unsigned ? CallerSignature.NotSigned : CallerSignature.Invalid, null);
            }

            if (SecCodeCopySigningInformation(code, _signingInformation, out information) != 0 || information == 0)
            {
                return (CallerSignature.Valid, null);
            }

            var flags = CFDictionaryGetValue(information, symbols.Flags);

            if (flags != 0 && CFNumberGetValue(flags, _sInt32, out var value) && (value & _adHocFlag) != 0)
            {
                return (CallerSignature.AdHoc, null);
            }

            return (CallerSignature.Valid, TextOf(CFDictionaryGetValue(information, symbols.Team)));
        }
        finally
        {
            Release(information);
            Release(code);
            Release(attributes);
            Release(audit);
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
    private static extern nint CFDataCreate(nint allocator, ref MacAuditToken bytes, nint length);

    [DllImport(_coreFoundation)]
    private static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallBacks, nint valueCallBacks);

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
    private static extern int SecCodeCopyGuestWithAttributes(nint host, nint attributes, uint flags, out nint guest);

    [DllImport(_security)]
    private static extern int SecCodeCheckValidity(nint code, uint flags, nint requirement);

    [DllImport(_security)]
    private static extern int SecCodeCopySigningInformation(nint code, uint flags, out nint information);
}
