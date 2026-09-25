using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ShellView : UserControl
{
    private ShellViewModel? _shell;

    /// <summary>Below this height the sidebar footer gives room to the project rows.</summary>
    private const double _shortHeight = 600;

    public ShellView() => AvaloniaXamlLoader.Load(this);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Classes.Set("short", e.NewSize.Height < _shortHeight);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_shell is not null)
        {
            _shell.SearchFocusRequested -= OnSearchFocusRequested;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.SearchFocusRequested += OnSearchFocusRequested;
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        var search = this.FindControl<TextBox>("ShellSearch")!;
        search.Focus();
        search.SelectAll();
    }
}
