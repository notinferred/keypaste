using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Tells the person, without taking focus, that an agent asked for a vault while the app was locked (D-0466).</summary>
internal sealed partial class LockedNoticeWindow : PromptWindow
{
    public LockedNoticeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var dismiss = this.FindControl<Button>("Dismiss")!;
        dismiss.Click += (_, _) => Close();

        // Focused once the person brings it forward, never as it opens, which would take the keyboard from the window they are typing in.
        Activated += (_, _) => dismiss.Focus();
    }

    /// <summary>A notice naming the vault an agent asked for, whose Open keypaste is armed a moment after it opens.</summary>
    /// <param name="vaultName">The vault's file name.</param>
    /// <param name="open">Shows the unlock screen; called once the notice has closed.</param>
    /// <param name="clock">The clock the arming delay runs on.</param>
    internal LockedNoticeWindow(string vaultName, Action open, TimeProvider clock)
        : this()
    {
        ArgumentNullException.ThrowIfNull(vaultName);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(clock);

        this.FindControl<TextBlock>("HeadText")!.Text = $"An agent asked for {vaultName}";
        this.FindControl<Button>("Open")!.Click += (_, _) =>
        {
            Close();
            open();
        };
        Opened += (_, _) => _ = ArmAsync(clock);
    }

    /// <summary>Lets Open keypaste be pressed, as <see cref="PromptViewModel.ArmingDelay"/> after opening does.</summary>
    internal void Arm() => this.FindControl<Button>("Open")!.IsEnabled = true;

    private async Task ArmAsync(TimeProvider clock)
    {
        await Task.Delay(PromptViewModel.ArmingDelay, clock).ConfigureAwait(false);
        Dispatcher.UIThread.Post(Arm);
    }
}
