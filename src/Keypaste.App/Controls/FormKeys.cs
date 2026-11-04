using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;

namespace Keypaste.App.Controls;

/// <summary>What Return and Escape do inside a form: its confirm, never a danger one, and its Cancel, which first asks when the form holds a draft.</summary>
internal sealed class FormKeys : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand?> ConfirmProperty =
        AvaloniaProperty.RegisterAttached<FormKeys, Control, ICommand?>("Confirm");

    public static readonly AttachedProperty<ICommand?> CancelProperty =
        AvaloniaProperty.RegisterAttached<FormKeys, Control, ICommand?>("Cancel");

    public static readonly AttachedProperty<Func<string?>?> DraftProperty =
        AvaloniaProperty.RegisterAttached<FormKeys, Control, Func<string?>?>("Draft");

    static FormKeys()
    {
        ConfirmProperty.Changed.AddClassHandler<Control>(Listen);
        CancelProperty.Changed.AddClassHandler<Control>(Listen);
    }

    private FormKeys()
    {
    }

    public static ICommand? GetConfirm(Control control) => control.GetValue(ConfirmProperty);

    public static void SetConfirm(Control control, ICommand? value) => control.SetValue(ConfirmProperty, value);

    public static ICommand? GetCancel(Control control) => control.GetValue(CancelProperty);

    public static void SetCancel(Control control, ICommand? value) => control.SetValue(CancelProperty, value);

    public static Func<string?>? GetDraft(Control control) => control.GetValue(DraftProperty);

    public static void SetDraft(Control control, Func<string?>? value) => control.SetValue(DraftProperty, value);

    private static void Listen(Control form, AvaloniaPropertyChangedEventArgs change)
    {
        // Removed first because Confirm and Cancel each arrive here, and one form answers a key once.
        form.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        form.AddHandler(InputElement.KeyDownEvent, OnKeyDown);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control form || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.Source is not TextBox { AcceptsReturn: true } && GetConfirm(form) is { } confirm && confirm.CanExecute(null):
                confirm.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when GetCancel(form) is { } cancel && cancel.CanExecute(null):
                Cancel(form, cancel);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private static void Cancel(Control form, ICommand cancel)
    {
        if (GetDraft(form)?.Invoke() is { } draft && form.FindAncestorOfType<ShellView>()?.DataContext is ShellViewModel shell)
        {
            shell.AskDiscard(draft, () => cancel.Execute(null));
            return;
        }

        cancel.Execute(null);
    }
}
