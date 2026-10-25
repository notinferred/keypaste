#if WINDOWS
using System.Runtime.InteropServices;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
#endif

namespace Keypaste.App.Session;

/// <summary>What a quick unlock asks on this machine.</summary>
internal static class QuickUnlockSigners
{
    /// <summary>Windows Hello in the app's Windows build on Windows 10 2004 or later, and nothing elsewhere (D-0549).</summary>
    internal static IQuickUnlockSigner? ForThisMachine()
    {
#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return new WindowsHello();
        }
#endif
        return null;
    }
}

#if WINDOWS
/// <summary>
/// Windows Hello through WinRT's <c>KeyCredentialManager</c>, as KeePassXC 2.8's quick unlock reaches it: one key of
/// keypaste's own, made on first use, signs a challenge once the person has shown Windows Hello it is them (D-0549).
/// </summary>
/// <remarks>
/// <para>
/// The key never leaves Windows, which keeps it in the TPM where there is one, and it signs with RSA PKCS #1 v1.5,
/// so one challenge always gives one signature.
/// </para>
/// <para>
/// <b>The prompt is brought forward.</b> Windows can open it behind the app's window, so after asking keypaste looks
/// for its window every half second, four times, and puts it in front, as KeePassXC's <c>queueSecurityPromptFocus</c> does.
/// </para>
/// </remarks>
internal sealed class WindowsHello : IQuickUnlockSigner
{
    private const string _keyName = "keypaste";
    private const string _promptWindowClass = "Credential Dialog Xaml Host";
    private const int _promptChecks = 4;

    private static readonly TimeSpan _promptCheck = TimeSpan.FromMilliseconds(500);

    public string Name => "Windows Hello";

    // Any failure to reach WinRT, such as a Windows edition without it, is Windows Hello that is not there.
    public bool IsAvailable()
    {
        try
        {
            return KeyCredentialManager.IsSupportedAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public SignOutcome Sign(byte[] challenge, out byte[]? signature)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        signature = null;

        BringPromptForward();

        try
        {
            if (OpenOrCreate(out var refused) is not { } credential)
            {
                return refused;
            }

            var signed = credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge)).AsTask().GetAwaiter().GetResult();

            if (signed.Status != KeyCredentialStatus.Success)
            {
                return Outcome(signed.Status);
            }

            CryptographicBuffer.CopyToByteArray(signed.Result, out signature);
            return SignOutcome.Signed;
        }
        catch (Exception)
        {
            return SignOutcome.Failed;
        }
    }

    /// <summary>Opens keypaste's key, or makes it when there is none, which asks the person too.</summary>
    private static KeyCredential? OpenOrCreate(out SignOutcome refused)
    {
        refused = SignOutcome.Failed;
        var opened = KeyCredentialManager.OpenAsync(_keyName).AsTask().GetAwaiter().GetResult();

        if (opened.Status == KeyCredentialStatus.NotFound)
        {
            opened = KeyCredentialManager.RequestCreateAsync(_keyName, KeyCredentialCreationOption.FailIfExists).AsTask().GetAwaiter().GetResult();

            if (opened.Status == KeyCredentialStatus.CredentialAlreadyExists)
            {
                opened = KeyCredentialManager.OpenAsync(_keyName).AsTask().GetAwaiter().GetResult();
            }
        }

        if (opened.Status != KeyCredentialStatus.Success)
        {
            refused = Outcome(opened.Status);
            return null;
        }

        return opened.Credential;
    }

    private static SignOutcome Outcome(KeyCredentialStatus status) => status switch
    {
        KeyCredentialStatus.Success => SignOutcome.Signed,
        KeyCredentialStatus.UserCanceled or KeyCredentialStatus.UserPrefersPassword => SignOutcome.Cancelled,
        KeyCredentialStatus.NotFound => SignOutcome.Unavailable,
        _ => SignOutcome.Failed,
    };

    private static void BringPromptForward() => _ = Task.Run(async () =>
    {
        for (var check = 0; check < _promptChecks; check++)
        {
            await Task.Delay(_promptCheck).ConfigureAwait(false);

            var prompt = FindWindow(_promptWindowClass, null);

            if (prompt != 0)
            {
                _ = SetForegroundWindow(prompt);
                return;
            }
        }
    });

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
#endif
