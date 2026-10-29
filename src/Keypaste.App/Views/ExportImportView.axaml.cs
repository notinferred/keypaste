using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ExportImportView : UserControl
{
    /// <summary>Each step's action; the first one showing and enabled takes the keyboard.</summary>
    private static readonly string[] _actions = ["ExportDone", "ExportConfirm", "ExportChooseAnother", "ExportCancel"];

    private ExportImportViewModel? _watched;
    private TopLevel? _root;

    public ExportImportView()
    {
        AvaloniaXamlLoader.Load(this);

        // The view is built with the shell and hidden; a dialog opens when it is given a model.
        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnModelChanged;
            }

            _watched = Model;

            if (_watched is not null)
            {
                _watched.PropertyChanged += OnModelChanged;
                FocusLater();
            }
        };
    }

    private ExportImportViewModel? Model => DataContext as ExportImportViewModel;

    // Esc and Enter are taken at the window, as the KeePass database dialog takes them, so they work wherever focus
    // sits while the dialog is open: Esc closes it, and Enter imports, or closes it once the import is done.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        _root?.AddHandler(KeyDownEvent, OnRootEscape, RoutingStrategies.Tunnel);
        _root?.AddHandler(KeyDownEvent, OnRootKeyDown);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _root?.RemoveHandler(KeyDownEvent, OnRootEscape);
        _root?.RemoveHandler(KeyDownEvent, OnRootKeyDown);
        _root = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRootEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || Model is not { } model)
        {
            return;
        }

        model.CancelCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled || Model is not { } model)
        {
            return;
        }

        var command = model.IsImported ? model.CancelCommand : model.ConfirmCommand;

        if (command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>As the dialog moves from reading to its preview, its refusal or its report, the keyboard moves to that step's action.</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExportImportViewModel.IsPreviewing) or nameof(ExportImportViewModel.IsUnreadable) or nameof(ExportImportViewModel.IsImported))
        {
            FocusLater();
        }
    }

    private void FocusLater() =>
        Dispatcher.UIThread.Post(
            () => _actions
                .Select(name => this.FindControl<Button>(name))
                .FirstOrDefault(button => button is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })?
                .Focus(),
            DispatcherPriority.Background);
}
