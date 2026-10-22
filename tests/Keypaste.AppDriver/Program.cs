using System.Collections.Concurrent;
using System.Globalization;
using Keypaste.App;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Recent;
using Keypaste.Mvvm;

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
/// <c>scripts/verify-current-state.sh</c> can change it between two real requests (U.3). With
/// neither, a request is put to the person in the app's own prompt window, as launch does, drawn on
/// a headless display: <c>prompt</c> prints what the window shows and <c>prompt withdrawn</c> when it
/// comes down, and the lines <c>approve</c> (the timed allow), <c>once</c>, <c>deny</c> and <c>close</c> click its buttons through
/// the display's hit-testing or close it, so <c>scripts/verify-desktop-approval.sh</c> can answer a
/// real request as a person would (4.4). With <c>--terminal-bundle</c> on macOS, the line
/// <c>terminal</c> saves a project's directory and command on the Env Sets screen and presses Run in
/// a terminal or Open terminal and then Start, so Terminal opens that <c>keypaste.app</c>'s launcher
/// and the run it starts asks this hold for the set, which <c>scripts/verify-run-session.sh</c>
/// watches (E.1d).
/// </para>
/// <para>
/// <c>launch</c> starts the app itself through <c>keypaste-app</c>'s own start, as <see cref="AppLaunch"/> says.
/// </para>
/// </remarks>
internal static class Program
{
    private const string _usage =
        "usage: open <vault>\n" +
        "       welcome [--keepassxc-config <ini>]\n" +
        "       open-offered <n> [--keepassxc-config <ini>]\n" +
        "       create <vault> [--new-keyfile <keyfile>]\n" +
        "       edit <vault> <entry-path> <notes> <url>\n" +
        "       item-new <vault> <login|apikey|database|server|securenote> <folder> <title> [--username u] [--url u] [--host h] [--key NAME] [--notes n] [--tag t]...\n" +
        "       field-add <vault> <entry-path> <name> <protected | plain>\n" +
        "       field-set <vault> <entry-path> <name>\n" +
        "       field-protect <vault> <entry-path> <name>\n" +
        "       field-rm <vault> <entry-path> <name>\n" +
        "       tag-add <vault> <entry-path> <tag> [--decline]\n" +
        "       tag-rm <vault> <entry-path> <tag> [--decline]\n" +
        "       env-add <vault> <project> <environment> <KEY> [--entry <entry-path>]\n" +
        "       env-set <vault> <project> <environment> <KEY> [--decline]\n" +
        "       env-rm <vault> <project> <environment> <KEY>\n" +
        "       env-import <vault> <project> <environment> <file>\n" +
        "       env-export <vault> <project> <environment> <file>\n" +
        "       env-entry-add <vault> <project> <environment> <entry-path> [--decline]\n" +
        "       env-entry-rm <vault> <project> <environment> <entry-path> [--decline]\n" +
        "       projects <vault>\n" +
        "       group-rename <vault> <group-path> <new-name>\n" +
        "       relocate <vault> <entry-path> <destination-group-path> <new-title>\n" +
        "       delete <vault> <entry-path>\n" +
        "       trash-restore <vault> <title>\n" +
        "       revision-restore <vault> <entry-path> <row | oldest>\n" +
        "       backup-restore <vault> <backup-file-name>\n" +
        "       export <vault> <destination>\n" +
        "       access <vault> [--password] [--attach <keyfile> | --new-keyfile <keyfile> | --remove-keyfile]\n" +
        "       notes-review <vault>\n" +
        "       notes-move <vault> <entry-path> [<key>...]\n" +
        "       notes-dismiss <vault> <entry-path> <key>\n" +
        "       notes-restore <vault> <entry-path> <key>\n" +
        "       tags-review <vault>\n" +
        "       tag-restore <vault> <entry-path> <tag>\n" +
        "       tags-dismiss <vault> <entry-path> <tag>\n" +
        "       tags-again <vault> <entry-path> <tag>\n" +
        "       hold <vault> [--locked] [--held-prompt | --approving-prompt] [--terminal-bundle <keypaste.app>]\n" +
        "            (then per line of standard input: lock, unlock, edit <entry-path>, delete <entry-path>,\n" +
        "             relocate <entry-path> <destination-group-path> <new-title>, and with the app's own\n" +
        "             prompt: approve, once, always, allow, deny, close, show-prompt; activity, revoke <n | all>;\n" +
        "             tools, connect <tool>, connect-remove <tool>, confirm, cancel, check, pick <n>; settings <label>,\n" +
        "             setting <label> mode|values|run <word>, setting <label> see <selector,...>,\n" +
        "             setting <label> row <scope>, setting <label> row-mode <scope> <word>, setting <label> use-vault;\n" +
        "             and on macOS with --terminal-bundle: terminal <run | open> <project> <directory> [command, the rest of the line])\n" +
        "       launch [--background]\n" +
        "            (runs the app as keypaste-app does, on a headless display, until standard input closes;\n" +
        "             per line of standard input: notice open, which presses a locked notice's Open keypaste once armed)\n" +
        "KEYPASTE_HOME must be set. KEYPASTE_DRIVER_PASSWORD is the password typed (empty for none),\n" +
        "KEYPASTE_DRIVER_KEYFILE the keyfile chosen, KEYPASTE_DRIVER_NEW_PASSWORD a new password, entry password or field value.";

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
                ["welcome"] => driver.Welcome(KeePassXcDatabases.LocalConfigPaths),
                ["welcome", "--keepassxc-config", var ini] => driver.Welcome(() => [ini]),
                ["open-offered", var row] => await driver.OpenOfferedAsync(row, KeePassXcDatabases.LocalConfigPaths).ConfigureAwait(true),
                ["open-offered", var row, "--keepassxc-config", var ini] => await driver.OpenOfferedAsync(row, () => [ini]).ConfigureAwait(true),
                ["create", var vault] => await driver.CreateAsync(vault).ConfigureAwait(true),
                ["create", var vault, "--new-keyfile", var keyfile] => await driver.CreateAsync(vault, keyfile).ConfigureAwait(true),
                ["edit", var vault, var entry, var notes, var url] => await driver.EditAsync(vault, entry, notes, url).ConfigureAwait(true),
                ["item-new", var vault, var template, var folder, var title, .. var options] => await driver.NewItemAsync(vault, template, folder, title, options).ConfigureAwait(true),
                ["field-add", var vault, var entry, var name, "protected"] => await driver.AddFieldAsync(vault, entry, name, protect: true).ConfigureAwait(true),
                ["field-add", var vault, var entry, var name, "plain"] => await driver.AddFieldAsync(vault, entry, name, protect: false).ConfigureAwait(true),
                ["field-set", var vault, var entry, var name] => await driver.ReplaceFieldAsync(vault, entry, name).ConfigureAwait(true),
                ["field-protect", var vault, var entry, var name] => await driver.ToggleProtectionAsync(vault, entry, name).ConfigureAwait(true),
                ["field-rm", var vault, var entry, var name] => await driver.RemoveFieldAsync(vault, entry, name).ConfigureAwait(true),
                ["tag-add", var vault, var entry, var tag] => await driver.AddTagAsync(vault, entry, tag, decline: false).ConfigureAwait(true),
                ["tag-add", var vault, var entry, var tag, "--decline"] => await driver.AddTagAsync(vault, entry, tag, decline: true).ConfigureAwait(true),
                ["tag-rm", var vault, var entry, var tag] => await driver.RemoveTagAsync(vault, entry, tag, decline: false).ConfigureAwait(true),
                ["tag-rm", var vault, var entry, var tag, "--decline"] => await driver.RemoveTagAsync(vault, entry, tag, decline: true).ConfigureAwait(true),
                ["env-add", var vault, var project, var environment, var key] => await driver.AddEnvKeyAsync(vault, project, environment, key, null).ConfigureAwait(true),
                ["env-add", var vault, var project, var environment, var key, "--entry", var entry] => await driver.AddEnvKeyAsync(vault, project, environment, key, entry).ConfigureAwait(true),
                ["env-set", var vault, var project, var environment, var key] => await driver.ReplaceEnvKeyAsync(vault, project, environment, key, decline: false).ConfigureAwait(true),
                ["env-set", var vault, var project, var environment, var key, "--decline"] => await driver.ReplaceEnvKeyAsync(vault, project, environment, key, decline: true).ConfigureAwait(true),
                ["env-rm", var vault, var project, var environment, var key] => await driver.RemoveEnvKeyAsync(vault, project, environment, key).ConfigureAwait(true),
                ["env-import", var vault, var project, var environment, var file] => await driver.ImportEnvAsync(vault, project, environment, file).ConfigureAwait(true),
                ["env-export", var vault, var project, var environment, var file] => await driver.ExportEnvAsync(vault, project, environment, file).ConfigureAwait(true),
                ["env-entry-add", var vault, var project, var environment, var entry] => await driver.AddEnvEntryAsync(vault, project, environment, entry, decline: false).ConfigureAwait(true),
                ["env-entry-add", var vault, var project, var environment, var entry, "--decline"] => await driver.AddEnvEntryAsync(vault, project, environment, entry, decline: true).ConfigureAwait(true),
                ["env-entry-rm", var vault, var project, var environment, var entry] => await driver.RemoveEnvEntryAsync(vault, project, environment, entry, decline: false).ConfigureAwait(true),
                ["env-entry-rm", var vault, var project, var environment, var entry, "--decline"] => await driver.RemoveEnvEntryAsync(vault, project, environment, entry, decline: true).ConfigureAwait(true),
                ["projects", var vault] => await driver.ListProjectsAsync(vault).ConfigureAwait(true),
                ["group-rename", var vault, var group, var name] => await driver.RenameGroupAsync(vault, group, name).ConfigureAwait(true),
                ["relocate", var vault, var entry, var group, var title] => await driver.RelocateAsync(vault, entry, group, title).ConfigureAwait(true),
                ["delete", var vault, var entry] => await driver.DeleteAsync(vault, entry).ConfigureAwait(true),
                ["trash-restore", var vault, var title] => await driver.RestoreRecycledAsync(vault, title).ConfigureAwait(true),
                ["revision-restore", var vault, var entry, var row] => await driver.RestoreRevisionAsync(vault, entry, row).ConfigureAwait(true),
                ["backup-restore", var vault, var backup] => await driver.RestoreBackupAsync(vault, backup).ConfigureAwait(true),
                ["export", var vault, var destination] => await driver.ExportAsync(vault, destination).ConfigureAwait(true),
                ["access", var vault, .. var change] => await driver.ChangeAccessAsync(vault, change).ConfigureAwait(true),
                ["notes-review", var vault] => await driver.ReviewNotesAsync(vault).ConfigureAwait(true),
                ["notes-move", var vault, var entry, .. var keys] => await driver.MoveNoteKeysAsync(vault, entry, keys).ConfigureAwait(true),
                ["notes-dismiss", var vault, var entry, var key] => await driver.DismissNoteKeyAsync(vault, entry, key).ConfigureAwait(true),
                ["notes-restore", var vault, var entry, var key] => await driver.ReviewNoteKeyAgainAsync(vault, entry, key).ConfigureAwait(true),
                ["tags-review", var vault] => await driver.ReviewLostTagsAsync(vault).ConfigureAwait(true),
                ["tag-restore", var vault, var entry, var tag] => await driver.RestoreLostTagAsync(vault, entry, tag).ConfigureAwait(true),
                ["tags-dismiss", var vault, var entry, var tag] => await driver.DismissLostTagAsync(vault, entry, tag).ConfigureAwait(true),
                ["tags-again", var vault, var entry, var tag] => await driver.ReviewLostTagAgainAsync(vault, entry, tag).ConfigureAwait(true),
                ["raw-add", var vault, var group, var title] => RawAdd(vault, group, title),
                ["hold", var vault, .. var options] => await HoldAsync(driver, vault, options).ConfigureAwait(true),
                ["launch"] => AppLaunch.Run(KeypasteHome.Resolve(home), []),
                ["launch", "--background"] => AppLaunch.Run(KeypasteHome.Resolve(home), ["--background"]),
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
            Console.Error.WriteLine($"driver failed: {ex}");
            return 3;
        }
    }

    private static async Task<int> HoldAsync(Driver driver, string vault, string[] options)
    {
        var locked = false;
        Func<IApprovalChannel>? prompt = null;
        string? terminalBundle = null;

        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
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
                case "--terminal-bundle" when terminalBundle is null && i + 1 < options.Length:
                    terminalBundle = Path.GetFullPath(options[++i]);
                    break;
                default:
                    return Usage();
            }
        }

        if (prompt is not null)
        {
            return await driver.HoldAsync(vault, prompt, locked, screen: null, terminalBundle).ConfigureAwait(true);
        }

        using var screen = new PromptScreen();
        return await driver.HoldAsync(vault, screen.Open, locked, screen, terminalBundle).ConfigureAwait(true);
    }

    // KeePassXC can store a name keypaste refuses to create, so this writes below the product's rules.
    private static int RawAdd(string vault, string group, string title)
    {
        var password = Environment.GetEnvironmentVariable("KEYPASTE_DRIVER_PASSWORD") ?? string.Empty;
        var value = Console.In.ReadLine() ?? string.Empty;

        using var v = Vault.Open(vault, password);
        v.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = value });
        v.Save();
        Console.WriteLine("added");
        return 0;
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

    /// <summary>Prints the databases the first run offers from KeePassXC, one path a line, as the welcome lists them (N.2).</summary>
    internal int Welcome(Func<IReadOnlyList<string>> keePassXcConfigs)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = new UnlockViewModel(session, home, _picker, () => { }, keePassXcConfigs: keePassXcConfigs);

        if (unlock.HasSelection)
        {
            throw new DriverException("a recent vault is selected, so the welcome is not showing");
        }

        foreach (var item in unlock.KeePassXc)
        {
            Console.Out.WriteLine(item.Path);
        }

        return 0;
    }

    /// <summary>Chooses the welcome's <paramref name="row"/>th database from KeePassXC, counting from 1, and unlocks it.</summary>
    internal async Task<int> OpenOfferedAsync(string row, Func<IReadOnlyList<string>> keePassXcConfigs)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = new UnlockViewModel(session, home, _picker, () => { }, keePassXcConfigs: keePassXcConfigs);

        if (!int.TryParse(row, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > unlock.KeePassXc.Count)
        {
            throw new DriverException($"the welcome offers {unlock.KeePassXc.Count} databases from KeePassXC, not a row {row}");
        }

        var chosen = unlock.KeePassXc[n - 1].Path;
        unlock.SelectedKeePassXc = unlock.KeePassXc[n - 1];

        if (unlock.SelectedPath != chosen)
        {
            return Refused(unlock.Message);
        }

        return await UnlockAsync(unlock, session, chosen).ConfigureAwait(true) ?? Did($"opened {chosen}");
    }

    /// <summary>Creates a vault through the unlock screen's create form, with a keyfile the form makes at <paramref name="newKeyfile"/> when one is named.</summary>
    internal async Task<int> CreateAsync(string vault, string? newKeyfile = null)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        _picker.NewPath = vault;
        await PressAsync(unlock.StartCreateCommand, "Create").ConfigureAwait(true);

        if (!unlock.IsCreating)
        {
            return Refused(unlock.Message);
        }

        if (newKeyfile is not null)
        {
            _picker.NewKeyfilePath = newKeyfile;
            await PressAsync(unlock.CreateKeyfileCommand, "Create a key file").ConfigureAwait(true);

            if (!unlock.HasKeyfile)
            {
                return Refused(unlock.Message);
            }
        }
        else if (await ChooseKeyfileAsync(unlock).ConfigureAwait(true) is { } refused)
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

    /// <summary>
    /// Makes an item from a template through New item (N.4): the template, the title, the folder
    /// chosen from the picker, the template's plain fields, tags and notes from <paramref name="options"/>,
    /// and the one secret, a password or a key's value, typed from <c>KEYPASTE_DRIVER_NEW_PASSWORD</c>.
    /// </summary>
    internal Task<int> NewItemAsync(string vault, string template, string folder, string title, string[] options) =>
        WithEntriesAsync(vault, entries =>
        {
            Press(entries.BeginAddCommand, "New item");
            var form = entries.NewItem!;
            form.SelectedTemplate = form.Templates.SingleOrDefault(choice => string.Equals(choice.Template.ToString(), template, StringComparison.OrdinalIgnoreCase))
                ?? throw new DriverException($"there is no template '{template}'");
            form.Title = title;
            form.Folder = form.Folders.SingleOrDefault(choice => choice.Path == folder)
                ?? throw new DriverException($"the folder picker offers no '{folder}'");

            for (var i = 0; i + 1 < options.Length; i += 2)
            {
                var value = options[i + 1];

                switch (options[i])
                {
                    case "--username": form.Username = value; break;
                    case "--url": form.Url = value; break;
                    case "--host": form.Host = value; break;
                    case "--key": form.KeyName = value; break;
                    case "--notes": form.Notes = value; break;
                    case "--tag":
                        form.DraftTag = value;
                        Press(form.AddTagCommand, "Add tag");
                        break;
                    default: throw new DriverException($"item-new takes no '{options[i]}'");
                }
            }

            if (form.ShowsKey)
            {
                Type(form.KeyValue);
            }
            else if (form.ShowsPassword && _newPassword.Length > 0)
            {
                form.GeneratePassword = false;
                Type(form.Password);
            }

            Press(entries.ConfirmAddCommand, "Create");
            return entries.IsAdding ? Refused(form.Error) : Did($"created {folder}/{title}");
        });

    internal Task<int> AddFieldAsync(string vault, string entry, string name, bool protect) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            Press(detail.BeginAddFieldCommand, "Add field");
            detail.DraftFieldName = name;
            detail.NewFieldProtected = protect;
            Type(detail.NewFieldValue);

            Press(detail.ConfirmAddFieldCommand, "Add");
            return detail.IsAddingField || entries.Error is not null ? Refused(entries.Error) : Did($"added {name} to {entry}");
        });

    internal Task<int> ReplaceFieldAsync(string vault, string entry, string name) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            Press(Field(detail, name).ReplaceCommand, "Replace");
            Type(detail.ReplacementFieldValue);

            Press(detail.ConfirmReplaceFieldCommand, "Replace");
            return detail.IsReplacingField || entries.Error is not null ? Refused(entries.Error) : Did($"replaced {name} on {entry}");
        });

    internal Task<int> ToggleProtectionAsync(string vault, string entry, string name) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            Press(Field(detail, name).ToggleProtectionCommand, "Protection");
            return entries.Error is not null ? Refused(entries.Error) : Did($"switched the protection of {name} on {entry}");
        });

    internal Task<int> RemoveFieldAsync(string vault, string entry, string name) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            Press(Field(detail, name).RemoveCommand, "Remove");
            Press(detail.ConfirmRemoveFieldCommand, "Remove");
            return detail.IsRemovingField || entries.Error is not null ? Refused(entries.Error) : Did($"removed {name} from {entry}");
        });

    internal Task<int> AddTagAsync(string vault, string entry, string tag, bool decline) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);

            detail.DraftTag = tag;
            Press(detail.AddTagCommand, "Add tag");
            return Answer(entries, detail, decline) ?? (entries.Error is not null ? Refused(entries.Error) : Did($"tagged {entry} {tag}"));
        });

    internal Task<int> RemoveTagAsync(string vault, string entry, string tag, bool decline) =>
        WithEntriesAsync(vault, entries =>
        {
            var detail = Select(entries, entry);
            var chip = detail.Tags.SingleOrDefault(chip => chip.Tag == tag)
                ?? throw new DriverException($"'{entry}' has no chip for the tag '{tag}'");

            Press(chip.RemoveCommand, "Remove tag");
            return Answer(entries, detail, decline) ?? (entries.Error is not null ? Refused(entries.Error) : Did($"untagged {entry} {tag}"));
        });

    /// <summary>Prints what a project tag change says it reaches, then confirms or cancels it; null when nothing was asked.</summary>
    private static int? Answer(EntriesViewModel entries, EntryDetailViewModel detail, bool decline)
    {
        if (!detail.IsConfirmingTagChange)
        {
            return decline ? Refused("nothing was asked, so there was nothing to decline") : null;
        }

        foreach (var line in detail.TagChangeLines)
        {
            Console.Out.WriteLine($"asked: {line}");
        }

        if (decline)
        {
            Press(detail.CancelTagChangeCommand, "Cancel");
            return Did("declined");
        }

        Press(detail.ConfirmTagChangeCommand, detail.TagChangeAction);
        return entries.Error is not null ? Refused(entries.Error) : null;
    }

    internal Task<int> AddEnvKeyAsync(string vault, string project, string environment, string key, string? entry) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            Press(open.BeginAddCommand, "Add a key");
            open.NewKey = key;

            if (entry is not null)
            {
                open.NewEntry = open.EntryChoices.SingleOrDefault(choice => choice.Entry is { } named && ApprovalPrompt.Shown(named) == entry)
                    ?? throw new DriverException($"'{entry}' is not offered for a new key in {project}/{environment}");
            }

            open.GenerateValue = false;
            Type(open.NewValue);
            Press(open.ConfirmAddCommand, "Add");
            return open.IsAdding || screen.Error is not null ? Refused(screen.Error) : Did($"added {key} to {project}/{environment}");
        });

    internal Task<int> ReplaceEnvKeyAsync(string vault, string project, string environment, string key, bool decline) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            Press(Variable(open, key).ReplaceCommand, "Replace");
            Console.Out.WriteLine($"asked: {open.ReplacePrompt}");

            if (decline)
            {
                Press(open.CancelReplaceCommand, "Cancel");
                return Did("declined");
            }

            Type(open.ReplacementValue);
            Press(open.ConfirmReplaceCommand, "Replace");
            return open.IsReplacing || screen.Error is not null ? Refused(screen.Error) : Did($"replaced {key} in {project}/{environment}");
        });

    internal Task<int> ExportEnvAsync(string vault, string project, string environment, string file) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            var said = open.ExportReferences(Path.GetFullPath(file), replace: true);
            return said.StartsWith("Wrote", StringComparison.Ordinal) ? Did(said) : Refused(said);
        });

    internal Task<int> AddEnvEntryAsync(string vault, string project, string environment, string entry, bool decline) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            Press(Listed(open, environment).AddEntry, "Add item");
            open.EntryFilter = entry;
            open.ChosenEntry = open.EntryCandidates.SingleOrDefault(candidate => candidate.Display == entry)
                ?? throw new DriverException($"'{entry}' is not offered to add to {project}/{environment}");
            return AnswerTagChange(screen, open, decline) ?? Did($"added {entry} to {project}/{environment}");
        });

    internal Task<int> RemoveEnvEntryAsync(string vault, string project, string environment, string entry, bool decline) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            var member = Listed(open, environment).Members.SingleOrDefault(row => row.Display == entry)
                ?? throw new DriverException($"'{entry}' is not listed in {project}/{environment}");
            Press(member.Remove, "Remove");
            return AnswerTagChange(screen, open, decline) ?? Did($"removed {entry} from {project}/{environment}");
        });

    /// <summary>Prints what the Projects screen lists for every project: environments, entries, where each value lives, and the tags it ignores.</summary>
    internal Task<int> ListProjectsAsync(string vault) =>
        WithProjectsAsync(vault, screen =>
        {
            foreach (var problem in screen.MalformedTags)
            {
                Console.Out.WriteLine($"ignored: {problem.Sentence}");
            }

            foreach (var project in screen.Projects)
            {
                screen.OpenCommand.Execute(project);
                var open = screen.OpenProject ?? throw new DriverException($"'{project}' did not open: {screen.Error}");
                Console.Out.WriteLine($"project {open.DisplayName}{(open.Launch.Mapping is { } mapping ? $" runs {mapping.Command} in {mapping.Directory}" : string.Empty)}");

                foreach (var environment in open.Environments)
                {
                    foreach (var member in environment.Members)
                    {
                        Console.Out.WriteLine($"entry {open.DisplayName}/{environment.DisplayName}: {member.Display} holds {member.KeysText}{(member.HasAlso ? $", {member.AlsoText}" : string.Empty)}");
                    }
                }

                foreach (var row in open.Rows)
                {
                    foreach (var cell in row.Cells.Where(cell => cell.HasSourceNote))
                    {
                        Console.Out.WriteLine($"value {open.DisplayName}/{cell.Profile} {row.DisplayKey}: {cell.SourceNote}");
                    }
                }
            }

            return 0;
        });

    private static EnvEnvironmentEntries Listed(EnvProjectViewModel open, string environment) =>
        open.Environments.SingleOrDefault(listed => listed.Name == environment)
            ?? throw new DriverException($"{open.Name} lists no environment '{environment}'");

    /// <summary>Prints what a tag change on the Projects screen says it reaches, then confirms or cancels it.</summary>
    private static int? AnswerTagChange(EnvSetsViewModel screen, EnvProjectViewModel open, bool decline)
    {
        if (!open.IsConfirmingTagChange)
        {
            return Refused(screen.Error ?? "nothing was asked");
        }

        foreach (var line in open.TagChangeLines)
        {
            Console.Out.WriteLine($"asked: {line}");
        }

        if (decline)
        {
            Press(open.CancelTagChangeCommand, open.TagChangeCancel);
            return Did("declined");
        }

        Press(open.ConfirmTagChangeCommand, open.TagChangeAction);
        return open.IsConfirmingTagChange || screen.Error is not null ? Refused(screen.Error) : null;
    }

    internal Task<int> RemoveEnvKeyAsync(string vault, string project, string environment, string key) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            Press(Variable(open, key).RemoveCommand, "Remove");
            Press(open.ConfirmRemoveCommand, "Remove");
            return open.IsRemoving || screen.Error is not null ? Refused(screen.Error) : Did($"removed {key} from {project}/{environment}");
        });

    internal Task<int> ImportEnvAsync(string vault, string project, string environment, string file) =>
        WithProjectAsync(vault, project, environment, (screen, open) =>
        {
            open.Import.Preview(Path.GetFullPath(file));

            if (!open.Import.IsPreviewing)
            {
                return Refused(screen.Error);
            }

            foreach (var row in open.Import.Rows)
            {
                Console.Out.WriteLine($"previewed: {row}");
            }

            Press(open.Import.ConfirmCommand, "Import");
            return screen.Error is not null ? Refused(screen.Error) : Did($"imported {file} into {project}/{environment}");
        });

    private static EnvVariableRow Variable(EnvProjectViewModel open, string key) =>
        open.Variables.SingleOrDefault(row => row.Key == key)
            ?? throw new DriverException($"no single '{key}' in {open.Name}/{open.SelectedProfile}");

    /// <summary>Unlocks the vault and opens one environment of a project on the Projects screen.</summary>
    private Task<int> WithProjectAsync(string vault, string project, string environment, Func<EnvSetsViewModel, EnvProjectViewModel, int> act) =>
        WithProjectsAsync(vault, screen =>
        {
            screen.OpenCommand.Execute(project);

            var open = screen.OpenProject ?? throw new DriverException($"'{project}' did not open: {screen.Error}");
            open.SelectedProfile = environment;
            return act(screen, open);
        });

    /// <summary>Unlocks the vault and shows the Projects screen.</summary>
    private async Task<int> WithProjectsAsync(string vault, Func<EnvSetsViewModel, int> act)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);
        using var screen = new EnvSetsViewModel(session, countdown, _picker);
        return act(screen);
    }

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

    /// <summary>Lists the keys Settings › Recommendations found in notes on unlock, and how many need review: entry, key, kind and state, never a value.</summary>
    internal Task<int> ReviewNotesAsync(string vault) =>
        WithRecommendationsAsync(vault, list =>
        {
            foreach (var row in list.Rows)
            {
                var kind = row.IsToken ? "token" : "assignment";
                var state = row.IsDismissed ? "dismissed" : "needs-review";
                Console.Out.WriteLine($"finding entry={row.Entry} key={row.Key} kind={kind} state={state}");
            }

            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"count {list.Rows.Count(row => row.NeedsReview)}"));
            return 0;
        });

    /// <summary>Selects one entry's findings, or the named keys of it, and presses Move selected.</summary>
    internal Task<int> MoveNoteKeysAsync(string vault, string entry, string[] keys) =>
        WithRecommendationsAsync(vault, list =>
        {
            var rows = list.Rows.Where(row => row.Entry == entry && row.NeedsReview && (keys.Length == 0 || keys.Contains(row.Key))).ToList();

            if (rows.Count == 0)
            {
                throw new DriverException($"no finding needing review on '{entry}'");
            }

            foreach (var row in rows)
            {
                row.IsSelected = true;
            }

            Press(list.MoveSelectedCommand, "Move selected");
            return list.Message.StartsWith("Moved ", StringComparison.Ordinal) ? Did(list.Message) : Refused(list.Message);
        });

    /// <summary>Presses Dismiss on one finding.</summary>
    internal Task<int> DismissNoteKeyAsync(string vault, string entry, string key) =>
        WithRecommendationsAsync(vault, list =>
        {
            var row = list.Rows.SingleOrDefault(row => row.Entry == entry && row.Key == key && row.NeedsReview)
                ?? throw new DriverException($"no finding for {key} on '{entry}'");

            Press(row.DismissCommand, "Dismiss");
            return list.Message.Length == 0 ? Did("dismissed") : Refused(list.Message);
        });

    /// <summary>Presses Review again on one dismissed finding.</summary>
    internal Task<int> ReviewNoteKeyAgainAsync(string vault, string entry, string key) =>
        WithRecommendationsAsync(vault, list =>
        {
            var row = list.Rows.SingleOrDefault(row => row.Entry == entry && row.Key == key && row.IsDismissed)
                ?? throw new DriverException($"no dismissed finding for {key} on '{entry}'");

            Press(row.ReviewAgainCommand, "Review again");
            return list.Message.Length == 0 ? Did("restored") : Refused(list.Message);
        });

    /// <summary>Lists the project tags Settings › Recommendations found dropped, and how many need review: entry, tag and state, never a value (V.11).</summary>
    internal Task<int> ReviewLostTagsAsync(string vault) =>
        WithRecommendationsAsync(vault, list =>
        {
            foreach (var row in list.LostTags)
            {
                var state = row.IsDismissed ? "dismissed" : "needs-review";
                Console.Out.WriteLine($"lost entry={row.Entry} tag={row.Tag} state={state}");
            }

            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"count {list.LostTags.Count(row => row.NeedsReview)}"));
            return 0;
        });

    /// <summary>Presses Restore tag on one finding, prints what it was asked, and confirms.</summary>
    internal Task<int> RestoreLostTagAsync(string vault, string entry, string tag) =>
        WithRecommendationsAsync(vault, list =>
        {
            var row = list.LostTags.SingleOrDefault(row => row.Entry == entry && row.Tag == tag && row.NeedsReview)
                ?? throw new DriverException($"no dropped {tag} needing review on '{entry}'");

            Press(row.RestoreCommand, "Restore tag");

            foreach (var line in row.Confirmation)
            {
                Console.Out.WriteLine($"asked {line}");
            }

            Press(row.ConfirmRestoreCommand, "Add tag");
            return list.Message.StartsWith("Put ", StringComparison.Ordinal) ? Did(list.Message) : Refused(list.Message);
        });

    /// <summary>Presses Dismiss on one finding.</summary>
    internal Task<int> DismissLostTagAsync(string vault, string entry, string tag) =>
        WithRecommendationsAsync(vault, list =>
        {
            var row = list.LostTags.SingleOrDefault(row => row.Entry == entry && row.Tag == tag && row.NeedsReview)
                ?? throw new DriverException($"no dropped {tag} needing review on '{entry}'");

            Press(row.DismissCommand, "Dismiss");
            return list.Message.Length == 0 ? Did("dismissed") : Refused(list.Message);
        });

    /// <summary>Presses Review again on one dismissed finding.</summary>
    internal Task<int> ReviewLostTagAgainAsync(string vault, string entry, string tag) =>
        WithRecommendationsAsync(vault, list =>
        {
            var row = list.LostTags.SingleOrDefault(row => row.Entry == entry && row.Tag == tag && row.IsDismissed)
                ?? throw new DriverException($"no dismissed {tag} on '{entry}'");

            Press(row.ReviewAgainCommand, "Review again");
            return list.Message.Length == 0 ? Did("restored") : Refused(list.Message);
        });

    private async Task<int> WithRecommendationsAsync(string vault, Func<RecommendationsViewModel, int> act)
    {
        using var session = new AppVaultSession(TimeProvider.System, home: home);
        using var unlock = Screen(session);

        if (await UnlockAsync(unlock, session, vault).ConfigureAwait(true) is { } refused)
        {
            return refused;
        }

        using var list = new RecommendationsViewModel(session, home);
        await list.Checking.ConfigureAwait(true);
        return act(list);
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

                case "--new-keyfile" when i + 1 < change.Length:
                    _picker.NewKeyfilePath = change[++i];
                    await PressAsync(access.CreateKeyfileCommand, "Create keyfile").ConfigureAwait(true);

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

    /// <summary>Does something through the app's shell over the held session, as launch composes it.</summary>
    private void OnShell(AppVaultSession session, AppAuthority authority, Action<ShellViewModel> act)
    {
        using var shell = new ShellViewModel(session, home, authority);
        act(shell);
    }

    private static void Post(SynchronizationContext? context, Action action)
    {
        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(_ => action(), null);
        }
    }

    internal async Task<int> HoldAsync(string vault, Func<IApprovalChannel> prompt, bool startLocked, PromptScreen? screen, string? terminalBundle)
    {
        // The authority owns the session from here, and disposing it is quitting.
#pragma warning disable CA2000
        var session = new AppVaultSession(TimeProvider.System, AppVaultSession.MaximumIdleTimeout, home);
        var context = SynchronizationContext.Current;
        using var authority = new AppAuthority(
            session,
            Environment.GetEnvironmentVariable(ApproverEndpoint.EnvironmentVariable),
            prompt,
            // `keypaste lock` locks it as launch composes the app, started from the thread this hold runs on.
            AppAuthority.RequestLock(session, action => Post(context, () =>
            {
                action();
                Console.Out.WriteLine("locked");
            })));
#pragma warning restore CA2000
        using var agents = new AgentsScreen(this, authority);

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
                    agents.Close();
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

                case ["notice"]:
                    OnShell(session, authority, shell =>
                        Console.Out.WriteLine(shell.HasVaultChanged ? $"notice {shell.VaultChanged}" : "notice none"));
                    break;

                case ["reload"]:
                    OnShell(session, authority, shell =>
                    {
                        shell.ReloadCommand.Execute(null);
                        Console.Out.WriteLine(shell.HasVaultChanged ? $"not reloaded: {shell.VaultChanged}" : "reloaded");
                    });
                    break;

                case ["activity"]:
                    Activity(authority);
                    break;

                case ["revoke", var which]:
                    Revoke(authority, which);
                    break;

                case ["show-prompt"]:
                    Console.Out.WriteLine(await (screen ?? throw new DriverException("show-prompt needs the app's own prompt"))
                        .RaiseAsync(() => ShowPrompt(authority)).ConfigureAwait(true));
                    break;

                case ["tools"]:
                    await agents.ToolsAsync().ConfigureAwait(true);
                    break;

                case ["connect", var tool]:
                    await agents.ConnectAsync(tool).ConfigureAwait(true);
                    break;

                case ["connect-remove", var tool]:
                    await agents.DisconnectAsync(tool).ConfigureAwait(true);
                    break;

                case ["confirm"]:
                    await agents.ConfirmAsync().ConfigureAwait(true);
                    break;

                case ["cancel"]:
                    await agents.CancelAsync().ConfigureAwait(true);
                    break;

                case ["check"]:
                    await agents.CheckAsync().ConfigureAwait(true);
                    break;

                case ["pick", var which]:
                    await agents.PickAsync(which).ConfigureAwait(true);
                    break;

                case ["settings", var label]:
                    await agents.ShowSettingsAsync(label).ConfigureAwait(true);
                    break;

                case ["setting", var label, .. var change]:
                    await agents.ChangeAsync(label, change).ConfigureAwait(true);
                    break;

                case [("approve" or "once" or "always" or "allow" or "deny" or "close") and var answer]:
                    await (screen ?? throw new DriverException("hold answers a prompt only with the app's own"))
                        .AnswerAsync(answer).ConfigureAwait(true);
                    break;

                case ["terminal", ("run" or "open") and var kind, var project, var directory, .. var rest]:
                    await TerminalAsync(session, authority, terminalBundle, kind == "run", project, directory, rest.Length == 0 ? null : command.Split(' ', 5)[4])
                        .ConfigureAwait(true);
                    break;

                default:
                    throw new DriverException($"hold does not understand '{command}'");
            }
        }

        // Standard input closing is quitting, as the app quits.
        agents.Close();
        authority.Dispose();
        Console.Out.WriteLine("shut down");
        Report(authority);
        return 0;
    }

    /// <summary>Prints what Agents shows, read by its own view model from the authority and the audit file.</summary>
    private void Activity(AppAuthority authority)
    {
        using var model = OpenActivity(authority);

        if (model.IsUnavailable)
        {
            Console.Out.WriteLine($"unavailable {model.Unavailable}");
        }

        foreach (var row in model.Waiting)
        {
            Console.Out.WriteLine($"waiting n={row.Number} {Describe(row)}");
        }

        foreach (var row in model.Grants)
        {
            Console.Out.WriteLine($"grant n={row.Number} {Describe(row)}");
        }

        if (model.HasHistoryMessage)
        {
            Console.Out.WriteLine($"history-message {model.HistoryMessage}");
        }

        if (model.HasHistory)
        {
            Console.Out.WriteLine($"history-heading {model.HistoryHeading}");
        }

        foreach (var line in model.History.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Console.Out.WriteLine($"history {line}");
        }

        Console.Out.WriteLine("activity end");
    }

    /// <summary>Presses End on one listed allowance, or End all.</summary>
    private void Revoke(AppAuthority authority, string which)
    {
        using var model = OpenActivity(authority);

        if (which == "all")
        {
            Press(model.EndAllCommand, "End all");
        }
        else
        {
            var row = model.Grants.SingleOrDefault(grant => grant.Number.ToString(CultureInfo.InvariantCulture) == which)
                ?? throw new DriverException($"Agents lists no allowance {which}");
            model.EndCommand.Execute(row);
        }

        Console.Out.WriteLine($"revoked {which}, {model.Grants.Count} left");
    }

    /// <summary>Presses Show the prompt on Agents, on the prompt's display thread, as the person's click does.</summary>
    private void ShowPrompt(AppAuthority authority)
    {
        using var model = OpenActivity(authority);
        Press(model.ShowPromptCommand, "Show the prompt");
    }

    /// <summary>
    /// Saves a project's directory and command, or keeps the saved command when none is given, on the
    /// Env Sets screen, presses Run in a terminal or Open terminal and then Start, and prints what the
    /// card asked and what the screen said.
    /// </summary>
    private async Task TerminalAsync(AppVaultSession session, AppAuthority authority, string? bundle, bool run, string project, string directory, string? command)
    {
        if (bundle is null || !OperatingSystem.IsMacOS())
        {
            throw new DriverException("hold opens Terminal only on macOS, with --terminal-bundle");
        }

        using var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);
        using var screen = new EnvSetsViewModel(session, countdown, _picker, ProjectLaunching.ForMacBundle(authority, bundle));

        screen.OpenCommand.Execute(project);
        var launch = (screen.OpenProject ?? throw new DriverException($"'{project}' did not open: {screen.Error}")).Launch;

        launch.Directory = directory;
        launch.Command = command ?? launch.Command;
        Press(launch.SaveCommand, "Save");

        var button = run ? launch.RunProjectCommand : launch.OpenTerminalCommand;

        if (screen.Error is not null || !button.CanExecute(null))
        {
            Console.Out.WriteLine($"terminal refused: {screen.Error ?? launch.UnsupportedReason ?? "the button is disabled"}");
            return;
        }

        var launching = button.ExecuteAsync();

        if (launch.IsConfirming)
        {
            Console.Out.WriteLine(
                $"terminal-card title={launch.ConfirmTitle} starts={launch.ConfirmStarts} directory={launch.ConfirmDirectory} " +
                $"keys={launch.ConfirmKeys} sources={launch.ConfirmSources}");
            Press(launch.ConfirmLaunchCommand, "Start");
        }

        await launching.ConfigureAwait(true);
        Console.Out.WriteLine(screen.Error is { } error ? $"terminal refused: {error}" : $"terminal launched: {screen.Notice}");
    }

    // Its ticks are posted nowhere and it looks for no tools: each command reads once, and a timer thread
    // must not rewrite the lists while they are printed.
    private AgentActivityViewModel OpenActivity(AppAuthority authority) => new(authority, home, TimeProvider.System, _ => { });

    // The screen a person keeps open: its ticks and what a look finds are posted to a queue this driver
    // applies before each command, as the window's loop would, and it looks for the tools on this machine.
    private AgentActivityViewModel OpenAgents(AppAuthority authority, Action<Action> post) =>
        new(authority, home, TimeProvider.System, post, ClientConnector.ForThisProcess());

    /// <summary>
    /// The Agents screen, kept open across lines as the screen stays open and closed by a lock as the shell
    /// closes it: the AI tools it finds on this machine, a tool's Connect pane and an agent's settings.
    /// </summary>
    /// <remarks>
    /// Connecting uses the programs the app would: each tool's own command on PATH, and the
    /// <c>keypaste mcp</c> the app finds. What the pane says is printed as it changes, so the check can be
    /// started on one line and its prompt answered on the next. A change to an agent's settings is made the
    /// same way, so the confirmation it opens can be answered on the next line.
    /// </remarks>
    private sealed class AgentsScreen(Driver driver, AppAuthority authority) : IDisposable
    {
        private readonly ConcurrentQueue<Action> _posted = new();
        private AgentActivityViewModel? _screen;
        private ConnectClientViewModel? _watched;

        /// <summary>Waits for the look in progress and applies what the screen posted, as its window's loop would.</summary>
        private async Task<AgentActivityViewModel> SettledAsync()
        {
            var screen = _screen ??= driver.OpenAgents(authority, _posted.Enqueue);
            await screen.Looking.ConfigureAwait(true);

            while (_posted.TryDequeue(out var posted))
            {
                posted();
            }

            return screen;
        }

        /// <summary>Prints each row once the look has finished: a found tool by its id and state, any other by its key.</summary>
        internal async Task ToolsAsync()
        {
            var screen = await SettledAsync().ConfigureAwait(true);

            foreach (var row in screen.Rows)
            {
                Console.Out.WriteLine(row is { Tool: not null, Presence: { } presence }
                    ? $"tool id={row.Key} state={State(presence.State)}"
                    : $"agent key={row.Key}");
            }

            Console.Out.WriteLine("tools end");
        }

        /// <summary>Presses a tool's Connect, or Connect again under its Settings when it is connected, and prints the plan.</summary>
        internal async Task ConnectAsync(string tool)
        {
            var screen = await SettledAsync().ConfigureAwait(true);
            var row = Row(screen, tool);

            if (screen.ConnectCommand.CanExecute(row))
            {
                screen.ConnectCommand.Execute(row);
            }
            else if (screen.ConnectAgainCommand.CanExecute(row))
            {
                row.IsOpen = true;
                screen.ConnectAgainCommand.Execute(row);
            }
            else
            {
                throw new DriverException($"{tool}'s row offers no Connect");
            }

            await ShownAsync(screen).ConfigureAwait(true);
        }

        /// <summary>Opens a tool's Settings, presses Disconnect, and prints the plan.</summary>
        internal async Task DisconnectAsync(string tool)
        {
            var screen = await SettledAsync().ConfigureAwait(true);
            var row = Row(screen, tool);
            row.IsOpen = true;

            if (!screen.DisconnectCommand.CanExecute(row))
            {
                throw new DriverException($"{tool}'s Settings offer no Disconnect");
            }

            screen.DisconnectCommand.Execute(row);
            await ShownAsync(screen).ConfigureAwait(true);
        }

        internal async Task ConfirmAsync()
        {
            var model = await PaneAsync().ConfigureAwait(true);
            await PressAsync(model.ConfirmCommand, "Run it").ConfigureAwait(true);
            Console.Out.WriteLine($"message {model.Message}");
        }

        internal async Task CancelAsync()
        {
            var model = await PaneAsync().ConfigureAwait(true);
            Press(model.CancelCommand, "Cancel");
            Console.Out.WriteLine($"message {model.Message}");
        }

        /// <summary>Starts the check and returns; what it says arrives as it goes, ending with <c>check end</c>.</summary>
        internal async Task CheckAsync() => Run((await PaneAsync().ConfigureAwait(true)).CheckCommand, "Check the connection");

        internal async Task PickAsync(string which)
        {
            var model = await PaneAsync().ConfigureAwait(true);
            var index = int.Parse(which, CultureInfo.InvariantCulture) - 1;
            model.SelectedCheckEntry = index >= 0 && index < model.CheckEntries.Count
                ? model.CheckEntries[index]
                : throw new DriverException($"the check lists no item {which}");

            Run(model.AskCommand, "Ask for it");
        }

        /// <summary>Opens a row's Settings, as pressing its disclosure does, and prints what they hold.</summary>
        internal async Task ShowSettingsAsync(string label)
        {
            var settings = await SettingsAsync(label).ConfigureAwait(true);
            settings.Read();
            string Chosen(IReadOnlyList<SettingChoice> choices) => choices.FirstOrDefault(choice => choice.IsChosen)?.Word ?? "none";

            Console.Out.WriteLine(
                $"settings agent={settings.Agent} mode={Chosen(settings.ModeChoices)} values={Chosen(settings.ValueChoices)} " +
                $"run={Chosen(settings.RunChoices)} see={settings.SeePattern} " +
                $"rows={string.Join(',', settings.Rows.Select(row => $"{row.Name}:{Chosen(row.Modes)}"))} " +
                $"held={(settings.IsHeldBack ? settings.HeldBack : "none")}" +
                (settings.HasProblem ? $" problem={settings.Problem}" : string.Empty));
        }

        /// <summary>Starts one change on a row's settings and returns; <c>setting-done</c> and what the panel then says follow once it has its answer.</summary>
        internal async Task ChangeAsync(string label, string[] change)
        {
            var settings = await SettingsAsync(label).ConfigureAwait(true);
            var changing = change switch
            {
                [("mode" or "values" or "run") and var setting, var word] => settings.ChooseAsync(Offered(settings, setting, word, null)),
                ["see", var selectors] => See(settings, selectors),
                ["row", var scope] => AddRow(settings, scope),
                ["row-mode", var scope, var word] => settings.ChooseAsync(Offered(settings, "mode", word, scope)),
                ["use-vault"] => settings.UseVaultAsync(),
                _ => throw new DriverException($"setting does not understand '{string.Join(' ', change)}'"),
            };

            _ = changing.ContinueWith(_ => Console.Out.WriteLine($"setting-done {settings.Message}"), TaskScheduler.Default);
        }

        private static Task See(AgentSettingsViewModel settings, string selectors)
        {
            settings.SeePattern = selectors;
            return settings.ApplySeeAsync();
        }

        private static Task AddRow(AgentSettingsViewModel settings, string scope)
        {
            settings.NewRowScope = settings.NewRowScopes.Contains(scope, StringComparer.Ordinal)
                ? scope
                : throw new DriverException($"the settings offer no new row for {scope}");
            return settings.AddRowAsync();
        }

        /// <summary>The settings open on a row, opened from its disclosure if they are not.</summary>
        private async Task<AgentSettingsViewModel> SettingsAsync(string label)
        {
            var screen = await SettledAsync().ConfigureAwait(true);

            if (screen.Rows.FirstOrDefault(row => string.Equals(row.Label, label, StringComparison.Ordinal)) is { Settings: { } open })
            {
                return open;
            }

            // Read again, as the screen does each second, so an agent that asked since it opened has its row.
            screen.Refresh();
            var row = screen.Rows.FirstOrDefault(row => string.Equals(row.Label, label, StringComparison.Ordinal))
                ?? throw new DriverException($"Agents shows no row for {label}");

            if (!row.HasSettings)
            {
                throw new DriverException($"{label}'s row has no settings");
            }

            row.IsOpen = true;
            return row.Settings ?? throw new DriverException($"{label}'s settings did not open");
        }

        private static SettingChoice Offered(AgentSettingsViewModel settings, string setting, string word, string? row)
        {
            var choices = row is null
                ? setting switch
                {
                    "mode" => settings.ModeChoices,
                    "values" => settings.ValueChoices,
                    _ => settings.RunChoices,
                }
                : settings.Rows.FirstOrDefault(listed => string.Equals(listed.Name, row, StringComparison.Ordinal))?.Modes
                    ?? throw new DriverException($"the settings have no row for {row}");

            return choices.FirstOrDefault(choice => string.Equals(choice.Word, word, StringComparison.Ordinal))
                ?? throw new DriverException($"the settings offer no {setting} {word}");
        }

        private static AgentRow Row(AgentActivityViewModel screen, string key) =>
            screen.Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal))
                ?? throw new DriverException($"Agents shows no row for {key}");

        private static string State(McpToolState state) => state switch
        {
            McpToolState.Connected => "connected",
            McpToolState.NotConnected => "connect",
            McpToolState.CannotTell => "cannot-tell",
            _ => "problem",
        };

        /// <summary>The pane open on the screen, watched so what it says is printed as it changes.</summary>
        private async Task<ConnectClientViewModel> PaneAsync()
        {
            var screen = await SettledAsync().ConfigureAwait(true);
            var pane = screen.Connection ?? throw new DriverException("no Connect pane is open");

            if (!ReferenceEquals(pane, _watched))
            {
                _watched = pane;
                pane.PropertyChanged += (_, changed) => Print(pane, changed.PropertyName);
            }

            return pane;
        }

        /// <summary>Waits for the pane just opened to compose its plan, then prints it.</summary>
        private async Task ShownAsync(AgentActivityViewModel screen)
        {
            await screen.Opening.ConfigureAwait(true);
            var model = await PaneAsync().ConfigureAwait(true);

            foreach (var line in model.Preview.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Console.Out.WriteLine($"preview {line}");
            }

            if (model.HasMessage)
            {
                Console.Out.WriteLine($"message {model.Message}");
            }

            Console.Out.WriteLine("preview end");
        }

        /// <summary>Closes the screen, as a lock or quitting does.</summary>
        internal void Close()
        {
            _screen?.Dispose();
            _screen = null;
            _watched = null;
        }

        public void Dispose() => Close();

        private static void Run(AsyncRelayCommand command, string button)
        {
            if (!command.CanExecute(null))
            {
                throw new DriverException($"the {button} button is disabled");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await command.ExecuteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine($"check threw {ex.GetType().Name}: {ex.Message}");
                }

                Console.Out.WriteLine("check end");
            });
        }

        private static void Print(ConnectClientViewModel model, string? property)
        {
            switch (property)
            {
                case nameof(ConnectClientViewModel.CheckMessage) when model.HasCheckMessage:
                    Console.Out.WriteLine($"check {model.CheckMessage}");
                    break;
                case nameof(ConnectClientViewModel.IsPicking) when model.IsPicking:
                    foreach (var (entry, i) in model.CheckEntries.Select((entry, i) => (entry, i)))
                    {
                        Console.Out.WriteLine($"check-entry n={i + 1} {entry.Name}");
                    }

                    break;
            }
        }
    }

    private static string Describe(ActivityRow row) =>
        $"client={row.Client} label={row.Label} entry={row.Entry} field={row.Field} left={row.Left}";

    /// <summary>A person in front of the prompt who approves whatever is asked.</summary>
    internal sealed class ApprovingPrompt : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Console.Out.WriteLine($"asking {prompt.Entry}");
            Console.Out.WriteLine("approved");
            return ValueTask.FromResult(ApprovalAnswer.Approved);
        }

        public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken)
        {
            Console.Out.WriteLine($"edit-asking command={prompt.Command} item={prompt.Item}");
            Console.Out.WriteLine("approved");
            return ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        }
    }

    /// <summary>A person in front of the prompt who never answers.</summary>
    internal sealed class HeldPrompt : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            HoldAsync($"asking {prompt.Entry}", cancellationToken);

        public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken) =>
            HoldAsync($"edit-asking command={prompt.Command} item={prompt.Item}", cancellationToken);

        private static async ValueTask<ApprovalAnswer> HoldAsync(string asking, CancellationToken cancellationToken)
        {
            Console.Out.WriteLine(asking);

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

    private static EntryFieldRow Field(EntryDetailViewModel detail, string name) =>
        detail.Fields.SingleOrDefault(field => field.Name == name)
            ?? throw new DriverException($"the pane lists no field '{name}'");

    /// <summary>Types the new value into a masked field one keystroke at a time, as the control forwards them.</summary>
    private void Type(SecretField field)
    {
        foreach (var c in _newPassword)
        {
            field.Type(c);
        }
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

        internal string? NewKeyfilePath { get; set; }

        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult(NewPath);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult(ExportPath);

        public Task<string?> PickKeyfileAsync() => Task.FromResult(KeyfilePath);

        public Task<string?> PickNewKeyfileAsync(string suggestedName) => Task.FromResult(NewKeyfilePath);

        public Task<string?> PickDotEnvAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
