using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class UnlockView : UserControl
{
    private readonly FileDragData.Judged<bool> _judged = new();
    private readonly UnlockForm _form;
    private UnlockViewModel? _watched;

    public UnlockView()
    {
        AvaloniaXamlLoader.Load(this);
        Cue = this.FindControl<DropCue>(nameof(Cue))!;
        _form = this.FindControl<UnlockForm>("Form")!;

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.FileChosen -= OnFileChosen;
            }

            _watched = Model;

            if (_watched is not null)
            {
                _watched.FileChosen += OnFileChosen;
            }
        };
    }

    // A vault picked in the open panel or dropped here is opened by typing its password, so that is where focus goes.
    private void OnFileChosen(object? sender, EventArgs e) => _form.FocusForm();

    private UnlockViewModel? Model => DataContext as UnlockViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // One file, which reads as a vault: the screen lights up only for what it would take. A platform that names the file only at the drop gets no highlight until then.
        var path = FileDragData.SinglePath(e.DataTransfer);
        var takes = Model is { } model && path is not null && _judged.Of(path, model.TakesDrop);
        var unnamed = path is null && !FileDragData.HasMany(e.DataTransfer) && e.DataTransfer.Contains(DataFormat.File);

        e.DragEffects = takes || unnamed ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;

        if (takes)
        {
            Cue.Show(this, UnlockViewModel.DropCaption(path!));
        }
        else
        {
            Cue.Hide();
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _judged.Forget();
        Cue.Hide();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);

        // A real filesystem path — the thing an HTML5 DataTransfer could never have given, and the
        // reason "drag a .kdbx" is achievable here at all.
        if (FileDragData.SinglePath(e.DataTransfer) is { } path && Model?.TakeDrop(path) == true)
        {
            _form.FocusForm();
        }

        e.Handled = true;
    }
}
