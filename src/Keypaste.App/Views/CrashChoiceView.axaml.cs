using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The crash-report choice with a link to the terms that cover it, on the unlock screen and in Settings.</summary>
internal sealed partial class CrashChoiceView : UserControl
{
    private CrashChoiceViewModel? _watched;

    public CrashChoiceView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnChoiceChanged;
            }

            _watched = DataContext as CrashChoiceViewModel;

            if (_watched is not null)
            {
                _watched.PropertyChanged += OnChoiceChanged;
            }
        };
    }

    // The button pressed is gone, so the keyboard stays here rather than falling back to the top of the window.
    private void OnChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CrashChoiceViewModel.Outcome))
        {
            var outcome = this.FindControl<TextBlock>("CrashChoiceOutcome");
            Dispatcher.UIThread.Post(() => outcome?.Focus(), DispatcherPriority.Background);
        }
    }
}
