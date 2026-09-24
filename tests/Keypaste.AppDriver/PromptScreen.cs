using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Keypaste.App.Session;
using Keypaste.App.Views;
using Keypaste.Core.Approval;

namespace Keypaste.AppDriver;

/// <summary>
/// The app's own prompt window on a headless display drawn by Skia, answered as a person answers it:
/// by a click the display routes to a button through hit-testing, or by closing the window.
/// </summary>
/// <remarks>
/// What it prints is read from the drawn window's text, never from the view model behind it, so a
/// gate reading these lines sees what the window shows.
/// </remarks>
internal sealed class PromptScreen : IDisposable
{
    private static readonly TimeSpan _armingWait = TimeSpan.FromSeconds(10);

    private readonly HeadlessUnitTestSession _display = HeadlessUnitTestSession.StartNew(typeof(PromptScreen));
    private readonly TaskCompletionSource _closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _running;
    private Window? _open;

    // The display runs its dispatcher only inside a dispatch, so one stays open for the screen's
    // life: that is the main loop the app's own posts, from the threads a request arrives on, run in.
    internal PromptScreen() =>
        _running = _display.Dispatch(
            async () =>
            {
                await _closing.Task.ConfigureAwait(true);
                return true;
            },
            CancellationToken.None);

    /// <summary>The display: the app, drawn by Skia with its own font, as the app's tests draw it.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>The channel launch composes for each unlock, watched by this screen.</summary>
    internal IApprovalChannel Open()
    {
        var channel = new WindowApprovalChannel(TimeProvider.System);
        channel.Shown += OnShown;
        return channel;
    }

    /// <summary>Answers the prompt on screen: <c>approve</c>, <c>deny</c> or <c>close</c>.</summary>
    internal Task AnswerAsync(string answer) =>
        Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                var window = _open ?? throw new DriverException("no prompt is on screen");

                switch (answer)
                {
                    case "approve":
                        var approve = Button(window, "Approve");
                        var armedBy = DateTime.UtcNow + _armingWait;

                        // A person reads the prompt before pressing Approve, which is armed a moment after it appears.
                        while (!approve.IsEffectivelyEnabled)
                        {
                            if (DateTime.UtcNow > armedBy)
                            {
                                throw new DriverException("Approve never became pressable");
                            }

                            await Task.Delay(50).ConfigureAwait(true);
                        }

                        Click(window, approve);
                        break;

                    case "deny":
                        Click(window, Button(window, "Deny"));
                        break;

                    default:
                        window.Close();
                        break;
                }
            });

    private void OnShown(object? sender, Window window)
    {
        _open = window;
        window.CaptureRenderedFrame();

        Console.Out.WriteLine(window is EnvApprovalWindow
            ? $"env-prompt project={Text(window, "ProjectText")} command={Text(window, "CommandText")} " +
                $"directory={Text(window, "DirectoryText")} keys={Text(window, "KeysText")?.ReplaceLineEndings(",")}"
            : $"prompt client={Text(window, "ClientText")} label={Text(window, "LabelText")} " +
                $"entry={Text(window, "EntryText")} field={Text(window, "FieldText")} for={Text(window, "LifetimeText")}");

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, window))
            {
                _open = null;
            }

            Console.Out.WriteLine("prompt withdrawn");
        };
    }

    private static string? Text(Window window, string name) => window.FindControl<TextBlock>(name)?.Text;

    private static Button Button(Window window, string name) =>
        window.FindControl<Button>(name) ?? throw new DriverException($"the prompt has no {name} button");

    private static void Click(Window window, Button button)
    {
        window.CaptureRenderedFrame();

        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new DriverException("the button is not in the prompt window");

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        _closing.TrySetResult();
        _running.Wait(TimeSpan.FromSeconds(5));
        _display.Dispose();
    }
}
