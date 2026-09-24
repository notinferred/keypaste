using Keypaste.App;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
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
/// <c>hold</c> is the one act that does not exit: it composes the app's <see cref="AppAuthority"/> as
/// launch does, unlocks, prints the session it holds, and then locks or unlocks again on each line
/// read from standard input until it closes, which it answers as the app answers quitting. Each of
/// those prints a <c>status</c> line as the authority reports it.
/// <c>scripts/verify-session-authority.sh</c> drives it (U.1). With <c>--locked</c> it starts at the
/// unlock screen, as a launch does, and waits for <c>unlock</c>, so
/// <c>scripts/verify-session-lifecycle.sh</c> can relaunch after a crash (4.4b). With <c>--held-prompt</c> a request
/// that needs a person is put in front of one who never answers, printing <c>asking</c> and then
/// <c>withdrawn</c>, so <c>scripts/verify-lock-boundary.sh</c> can lock the app with a real request
/// waiting at its session (U.2). With <c>--approving-prompt</c> a person approves every request,
/// printing <c>asking</c> and <c>approved</c>, and the lines <c>edit</c>, <c>delete</c> and
/// <c>relocate</c> act on the held vault through the entries screen, so
/// <c>scripts/verify-current-state.sh</c> can change it between two real requests (U.3). The app
/// itself still has nowhere to ask until STEPS 4.4.
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
        "       hold <vault> [--locked] [--held-prompt | --approving-prompt]\n" +
        "            (then per line of standard input: lock, unlock, edit <entry-path>, delete <entry-path>,\n" +
        "             relocate <entry-path> <destination-group-path> <new-title>)\n" +
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
                ["hold", var vault, .. var options] => await HoldAsync(driver, vault, options).ConfigureAwait(true),
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

    private static Task<int> HoldAsync(Driver driver, string vault, string[] options)
    {
        var locked = false;
        Func<IApprovalChannel>? prompt = null;

        foreach (var option in options)
        {
            switch (option)
            {
                case "--locked":
                    locked = true;
                    break;
                case "--held-prompt" when prompt is null:
                    prompt = () => new Driver.HeldPrompt();
                    break;
                case "--approving-prompt" when prompt is null:
                    prompt = () => new Driver.ApprovingPrompt();
                    break;
                default:
                    return Task.FromResult(Usage());
            }
        }

        return driver.HoldAsync(vault, prompt, locked);
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
        WithEntriesAsync(vault, entries => Relocate(entries, entry, group, title));

    internal Task<int> DeleteAsync(string vault, string entry) =>
        WithEntriesAsync(vault, entries => Delete(entries, entry));

    private static int Relocate(EntriesViewModel entries, string entry, string group, string title)
    {
        Select(entries, entry);

        Press(entries.OrganizeCommand, "Organize");
        entries.DraftTitle = title;
        entries.MoveTarget = entries.MoveTargets.SingleOrDefault(node => node.Path == group)
            ?? throw new DriverException($"no group '{group}' to move to");
        Press(entries.ConfirmOrganizeCommand, "Move");
        return entries.IsOrganizing || entries.Error is not null ? Refused(entries.Error) : Did(entries.Notice);
    }

    private static int Delete(EntriesViewModel entries, string entry)
    {
        Select(entries, entry);

        Press(entries.DeleteCommand, "Delete");
        Press(entries.ConfirmDeleteCommand, "Move to trash");
        return entries.Error is not null ? Refused(entries.Error) : Did(entries.Notice);
    }

    /// <summary>Sets an entry's password to the new one, as the detail pane's edit is used.</summary>
    private int SetPassword(EntriesViewModel entries, string entry)
    {
        var detail = Select(entries, entry);

        Press(detail.EditCommand, "Edit");

        foreach (var c in _newPassword)
        {
            detail.NewPassword.Type(c);
        }

        Press(detail.SaveCommand, "Save");
        return detail.IsEditing || entries.Error is not null ? Refused(entries.Error) : Did($"saved {entry}");
    }

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

        return OnEntries(session, act);
    }

    private static int OnEntries(AppVaultSession session, Func<EntriesViewModel, int> act)
    {
        using var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);
        using var entries = new EntriesViewModel(session, countdown);
        return act(entries);
    }

    internal async Task<int> HoldAsync(string vault, Func<IApprovalChannel>? prompt, bool startLocked)
    {
        // The authority owns the session from here, and disposing it is quitting.
#pragma warning disable CA2000
        using var authority = new AppAuthority(
            new AppVaultSession(TimeProvider.System, AppVaultSession.MaximumIdleTimeout, home),
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            prompt);
#pragma warning restore CA2000
        var session = authority.Session;

        if (startLocked)
        {
            Report(authority);
        }
        else if (await HoldOnceAsync(authority, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        while (await Console.In.ReadLineAsync().ConfigureAwait(true) is { } command)
        {
            switch (command.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                case ["lock"]:
                    session.Lock(VaultLockReason.Manual);
                    Console.Out.WriteLine("locked");
                    Report(authority);
                    break;

                case ["unlock"]:
                    if (await HoldOnceAsync(authority, vault).ConfigureAwait(true) is { } again)
                    {
                        return again;
                    }

                    break;

                case ["edit", var entry]:
                    OnEntries(session, entries => SetPassword(entries, entry));
                    break;

                case ["delete", var entry]:
                    OnEntries(session, entries => Delete(entries, entry));
                    break;

                case ["relocate", var entry, var group, var title]:
                    OnEntries(session, entries => Relocate(entries, entry, group, title));
                    break;

                default:
                    throw new DriverException($"hold does not understand '{command}'");
            }
        }

        // Standard input closing is quitting, as the app quits.
        authority.Dispose();
        Console.Out.WriteLine("shut down");
        Report(authority);
        return 0;
    }

    /// <summary>A person in front of the prompt who approves whatever is asked.</summary>
    internal sealed class ApprovingPrompt : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Console.Out.WriteLine($"asking {prompt.Entry}");
            Console.Out.WriteLine("approved");
            return ValueTask.FromResult(ApprovalAnswer.Approved);
        }
    }

    /// <summary>A person in front of the prompt who never answers.</summary>
    internal sealed class HeldPrompt : IApprovalChannel
    {
        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Console.Out.WriteLine($"asking {prompt.Entry}");

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Out.WriteLine("withdrawn");
            }

            return ApprovalAnswer.Denied;
        }
    }

    private async Task<int?> HoldOnceAsync(AppAuthority authority, string vault)
    {
        var session = authority.Session;
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            if (unlock.HasOwner)
            {
                Console.Out.WriteLine($"owner: {unlock.Owner}");
            }

            Report(authority);
            return refused;
        }

        Console.Out.WriteLine(Report(authority) is AuthorityStatus.Serving serving
            ? $"holding session {serving.Session} as process {serving.Owner.ProcessId} on {serving.Endpoint}"
            : $"holding session {session.SessionId} as process {Environment.ProcessId}, not served");

        return null;
    }

    /// <summary>Prints what the authority says agents meet now.</summary>
    private static AuthorityStatus Report(AppAuthority authority)
    {
        var status = authority.Status;

        Console.Out.WriteLine(status switch
        {
            AuthorityStatus.Serving serving =>
                $"status serving {serving.Session} as process {serving.Owner.ProcessId} on {serving.Endpoint}",
            AuthorityStatus.HeldBy held => $"status held by {held.Owner.Describe()}",
            AuthorityStatus.NotServing notServing => $"status not serving: {notServing.Reason}",
            _ => "status locked",
        });

        return status;
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
