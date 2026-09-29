using Avalonia.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App;

/// <summary>The macOS menu bar's File menu, where a Mac user looks for an import (N.1a1).</summary>
/// <remarks>
/// Built from the shell of the moment rather than one captured at launch, because a lock disposes
/// the shell and an unlock builds another. With no shell, or while a dialog is open, the item is off.
/// </remarks>
internal static class AppMenu
{
    internal const string ImportHeader = "Import .kdbx…";

    internal static NativeMenu Build(Func<ShellViewModel?> shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        var import = new NativeMenuItem(ImportHeader)
        {
            Command = new RelayCommand(() => shell()?.ImportCommand.Execute(null)),
        };

        var file = new NativeMenu();
        file.Add(import);
        file.NeedsUpdate += (_, _) => import.IsEnabled = CanImport(shell());
        import.IsEnabled = CanImport(shell());

        var bar = new NativeMenu();
        bar.Add(new NativeMenuItem("File") { Menu = file });
        return bar;
    }

    internal static bool CanImport(ShellViewModel? shell) =>
        shell is { HasShare: false } && shell.ImportCommand.CanExecute(null);
}
