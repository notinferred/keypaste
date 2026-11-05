using Keypaste.Core;
using Keypaste.Core.Import;
using Keypaste.Core.Ownership;

namespace Keypaste.App.ViewModels;

/// <summary>What a file dropped on the shell would do.</summary>
/// <param name="Caption">Says so, by the drop's highlight.</param>
/// <param name="Take">Opens the flow a picker's file opens, whose own confirmation still asks.</param>
/// <param name="OnItem">Whether it goes to the open item, so the item's pane is what lights up.</param>
internal sealed record FileDrop(string Caption, Action Take, bool OnItem);

/// <summary>Files dropped on the window, and files the system or a second start names while a vault is open.</summary>
internal sealed partial class ShellViewModel
{
    private Action? _openHanded;

    /// <summary>What the file's kind makes of a drop anywhere on the shell: an import for what keypaste can import, otherwise an attachment to the open item; null when nothing here takes it.</summary>
    internal FileDrop? OfferFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (TakesNoFile || !HandedFile.IsWholeFile(path))
        {
            return null;
        }

        var name = Shown(Path.GetFileName(path));

        if (KdbxImport.TryProbe(path, out _, out _) || Core.Import.ExportImport.Recognize(path, Core.Import.ExportImport.Readers) is not null)
        {
            return new FileDrop($"Import {name}", () => OpenImport(path), OnItem: false);
        }

        if (IsDotEnv(name))
        {
            return ((Content as EnvSetsViewModel)?.OpenProject?.Name ?? (_projects.Count > 0 ? _projects[0].Name : null)) is { } project
                ? new FileDrop($"Import {name} into {Shown(project)}", () => PreviewDotEnv(project, path), OnItem: false)
                : null;
        }

        return ItemTakingFiles is { Selected: { } item, Detail: { } detail } && detail.Attachments.RefusalOfDropped(path) is null
            ? new FileDrop($"Attach {name} to {item.DisplayTitle}", () => detail.Attachments.AddDropped(path), OnItem: true)
            : null;
    }

    // Nothing is attached while a form in this view holds work, which an attachment's write and refresh would go past.
    private EntriesViewModel? ItemTakingFiles => Content is EntriesViewModel { HoldsNoForm: true, Selected: not null, Detail: not null } entries ? entries : null;

    /// <summary>Says a file that reached the window after all was not taken, since a platform may only name it once it is dropped.</summary>
    internal void RefuseFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var refusal = ItemTakingFiles?.Detail?.Attachments.RefusalOfDropped(path);
        ShowToast(refusal ?? $"keypaste can't take {Shown(Path.GetFileName(path))} here. It imports vaults, exports and .env files, and attaches other files to an open item.", ToastTone.Information);
    }

    /// <summary>What "Lock … and open …?" says while it is asked over the window, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHandedPrompt))]
    internal partial string? HandedPrompt { get; private set; }

    /// <summary>Where the file asked about is, so the person sees which one this is before answering.</summary>
    [ObservableProperty]
    internal partial string? HandedLocation { get; private set; }

    internal bool HasHandedPrompt => HandedPrompt is not null;

    /// <summary>
    /// Asks before a file the system or a second start named may lock this vault to go to the unlock screen. Nothing outside
    /// this process can answer, what is already up here is left as it is, and a file the unlock screen would refuse never costs the person their session.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="open">Locks and offers the file; runs only on the person's answer, past the discard ask for any draft.</param>
    internal void AskOpenHanded(string path, Action open)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(open);

        if (_disposed)
        {
            return;
        }

        var name = Shown(Path.GetFileName(path));

        if (RefusalOfHanded(path, name) is { } refusal)
        {
            ShowToast(refusal, ToastTone.Information);
            return;
        }

        if (TakesNoFile)
        {
            ShowToast($"keypaste left {name} unopened. Answer what's open here, then open it again.", ToastTone.Information);
            return;
        }

        _openHanded = open;
        HandedLocation = DisplayTextSanitizer.Escaped(path);
        HandedPrompt = $"Lock {VaultName} and open {name}?";
    }

    private string? RefusalOfHanded(string path, string name) =>
        !HandedFile.IsLocal(path) ? $"keypaste won't open {name} from a network location it was handed. Lock this vault and choose it with Open a vault instead."
        : string.Equals(Path.GetFullPath(path), _session.VaultPath, PathIdentity.Comparison) ? $"{name} is already open."
        : !KdbxHeader.IsVaultFile(path) ? $"keypaste can't open {name}: it isn't there, or it isn't a readable KeePass vault."
        : null;

    [RelayCommand]
    private void KeepVault() => CloseHandedPrompt();

    [RelayCommand]
    private void LockAndOpen()
    {
        var open = _openHanded;
        CloseHandedPrompt();

        if (open is not null)
        {
            Leave(open);
        }
    }

    private void CloseHandedPrompt()
    {
        _openHanded = null;
        HandedLocation = null;
        HandedPrompt = null;
    }

    private bool TakesNoFile =>
        _disposed || HasImport || HasDiscardPrompt || HasHandedPrompt || HasShare || HasGeneratorDialog || IsWaitingForTouch || Authority?.IsAsking == true;

    private static string Shown(string text) => DisplayTextSanitizer.Sanitize(text).Text;

    // A .env.keypaste holds references for a project to read, not values to import.
    private static bool IsDotEnv(string name) =>
        !name.EndsWith(".keypaste", StringComparison.OrdinalIgnoreCase)
        && (name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".env", StringComparison.OrdinalIgnoreCase));

    private void PreviewDotEnv(string project, string path) => Leave(() =>
    {
        ShowProject(project);
        (Content as EnvSetsViewModel)?.OpenProject?.Import.Preview(path);
    });
}
