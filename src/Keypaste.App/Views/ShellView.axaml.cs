using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ShellView : UserControl
{
    private ShellViewModel? _shell;
    private Control? _focusBeforeImport;

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
            _shell.PropertyChanged -= OnShellChanged;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.SearchFocusRequested += OnSearchFocusRequested;
            _shell.PropertyChanged += OnShellChanged;
        }
    }

    /// <summary>The import dialog hands the keyboard back to what had it, or to Import .kdbx, when it closes.</summary>
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.HasImport) || _shell is null)
        {
            return;
        }

        if (_shell.HasImport)
        {
            // Read now: the dialog moves focus into itself on a later dispatcher pass.
            _focusBeforeImport = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var back = _focusBeforeImport is { IsEffectivelyVisible: true } before && TopLevel.GetTopLevel(before) is not null
                    ? before
                    : this.FindControl<Button>("ImportKdbx");
                _focusBeforeImport = null;
                back?.Focus();
            },
            DispatcherPriority.Background);
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        var search = this.FindControl<TextBox>("ShellSearch")!;
        search.Focus();
        search.SelectAll();
    }
}
