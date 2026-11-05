using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Share…, from an item's ⋯ menu: a step linking the terms of use the first time, and the form that makes one link, over the screen it was opened from.</summary>
internal sealed partial class ShareDialogView : UserControl
{
    private SharingViewModel? _watched;

    public ShareDialogView() => AvaloniaXamlLoader.Load(this);

    // The keyboard goes into the dialog as it opens and as its form follows that step, so that Return and Escape reach them.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnShareChanged;
        }

        _watched = DataContext as SharingViewModel;

        if (_watched is not null)
        {
            _watched.PropertyChanged += OnShareChanged;
            Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
        }
    }

    private void OnShareChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharingViewModel.IsReadingTerms))
        {
            Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
        }
    }

    private void FocusFirst()
    {
        if (_watched is { IsReadingTerms: true })
        {
            this.FindControl<Button>("ContinueToShare")?.Focus();
        }
        else
        {
            this.FindControl<TextBox>("Recipient")?.Focus();
        }
    }
}
