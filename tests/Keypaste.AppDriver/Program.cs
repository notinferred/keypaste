using Keypaste.App;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.AppDriver;

/// <summary>Performs one desktop act on one vault through the screen a person uses, and exits.</summary>
/// <remarks>
/// <para>
/// The app half of <c>scripts/verify-keepassxc-workflows.sh</c> (9.4). KeePassXC makes the vault and
/// reads it back; between the two, this presses the same commands the views bind to, on the same view
/// models, so a refusal a screen makes before core is reached is the app's refusal too. It never calls
/// core for the act under test: <c>Keypaste.VaultRestorer</c> does that for the per-feature gates.
/// </para>
/// <para>
/// A command that is disabled is a refusal, not a silent no-op, so a greyed-out button cannot pass
/// for a save. Output is the screen's own sentence and never a value. Secrets arrive in the
/// environment, as they do for the restorer. Exit 0 did it, 1 the screen refused, 2 usage, 3 the
/// driver could not arrange the act.
/// </para>
/// <para>
/// <c>hold</c> is the one act that does not exit: it unlocks as the app does, serves the vault as the
/// app does, prints the session it holds, and then locks or unlocks again on each line read from
/// standard input until it closes. <c>scripts/verify-session-authority.sh</c> drives it (U.1).
/// </para>
/// </remarks>
internal static class Program
{
    private const string _usage =
        "usage: open <vault>\n" +
        "       create <vault>\n" +
        "       edit <vault> <entry-path> <notes> <url>\n" +
        "       group-rename <vault> <group-path> <new-name>\n" +
        "       relocate <vault> <entry-path> <destination-group-path> <new-title>\n" +
        "       delete <vault> <entry-path>\n" +
        "       trash-restore <vault> <title>\n" +
        "       revision-restore <vault> <entry-path> <row | oldest>\n" +
        "       backup-restore <vault> <backup-file-name>\n" +
        "       export <vault> <destination>\n" +
        "       access <vault> [--password] [--attach <keyfile> | --remove-keyfile]\n" +
        "       hold <vault>   (then 'lock' or 'unlock' per line of standard input)\n" +
        "KEYPASTE_HOME must be set. KEYPASTE_DRIVER_PASSWORD is the password typed (empty for none),\n" +
        "KEYPASTE_DRIVER_KEYFILE the keyfile chosen, KEYPASTE_DRIVER_NEW_PASSWORD a new password or entry password.";

    private static async Task<int> Main(string[] args)
    {
        var home = Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable);

        if (string.IsNullOrEmpty(home))
        {
            Console.Error.WriteLine("KEYPASTE_HOME is unset, and the driver never touches a real home");
            return 2;
        }

        var driver = new Driver(home);

        try
        {
            return args switch
            {
                ["open", var vault] => await driver.OpenAsync(vault).ConfigureAwait(true),
                ["create", var vault] => await driver.CreateAsync(vault).ConfigureAwait(true),
                ["edit", var vault, var entry, var notes, var url] => await driver.EditAsync(vault, entry, notes, url).ConfigureAwait(true),
                ["group-rename", var vault, var group, var name] => await driver.RenameGroupAsync(vault, group, name).ConfigureAwait(true),
                ["relocate", var vault, var entry, var group, var title] => await driver.RelocateAsync(vault, entry, group, title).ConfigureAwait(true),
                ["delete", var vault, var entry] => await driver.DeleteAsync(vault, entry).ConfigureAwait(true),
                ["trash-restore", var vault, var title] => await driver.RestoreRecycledAsync(vault, title).ConfigureAwait(true),
                ["revision-restore", var vault, var entry, var row] => await driver.RestoreRevisionAsync(vault, entry, row).ConfigureAwait(true),
                ["backup-restore", var vault, var backup] => await driver.RestoreBackupAsync(vault, backup).ConfigureAwait(true),
                ["export", var vault, var destination] => await driver.ExportAsync(vault, destination).ConfigureAwait(true),
                ["access", var vault, .. var change] => await driver.ChangeAccessAsync(vault, change).ConfigureAwait(true),
                ["hold", var vault] => await driver.HoldAsync(vault).ConfigureAwait(true),
                _ => Usage(),
            };
        }
        catch (DriverException ex)
        {
            Console.Error.WriteLine($"driver could not arrange the act: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"driver failed: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine(_usage);
        return 2;
    }
}

internal sealed class DriverException(string message) : Exception(message);

internal sealed class Driver(string home)
{
    private readonly string _password = Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_PASSWORD") ?? string.Empty;
    private readonly string? _keyfile = NullIfEmpty(Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_KEYFILE"));
    private readonly string _newPassword = Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_NEW_PASSWORD") ?? string.Empty;
    private readonly Picker _picker = new();

    internal async Task<int> OpenAsync(string vault)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);
        return await UnlockAsync(unlock, session, vault).ConfigureAwait(true) ?? Did("opened");
    }

    internal async Task<int> CreateAsync(string vault)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        _picker.NewPath = vault;
        await PressAsync(unlock.StartCreateCommand, "Create").ConfigureAwait(true);

        if (!unlock.IsCreating)
        {
            return Refused(unlock.Message);
        }

        if (await ChooseKeyfileAsync(unlock).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        foreach (var c in _newPassword)
        {
            unlock.TypeNew(c);
            unlock.TypeConfirm(c);
        }

        if (!unlock.CreateCommand.CanExecute(null))
        {
            return Refused("the Create button is disabled");
        }

        await unlock.CreateCommand.ExecuteAsync().ConfigureAwait(true);
        return session.IsUnlocked ? Did("created") : Refused(unlock.Message);
    }

    internal Task<int> EditAsync(string vault, string entry, string notes, string url) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            Press(detail.EditCommand, "Edit");
            detail.DraftNotes = notes;
            detail.DraftUrl = url;

            foreach (var c in _newPassword)
            {
                detail.NewPassword.Type(c);
            }

            Press(detail.SaveCommand, "Save");
            return detail.IsEditing || entries.Error is not null ? Refused(entries.Error) : Did($"saved {entry}");
        });

    internal Task<int> RenameGroupAsync(string vault, string group, string name) =>
        WithEntriesAsync(vault, entries =>
        {
            entries.SelectedGroup = entries.Groups.SingleOrDefault(node => node.Path == group)
                ?? throw new DriverException($"no group '{group}' in the sidebar");

            Press(entries.BeginRenameGroupCommand, "Rename group");
            entries.DraftGroupName = name;
            Press(entries.ConfirmRenameGroupCommand, "Rename");
            return entries.IsRenamingGroup || entries.Error is not null ? Refused(entries.Error) : Did(entries.Notice);
        });

    internal Task<int> RelocateAsync(string vault, string entry, string group, string title) =>
        WithEntriesAsync(vault, entries =>
        {
            Select(entries, entry);

            Press(entries.OrganizeCommand, "Organize");
            entries.DraftTitle = title;
            entries.MoveTarget = entries.MoveTargets.SingleOrDefault(node => node.Path == group)
                ?? throw new DriverException($"no group '{group}' to move to");
            Press(entries.ConfirmOrganizeCommand, "Move");
            return entries.IsOrganizing || entries.Error is not null ? Refused(entries.Error) : Did(entries.Notice);
        });

    internal Task<int> DeleteAsync(string vault, string entry) =>
        WithEntriesAsync(vault, entries =>
        {
            Select(entries, entry);

            Press(entries.DeleteCommand, "Delete");
            Press(entries.ConfirmDeleteCommand, "Move to trash");
            return entries.Error is not null ? Refused(entries.Error) : Did(entries.Notice);
        });

    internal async Task<int> RestoreRecycledAsync(string vault, string title)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var trash = new TrashViewModel(session);

        trash.Selected = trash.Rows.SingleOrDefault(row => row.Title == title)
            ?? throw new DriverException($"no single '{title}' in the trash");
        Press(trash.RestoreCommand, "Restore");
        return trash.Error is not null ? Refused(trash.Error) : Did(trash.Notice);
    }

    internal Task<int> RestoreRevisionAsync(string vault, string entry, string which) =>
        WithEntriesAsync(vault, entries =>
        {
            var history = Select(entries, entry).History;

            Press(history.ToggleCommand, "Show history");

            var row = which == "oldest"
                ? history.Rows.Count - 1
                : int.Parse(which, System.Globalization.CultureInfo.InvariantCulture);

            if (row < 0 || row >= history.Rows.Count)
            {
                throw new DriverException($"the history shows {history.Rows.Count} revisions, not row {row}");
            }

            history.Selected = history.Rows[row];
            Press(history.RestoreCommand, "Restore this version");
            return entries.Error is not null ? Refused(entries.Error) : Did($"restored row {row} of {entry}");
        });

    internal async Task<int> RestoreBackupAsync(string vault, string backup)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        unlock.Offer(vault);

        if (!string.Equals(unlock.SelectedPath, Path.GetFullPath(vault), StringComparison.Ordinal))
        {
            return Refused(unlock.Message);
        }

        if (await ChooseKeyfileAsync(unlock).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        Press(unlock.StartRestoreCommand, "Restore a backup");

        var restore = unlock.Restore ?? throw new DriverException("the restore panel did not open");

        restore.Selected = restore.Rows.SingleOrDefault(row => Path.GetFileName(row.Backup.Path) == backup)
            ?? throw new DriverException($"no backup '{backup}' listed");

        foreach (var c in _password)
        {
            restore.Type(c);
        }

        await PressAsync(restore.CheckCommand, "Check").ConfigureAwait(true);

        if (!restore.IsConfirming)
        {
            return Refused(restore.Message);
        }

        await PressAsync(restore.ConfirmCommand, "Restore").ConfigureAwait(true);
        return session.IsUnlocked ? Did($"restored {backup}") : Refused(unlock.Restore?.Message ?? unlock.Message);
    }

    internal async Task<int> ExportAsync(string vault, string destination)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var settings = new SettingsViewModel(session, home, new DesktopPreferences(home), _ => { }, _picker);

        _picker.ExportPath = Path.GetFullPath(destination);
        await PressAsync(settings.ExportCommand, "Export").ConfigureAwait(true);
        return settings.Message.StartsWith("An encrypted copy is at", StringComparison.Ordinal)
            ? Did(settings.Message)
            : Refused(settings.Message);
    }

    internal async Task<int> ChangeAccessAsync(string vault, string[] change)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var access = new VaultAccessViewModel(session, home, _picker);

        foreach (var c in _password)
        {
            access.TypeCurrent(c);
        }

        for (var i = 0; i < change.Length; i++)
        {
            switch (change[i])
            {
                case "--password":
                    access.SetPassword = true;

                    foreach (var c in _newPassword)
                    {
                        access.TypeNew(c);
                        access.TypeConfirm(c);
                    }

                    break;

                case "--attach" when i + 1 < change.Length:
                    _picker.KeyfilePath = change[++i];
                    await PressAsync(access.ChooseKeyfileCommand, "Choose keyfile").ConfigureAwait(true);

                    if (!access.AttachKeyfile)
                    {
                        return Refused(access.Message);
                    }

                    break;

                case "--remove-keyfile":
                    access.RemoveKeyfile = true;
                    break;

                default:
                    throw new DriverException($"'{change[i]}' is not an access change");
            }
        }

        if (!access.ReviewCommand.CanExecute(null))
        {
            return Refused("the Review button is disabled");
        }

        access.ReviewCommand.Execute(null);
        await PressAsync(access.ConfirmCommand, "Change").ConfigureAwait(true);
        return access.Message.StartsWith("Changed.", StringComparison.Ordinal) ? Did(access.Message) : Refused(access.Message);
    }

    private async Task<int> WithEntriesAsync(string vault, Func<EntriesViewModel, int> act)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);
        using var entries = new EntriesViewModel(session, countdown);
        return act(entries);
    }

    internal async Task<int> HoldAsync(string vault)
    {
        using var session = new AppVaultSession(TimeProvider.System, AppVaultSession.MaximumIdleTimeout, home);
        using var host = new SessionHost(session, Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable));

        if (await HoldOnceAsync(session, host, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        while (await Console.In.ReadLineAsync().ConfigureAwait(true) is { } command)
        {
            switch (command.Trim())
            {
                case "lock":
                    session.Lock(VaultLockReason.Manual);
                    Console.Out.WriteLine("locked");
                    break;

                case "unlock":
                    if (await HoldOnceAsync(session, host, vault).ConfigureAwait(true) is { } again)
                    {
                        return again;
                    }

                    break;

                default:
                    throw new DriverException($"hold understands 'lock' and 'unlock', not '{command}'");
            }
        }

        return 0;
    }

    private async Task<int?> HoldOnceAsync(AppVaultSession session, SessionHost host, string vault)
    {
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        Console.Out.WriteLine(host.Endpoint is { } endpoint
            ? $"holding session {session.SessionId} as process {Environment.ProcessId} on {endpoint}"
            : $"holding session {session.SessionId} as process {Environment.ProcessId}, not served: {host.Failure}");

        return null;
    }

    private UnlockViewModel Screen(AppVaultSession session) => new(session, home, _picker, () => { });

    /// <summary>Chooses the vault and the keyfile and types the password, as the unlock screen is used.</summary>
    /// <returns>Null when the vault opened, or the refusal's exit code.</returns>
    private async Task<int?> UnlockAsync(UnlockViewModel unlock, AppVaultSession session, string vault)
    {
        if (!unlock.Offer(vault))
        {
            return Refused(unlock.Message);
        }

        if (await ChooseKeyfileAsync(unlock).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        foreach (var c in _password)
        {
            unlock.Type(c);
        }

        if (!unlock.UnlockCommand.CanExecute(null))
        {
            return Refused("the Unlock button is disabled");
        }

        await unlock.UnlockCommand.ExecuteAsync().ConfigureAwait(true);
        return session.IsUnlocked ? null : Refused(unlock.Message);
    }

    /// <summary>Picks the configured keyfile, or clears one the recent list remembered when none is configured.</summary>
    private async Task<int?> ChooseKeyfileAsync(UnlockViewModel unlock)
    {
        if (_keyfile is null)
        {
            if (unlock.HasKeyfile)
            {
                Press(unlock.ClearKeyfileCommand, "Clear keyfile");
            }

            return null;
        }

        _picker.KeyfilePath = _keyfile;
        await PressAsync(unlock.ChooseKeyfileCommand, "Choose keyfile").ConfigureAwait(true);
        return unlock.HasKeyfile ? null : Refused(unlock.Message);
    }

    private static EntryDetailViewModel Select(EntriesViewModel entries, string path)
    {
        entries.Selected = entries.Rows.SingleOrDefault(row => row.Path == path)
            ?? throw new DriverException($"no single '{path}' in the entry list");
        return entries.Detail ?? throw new DriverException($"'{path}' has no detail pane: {entries.Error}");
    }

    private static void Press(RelayCommand command, string button)
    {
        if (!command.CanExecute(null))
        {
            throw new DriverException($"the {button} button is disabled");
        }

        command.Execute(null);
    }

    private static async Task PressAsync(AsyncRelayCommand command, string button)
    {
        if (!command.CanExecute(null))
        {
            throw new DriverException($"the {button} button is disabled");
        }

        await command.ExecuteAsync().ConfigureAwait(true);
    }

    private static int Did(string? what)
    {
        Console.Out.WriteLine(what ?? "done");
        return 0;
    }

    private static int Refused(string? why)
    {
        Console.Out.WriteLine($"refused: {why}");
        return 1;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed class Picker : IVaultFilePicker
    {
        internal string? NewPath { get; set; }

        internal string? ExportPath { get; set; }

        internal string? KeyfilePath { get; set; }

        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult(NewPath);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult(ExportPath);

        public Task<string?> PickKeyfileAsync() => Task.FromResult(KeyfilePath);
    }
}
