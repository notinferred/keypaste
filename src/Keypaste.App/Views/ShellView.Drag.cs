using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>What the shell takes from a drag: a file from anywhere, and an item it is dragging itself onto a group.</summary>
internal sealed partial class ShellView
{
    private static readonly TimeSpan _railHold = TimeSpan.FromMilliseconds(500);

    private readonly FileDragData.Judged<FileDrop?> _judged = new();
    private readonly FileDragData.Judged<bool> _movable = new();
    private EntryRow? _heldItem;
    private DispatcherTimer? _railTimer;
    private bool _openedForDrag;

    /// <summary>Drags an item: it stays in this process, the platform being given nothing of it, until the drag ends.</summary>
    internal async Task DragItemAsync(EntryRow row, PointerPressedEventArgs press)
    {
        HoldItem(row);

        try
        {
            using var payload = FileDragData.ItemPayload();
            await DragDrop.DoDragDropAsync(press, payload, DragDropEffects.Move).ConfigureAwait(true);
        }
        finally
        {
            ReleaseItem();
        }
    }

    /// <summary>Whether an item is being dragged.</summary>
    internal bool IsHoldingItem => _heldItem is not null;

    /// <summary>The item being dragged from now on.</summary>
    internal void HoldItem(EntryRow row)
    {
        _movable.Forget();
        _heldItem = row;
    }

    /// <summary>The drag is over: nothing is held, and a sidebar it opened over the content closes.</summary>
    internal void ReleaseItem()
    {
        _heldItem = null;
        _movable.Forget();
        StopRailTimer();
        Cue.Hide();

        if (_openedForDrag)
        {
            _openedForDrag = false;
            _shell?.CloseSidebarOverlay();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (_heldItem is { } item)
        {
            e.DragEffects = OverItemTarget(item, e.GetPosition(this));
            return;
        }

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
        StopRailTimer();
        Cue.Hide();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _judged.Forget();
        StopRailTimer();
        Cue.Hide();

        if (_heldItem is { } item)
        {
            if (GroupAt(e.GetPosition(this)) is { } group && _shell?.Content is EntriesViewModel entries)
            {
                entries.MoveToGroup(item, group.Path);
            }

            return;
        }

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

    /// <summary>A group takes the item unless the vault would refuse the move; the rail's groups button, held, opens the groups over the content.</summary>
    private DragDropEffects OverItemTarget(EntryRow item, Point at)
    {
        var over = this.InputHitTest(at) as Visual;

        if (GroupAt(at) is { } group && _shell?.Content is EntriesViewModel entries && _movable.Of(group.Path, path => entries.CanMove(item, path)))
        {
            StopRailTimer();
            Cue.Show(over!.GetSelfAndVisualAncestors().OfType<ListBoxItem>().First(), $"Move to {group.Title}");
            return DragDropEffects.Move;
        }

        if (over?.GetSelfAndVisualAncestors().Contains(this.FindControl<Button>("RailGroups")) == true && _shell is { ShowsRail: true })
        {
            Cue.Show(this.FindControl<Button>("RailGroups")!, "Hold here to see the groups");
            StartRailTimer();
            return DragDropEffects.None;
        }

        StopRailTimer();
        Cue.Hide();
        return DragDropEffects.None;
    }

    private GroupRow? GroupAt(Point at) =>
        (this.InputHitTest(at) as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as GroupRow;

    private void StartRailTimer()
    {
        if (_railTimer is not null)
        {
            return;
        }

        _railTimer = new DispatcherTimer { Interval = _railHold };
        _railTimer.Tick += (_, _) =>
        {
            StopRailTimer();

            if (_heldItem is not null)
            {
                OpenGroupsForHeldItem();
            }
        };
        _railTimer.Start();
    }

    /// <summary>Opens the groups over the content for the held item, whose hint on the rail's button they would cover.</summary>
    internal void OpenGroupsForHeldItem()
    {
        Cue.Hide();
        _openedForDrag = true;
        _shell?.OpenSidebarOverlay();
    }

    private void StopRailTimer()
    {
        _railTimer?.Stop();
        _railTimer = null;
    }
}
