using Avalonia.Threading;
using Keypaste.Core.HardwareKeys;
using Microsoft.Extensions.Logging.Abstractions;
using Yubico.YubiKey;
using Yubico.YubiKey.Otp;

namespace Keypaste.App.HardwareKeys;

/// <summary>
/// YubiKeys reached through Yubico's SDK: HMAC-SHA1 challenge-response from an OTP slot over the
/// USB keyboard interface, or the smart-card one over NFC.
/// </summary>
/// <remarks>
/// <para>
/// <b>A wait on the UI thread keeps the window painting.</b> The app saves on the UI thread, and
/// every save of a vault with a hardware key asks the key, which can wait fifteen seconds for a
/// touch. The ask runs on the thread pool and the UI thread pumps its dispatcher meanwhile, so the
/// shell's "Touch your YubiKey" and its Cancel can be seen and pressed.
/// </para>
/// <para>
/// <b>A cancelled ask cannot be taken back from the key.</b> The SDK has no way to abandon one, so
/// the caller is released at once and the key times out on its own; the next ask waits for that.
/// </para>
/// </remarks>
internal sealed class YubiKeyHardware : IChallengeResponseDevice
{
    private static readonly TimeSpan _pumpAfter = TimeSpan.FromMilliseconds(150);

    private readonly Lock _gate = new();
    private Task? _inFlight;

    static YubiKeyHardware() =>
        // Before the SDK's first use: its default reads appsettings.json from the working directory
        // and logs to the console.
        Yubico.Core.Logging.Log.Instance = NullLoggerFactory.Instance;

    public IReadOnlyList<HardwareKeyInfo> Find() =>
        Reach<IReadOnlyList<HardwareKeyInfo>>(() => [.. OtpKeys().Select(Describe)]);

    public byte[] Respond(int slot, byte[] challenge, Action touchNeeded, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(touchNeeded);

        Task<byte[]> asking;
        lock (_gate)
        {
            var previous = _inFlight ?? Task.CompletedTask;
            asking = previous.ContinueWith(
                _ => Reach(() => Challenge(slot, challenge, touchNeeded)),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            _inFlight = asking;
        }

        Wait(asking, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return asking.GetAwaiter().GetResult();
    }

    private static byte[] Challenge(int slot, byte[] challenge, Action touchNeeded)
    {
        var keys = OtpKeys();

        var key = keys.Count switch
        {
            0 => throw new HardwareKeyException(HardwareKeyFailure.NotFound, "No YubiKey is plugged in."),
            > 1 => throw new HardwareKeyException(
                HardwareKeyFailure.MoreThanOne, "More than one YubiKey is plugged in. Leave only the one this vault uses."),
            _ => keys[0],
        };

        using var otp = new OtpSession(key);
        var otpSlot = slot == 1 ? Slot.ShortPress : Slot.LongPress;

        if (!(slot == 1 ? otp.IsShortPressConfigured : otp.IsLongPressConfigured))
        {
            throw new HardwareKeyException(HardwareKeyFailure.SlotNotConfigured, $"Slot {slot} of this YubiKey is empty.");
        }

        var touched = false;
        try
        {
            return otp.CalculateChallengeResponse(otpSlot)
                .UseChallenge(challenge)
                .UseYubiOtp(false)
                .UseTouchNotifier(() =>
                {
                    touched = true;
                    touchNeeded();
                })
                .GetDataBytes()
                .ToArray();
        }
        catch (Exception ex) when (ex is KeyboardConnectionException or InvalidOperationException)
        {
            throw Volatile.Read(ref touched)
                ? new HardwareKeyException(HardwareKeyFailure.TimedOut, "The YubiKey wasn't touched in time.", ex)
                : new HardwareKeyException(
                    HardwareKeyFailure.SlotNotConfigured, $"Slot {slot} of this YubiKey isn't set up for HMAC-SHA1 challenge-response.", ex);
        }
    }

    private static List<IYubiKeyDevice> OtpKeys() =>
        [.. YubiKeyDevice.FindByTransport(Transport.HidKeyboard | Transport.SmartCard)
            .Where(key => ((key.EnabledUsbCapabilities | key.EnabledNfcCapabilities) & YubiKeyCapabilities.Otp) != 0)];

    private static HardwareKeyInfo Describe(IYubiKeyDevice key)
    {
        using var otp = new OtpSession(key);
        List<int> slots = [];

        if (otp.IsShortPressConfigured)
        {
            slots.Add(1);
        }

        if (otp.IsLongPressConfigured)
        {
            slots.Add(2);
        }

        return new HardwareKeyInfo($"YubiKey {key.FirmwareVersion.Major}", key.SerialNumber, slots);
    }

    /// <summary>Runs an SDK call, turning a platform that cannot reach keys into one refusal.</summary>
    private static T Reach<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException
            or PlatformNotSupportedException or Yubico.PlatformInterop.PlatformApiException)
        {
            throw new HardwareKeyException(
                HardwareKeyFailure.Unavailable, $"keypaste can't reach YubiKeys on this system: {ex.Message}", ex);
        }
    }

    /// <summary>Returns once <paramref name="asking"/> has finished or the wait is cancelled, whichever is first.</summary>
    private static void Wait(Task asking, CancellationToken cancellationToken)
    {
        var finished = ((IAsyncResult)asking).AsyncWaitHandle;

        if (finished.WaitOne(_pumpAfter))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            WaitHandle.WaitAny([finished, cancellationToken.WaitHandle]);
            return;
        }

        var frame = new DispatcherFrame();
        using var cancelled = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => frame.Continue = false));
        _ = asking.ContinueWith(
            _ => Dispatcher.UIThread.Post(() => frame.Continue = false),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
    }
}
