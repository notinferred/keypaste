using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    // One per process, never disposed: Avalonia 12.1's StartNew can return a session whose Dispose throws (F.51).
    private static readonly HeadlessUnitTestSession _display = HeadlessUnitTestSession.StartNew(typeof(PromptScreen));

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
        var channel = new WindowApprovalChannel(TimeProvider.System, ApprovalLimits.Default.Window);
        channel.Shown += OnShown;
        return channel;
    }

    /// <summary>
    /// Answers the prompt on screen: <c>approve</c> (the timed allow), <c>once</c>, <c>always</c> (Always for),
    /// <c>allow</c> (a change's confirmation), <c>deny</c> or <c>close</c>.
    /// </summary>
    internal Task AnswerAsync(string answer) =>
        Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                var window = _open ?? throw new DriverException("no prompt is on screen");

                switch (answer)
                {
                    case "approve" or "once" or "always" or "allow":
                        var allow = Button(window, answer switch
                        {
                            "once" => "AllowOnce",
                            "always" => "AlwaysFor",
                            "allow" => "Allow",
                            _ => "Approve",
                        });
                        var armedBy = DateTime.UtcNow + _armingWait;

                        // A person reads the prompt before allowing it, which is armed a moment after it appears.
                        while (!allow.IsEffectivelyEnabled)
                        {
                            if (DateTime.UtcNow > armedBy)
                            {
                                throw new DriverException($"{allow.Name} never became pressable");
                            }

                            await Task.Delay(50).ConfigureAwait(true);
                        }

                        Click(window, allow);
                        break;

                    case "deny":
                        Click(window, Button(window, "Deny"));
                        break;

                    default:
                        window.Close();
                        break;
                }
            });

    /// <summary>
    /// Waits until the prompt on screen is armed, then presses Show the prompt on Agents through
    /// <paramref name="press"/> on this display's thread, and says whether the prompt is then armed and which
    /// of its buttons holds focus: <c>prompt raised armed=false focus=Deny</c> is what a bring-forward must do.
    /// </summary>
    internal Task<string> RaiseAsync(Action press) =>
        Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                var window = _open ?? throw new DriverException("no prompt is on screen");
                var allow = new[] { "Approve", "AllowOnce", "Allow" }.Select(window.FindControl<Button>).FirstOrDefault(button => button is { IsVisible: true })
                    ?? throw new DriverException("the prompt has no allow button");
                var armedBy = DateTime.UtcNow + _armingWait;

                while (!allow.IsEffectivelyEnabled)
                {
                    if (DateTime.UtcNow > armedBy)
                    {
                        throw new DriverException($"{allow.Name} never became pressable");
                    }

                    await Task.Delay(50).ConfigureAwait(true);
                }

                press();
                Dispatcher.UIThread.RunJobs();
                var focus = new[] { "Deny", "AllowOnce", "Approve", "AlwaysFor", "Allow" }.FirstOrDefault(name => window.FindControl<Button>(name)?.IsFocused == true) ?? "elsewhere";
                return $"prompt raised armed={(allow.IsEffectivelyEnabled ? "true" : "false")} focus={focus}";
            });

    private void OnShown(object? sender, Window window)
    {
        window.CaptureRenderedFrame();

        // A notice asks nothing, so it is printed and left on screen; the next answer is for a prompt.
        if (window is NoticeWindow)
        {
            Console.Out.WriteLine($"notice text={Text(window, "NoticeText")}");
            return;
        }

        if (window is PairingNoticeWindow)
        {
            Console.Out.WriteLine($"pair-notice identity={Text(window, "IdentityText")} text={Text(window, "NoticeText")}");
            return;
        }

        _open = window;

        Console.Out.WriteLine(window switch
        {
            EnvApprovalWindow =>
                $"env-prompt project={Text(window, "ProjectText")} command={Text(window, "CommandText")} " +
                $"directory={Text(window, "DirectoryText")} keys={string.Join(',', Classed(window, "key"))} " +
                $"entries={string.Join(',', Classed(window, "entry"))} timed={window.FindControl<Button>("Approve")?.IsVisible == true}",
            RunApprovalWindow =>
                $"run-prompt title={Text(window, "TitleText")} program={Text(window, "ProgramText")} " +
                $"command={Text(window, "CommandText")} directory={Text(window, "DirectoryText")}",
            AccessChangeWindow =>
                $"access-prompt agent={Text(window, "AgentText")} asked={Text(window, "OriginText")} " +
                $"gains={string.Join("; ", Gains(window))} reason={Text(window, "ReasonText")}",
            TerminalEditWindow =>
                $"edit-prompt command={Text(window, "CommandText")} item={Text(window, "ItemText")} " +
                $"asked={Text(window, "OriginText")} changes={string.Join("; ", Changes(window))}",
            PairingWindow =>
                $"pair-prompt identity={Text(window, "IdentityText")} name={window.FindControl<TextBox>("NameBox")?.Text}",
            _ =>
                $"prompt client={Text(window, "ClientText")} label={Text(window, "LabelText")} " +
                $"entry={Text(window, "EntryText")} field={Text(window, "FieldText")} for={Text(window, "LifetimeText")} " +
                $"always={(window.FindControl<Button>("AlwaysFor")?.IsVisible == true ? Text(window, "AlwaysText") : "none")}",
        });

        var withdrawn = window switch
        {
            AccessChangeWindow => "access-prompt withdrawn",
            TerminalEditWindow => "edit-prompt withdrawn",
            PairingWindow => "pair-prompt withdrawn",
            _ => "prompt withdrawn",
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, window))
            {
                _open = null;
            }

            Console.Out.WriteLine(withdrawn);
        };
    }

    private static string? Text(Window window, string name) => window.FindControl<TextBlock>(name)?.Text;

    private static IEnumerable<string?> Gains(Window window) =>
        window.FindControl<ItemsControl>("GainsList")?.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("gain")).Select(block => block.Text) ?? [];

    private static IEnumerable<string?> Changes(Window window) =>
        window.FindControl<ItemsControl>("ChangesList")?.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("change")).Select(block => block.Text) ?? [];

    private static IEnumerable<string?> Classed(Window window, string className) =>
        window.FindControl<ItemsControl>("KeysList")?.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains(className)).Select(block => block.Text) ?? [];

    private static Button Button(Window window, string name) =>
        window.FindControl<Button>(name) ?? throw new DriverException($"the prompt has no {name} button");

    // Touches none of this type's static fields, so a launch that presses with it never starts this screen's display.
    internal static void Click(Window window, Button button)
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
    }
}
