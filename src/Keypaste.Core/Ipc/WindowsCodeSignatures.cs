using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>A program's Authenticode signature, through <c>WinVerifyTrust</c>, and its signer's subject.</summary>
/// <remarks>
/// <para>
/// Revocation is not checked and nothing is fetched, so the check stays local. A program Windows signs only
/// through a catalog, as many of its own are, reads as unsigned.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every
/// signature is blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCodeSignatures : ICodeSignatures
{
    private const int _noSignature = unchecked((int)0x800B0100);
    private const uint _noInterface = 2;
    private const uint _noRevocationCheck = 0;
    private const uint _file = 1;
    private const uint _verify = 1;
    private const uint _close = 2;
    private const uint _cacheOnlyRetrieval = 0x1000;

    private static readonly Guid _genericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <inheritdoc/>
    public (CallerSignature State, string? Signer) Check(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var path = Marshal.StringToHGlobalUni(executable);
        var file = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());

        try
        {
            Marshal.StructureToPtr(new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path }, file, fDeleteOld: false);

            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = _noInterface,
                RevocationChecks = _noRevocationCheck,
                UnionChoice = _file,
                File = file,
                StateAction = _verify,
                ProviderFlags = _cacheOnlyRetrieval,
            };

            var action = _genericVerifyV2;
            var status = WinVerifyTrust(-1, ref action, ref data);

            data.StateAction = _close;
            _ = WinVerifyTrust(-1, ref action, ref data);

            return status switch
            {
                0 => (CallerSignature.Valid, SignerOf(executable)),
                _noSignature => (CallerSignature.NotSigned, null),
                _ => (CallerSignature.Invalid, null),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(path);
        }
    }

    private static string? SignerOf(string executable)
    {
        try
        {
            // X509CertificateLoader reads no Authenticode signature, so this is the platform's one managed reader of a signer.
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(executable);
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustFile
    {
        public uint Size;
        public nint Path;
        public nint Handle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
