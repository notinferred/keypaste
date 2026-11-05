using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>A drag delivered to a drawn window, which the platform routes by hit-testing as it does a real one.</summary>
internal static class WindowDrag
{
    private const DragDropEffects _all = DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move;

    internal static DataTransfer Files(params string[] paths)
    {
        var data = new DataTransfer();

        foreach (var path in paths)
        {
            data.Add(DataTransferItem.CreateFile(LocalFile(path)));
        }

        return data;
    }

    internal static Point Centre(TopLevel window, Visual control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    internal static void Over(TopLevel window, Point at, IDataTransfer data)
    {
        window.DragDrop(at, RawDragEventType.DragEnter, data, _all, RawInputModifiers.None);
        window.DragDrop(at, RawDragEventType.DragOver, data, _all, RawInputModifiers.None);
        WindowInput.Drain();
    }

    internal static void Drop(TopLevel window, Point at, IDataTransfer data)
    {
        window.DragDrop(at, RawDragEventType.DragEnter, data, _all, RawInputModifiers.None);
        window.DragDrop(at, RawDragEventType.Drop, data, _all, RawInputModifiers.None);
        WindowInput.Drain();
    }

    // Avalonia lets no test implement a storage file, and the platform's own is internal.
    private static IStorageFile LocalFile(string path)
    {
        var type = typeof(DataTransfer).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile");
        Assert.True(type is not null, "Avalonia no longer has BclStorageFile; name a dragged file another way");
        return (IStorageFile)Activator.CreateInstance(type, [new FileInfo(path)])!;
    }
}
