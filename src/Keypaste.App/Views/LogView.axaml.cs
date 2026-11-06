using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// The activity view.
/// </summary>
/// <remarks>
/// Everything this screen does — reading the log, checking the chain, deciding what to say about a
/// file that is not there — belongs to <see cref="ViewModels.LogViewModel"/>, which is where it can be
/// tested without a display. What is here tells the table whether a narrow window dropped Where.
/// </remarks>
internal sealed partial class LogView : UserControl
{
    private ShellViewModel? _shell;

    public LogView() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ShowWhere();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _shell = this.FindAncestorOfType<ShellView>()?.DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.PropertyChanged += OnShellChanged;
        }

        ShowWhere();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellChanged;
            _shell = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsNarrow))
        {
            ShowWhere();
        }
    }

    private void ShowWhere() => (DataContext as LogViewModel)?.Table.Show(TableStates.Where, _shell?.IsNarrow != true);
}
