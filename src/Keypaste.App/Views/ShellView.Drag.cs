using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>What the shell takes from a drag: a file from anywhere.</summary>
internal sealed partial class ShellView
{
    private readonly FileDragData.Judged<FileDrop?> _judged = new();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var path = FileDragData.SinglePath(e.DataTransfer);
        var drop = path is null || _shell is not { } shell ? null : _judged.Of(path, shell.OfferFile);

        if (drop is null)
        {
            Cue.Hide();
            var unnamed = path is null && !FileDragData.HasMany(e.DataTransfer) && e.DataTransfer.Contains(DataFormat.File);
            e.DragEffects = unnamed ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }

        Cue.Show(drop.OnItem ? this.FindControl<Control>("ContentArea")!.GetVisualDescendants().OfType<ContentControl>().FirstOrDefault(pane => pane.Name == "EntryPane" && pane.IsEffectivelyVisible) ?? this : this.FindControl<Control>("ContentArea")!, drop.Caption);
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _judged.Forget();
        Cue.Hide();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _judged.Forget();
        Cue.Hide();

        if (FileDragData.SinglePath(e.DataTransfer) is { } path && _shell is { } shell)
        {
            // Judged again, since the screen may have changed under a long drag.
            if (shell.OfferFile(path) is { } drop)
            {
                drop.Take();
            }
            else
            {
                shell.RefuseFile(path);
            }
        }
    }
}
