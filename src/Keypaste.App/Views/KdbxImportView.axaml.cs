using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class KdbxImportView : UserControl
{
    private readonly MaskedInput _password;
    private KdbxImportViewModel? _watched;
    private TopLevel? _root;

    public KdbxImportView()
    {
        AvaloniaXamlLoader.Load(this);

        _password = this.FindControl<MaskedInput>("ImportPassword")!;
        _password.Submitted += (_, _) => Run(Model?.UnlockCommand);

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
                FocusLater(_watched.IsReadable ? _password : First("ChooseAnother", "ImportConfirm"));
            }
        };
    }

    private KdbxImportViewModel? Model => DataContext as KdbxImportViewModel;

    // Esc and Enter are taken at the window, so they work wherever focus sits while the dialog is open.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        _root?.AddHandler(KeyDownEvent, OnRootKeyDown);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _root?.RemoveHandler(KeyDownEvent, OnRootKeyDown);
        _root = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Model is not { } model)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && model.ShowsConfirm && model.ConfirmCommand.CanExecute(null))
        {
            model.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Once the password field goes, the keyboard moves to the confirm button rather than out of the dialog.</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KdbxImportViewModel.NeedsUnlock) || Model is not { NeedsUnlock: false })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
                var stays = focused is { IsEffectivelyVisible: true } && focused != _password && this.IsVisualAncestorOf(focused);

                if (!stays)
                {
                    First("ImportConfirm")?.Focus();
                }
            },
            DispatcherPriority.Background);
    }

    private Button? First(params string[] names) =>
        names.Select(name => this.FindControl<Button>(name)).FirstOrDefault(button => button is { IsVisible: true });

    private static void FocusLater(Control? control)
    {
        if (control is not null)
        {
            Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Background);
        }
    }

    private static void Run(AsyncRelayCommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
