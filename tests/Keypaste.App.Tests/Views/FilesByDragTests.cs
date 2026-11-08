using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// A file dropped on the window goes where a picker's would, an item dropped on a group moves there, and each is drawn as
/// accepted only when it is. The drags are the headless platform's, hit-tested through the window as a real one is;
/// the platforms' own drag sources are not reachable from here.
/// </summary>
public sealed class FilesByDragTests
{
    private const string _bitwardenExport = """{"encrypted":false,"folders":[],"items":[{"type":1,"name":"from bitwarden","login":{"username":"me","password":"p"}}]}""";

    private static RenderedShell Shell(string name, double width = 1100)
    {
        var shell = new RenderedShell(name, vault =>
        {
            vault.CreateGroup(string.Empty, "servers", out _);
            vault.AddEntry(new VaultEntry { Title = "db", Username = "root", Password = "SENTINEL-DB" });
            vault.Relocate(new EntryName(string.Empty, "db"), new EntryName("servers", "db"), out _);
        });
        shell.Window.Width = width;
        shell.Frame();
        return shell;
    }

    private static string FileIn(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DropCue Cue(TopLevel window) => window.GetVisualDescendants().OfType<DropCue>().First();

    private static string Caption(DropCue cue) =>
        cue.GetVisualDescendants().OfType<TextBlock>().Single().Text!;

    private static ListBoxItem GroupRowOf(RenderedShell shell, string path)
    {
        var nav = shell.Named<ListBox>("Nav");
        return (ListBoxItem)nav.ContainerFromItem(nav.Items.OfType<GroupRow>().Single(row => row.Path == path))!;
    }

    private static (UnlockViewModel Model, Window Window, TempVault Vault, AppVaultSession Session) Unlock(bool remembered = true)
    {
        var fixture = new TempVault();

        if (remembered)
        {
            fixture.RememberSelf();
        }

        var session = new AppVaultSession(new ManualClock(AppClock.Start));
        var model = new UnlockViewModel(session, fixture.Home, new FakeVaultFilePicker(), () => { });
        var window = new MainWindow { Width = 900, Height = 700 };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
        window.Show();
        WindowInput.Drain();
        return (model, window, fixture, session);
    }

    private static string AnotherVault(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        using var vault = Vault.Create(path, "another password");
        vault.Save();
        return path;
    }

    private static void BackupBeside(string vaultPath)
    {
        var kept = Directory.CreateDirectory(VaultBackups.DirectoryFor(vaultPath)).FullName;
        var backup = Path.Combine(kept, $"{Path.GetFileNameWithoutExtension(vaultPath)}.20260101T000000Z.kdbx");
        using var vault = Vault.Create(backup, "another password");
        vault.Save();
    }

    private static void Finish((UnlockViewModel Model, Window Window, TempVault Vault, AppVaultSession Session) screen)
    {
        screen.Window.Hide();
        screen.Model.Dispose();
        screen.Session.Dispose();
        screen.Vault.Dispose();
    }

    [Fact]
    public Task A_vault_lights_the_unlock_screen_and_is_selected_with_the_password_typed_for_another_gone() => HeadlessSession.On(() =>
    {
        var screen = Unlock();
        var other = AnotherVault(screen.Vault.Home, "other.kdbx");
        var at = WindowDrag.Centre(screen.Window, screen.Window.GetVisualDescendants().OfType<UnlockView>().Single());
        screen.Model.Type('x');

        WindowDrag.Over(screen.Window, at, WindowDrag.Files(other));
        Assert.True(Cue(screen.Window).IsVisible);
        Assert.Equal("Open other.kdbx", Caption(Cue(screen.Window)));

        WindowDrag.Drop(screen.Window, at, WindowDrag.Files(other));
        Assert.False(Cue(screen.Window).IsVisible);
        Assert.Equal(other, screen.Model.SelectedPath);
        Assert.Equal(0, screen.Model.MaskedLength);
        Assert.Null(screen.Window.GetVisualDescendants().OfType<ListBox>().Single(list => ReferenceEquals(list.ItemsSource, screen.Model.Recent)).SelectedItem);
        Assert.False(screen.Session.IsUnlocked);
        Finish(screen);
    });

    [Fact]
    public Task Anything_else_is_not_lit_on_the_unlock_screen_and_selects_nothing() => HeadlessSession.On(() =>
    {
        var screen = Unlock();
        var selected = screen.Model.SelectedPath;
        var notes = FileIn(screen.Vault.Home, "notes.txt", "hello");
        var at = WindowDrag.Centre(screen.Window, screen.Window.GetVisualDescendants().OfType<UnlockView>().Single());

        WindowDrag.Over(screen.Window, at, WindowDrag.Files(notes));
        Assert.False(Cue(screen.Window).IsVisible);

        WindowDrag.Over(screen.Window, at, WindowDrag.Files(notes, notes));
        Assert.False(Cue(screen.Window).IsVisible);

        WindowDrag.Drop(screen.Window, at, WindowDrag.Files(notes));
        Assert.Equal(selected, screen.Model.SelectedPath);
        Finish(screen);
    });

    [Fact]
    public Task A_file_dropped_on_a_form_or_a_busy_unlock_changes_nothing() => HeadlessSession.On(async () =>
    {
        var screen = Unlock();
        var other = AnotherVault(screen.Vault.Home, "other.kdbx");
        var selected = screen.Model.SelectedPath;
        var at = WindowDrag.Centre(screen.Window, screen.Window.GetVisualDescendants().OfType<UnlockView>().Single());
        var picker = new FakeVaultFilePicker { NewPath = Path.Combine(screen.Vault.Home, "new.kdbx") };
        var creating = new UnlockViewModel(screen.Session, screen.Vault.Home, picker, () => { });
        screen.Window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = creating };
        WindowInput.Drain();
        await creating.StartCreateAsync();
        Assert.True(creating.IsCreating);
        DrawnFrame.Capture(screen.Window);

        WindowDrag.Over(screen.Window, at, WindowDrag.Files(other));
        Assert.False(Cue(screen.Window).IsVisible);

        WindowDrag.Drop(screen.Window, at, WindowDrag.Files(other));
        Assert.True(creating.IsCreating);
        Assert.Equal(selected, creating.SelectedPath);
        creating.Dispose();
        Finish(screen);
    });

    [Fact]
    public Task A_handed_file_is_only_selected_and_says_where_it_is_when_it_is_new() => HeadlessSession.On(() =>
    {
        var screen = Unlock();
        var other = AnotherVault(screen.Vault.Home, "other.kdbx");

        screen.Model.TakeHanded(other);

        Assert.Equal(other, screen.Model.SelectedPath);
        Assert.False(screen.Session.IsUnlocked);
        Assert.Equal(other, screen.Model.NewFileLocation);

        // Focus is in the password field by now, and the first character typed must not take away how to tell this file from another.
        screen.Model.Type('x');
        Assert.Equal(other, screen.Model.NewFileLocation);
        screen.Model.ClearPassword();

        screen.Model.SelectedPath = screen.Vault.Path_;
        Assert.Null(screen.Model.NewFileLocation);
        Finish(screen);
    });

    [Fact]
    public Task A_handed_file_only_a_backup_can_bring_back_also_says_where_it_is() => HeadlessSession.On(() =>
    {
        var screen = Unlock();
        var gone = Path.Combine(screen.Vault.Home, "gone.kdbx");
        BackupBeside(gone);

        screen.Model.TakeHanded(gone);

        Assert.Equal(gone, screen.Model.SelectedPath);
        Assert.True(screen.Model.IsRestoreOnly);
        Assert.Equal(gone, screen.Model.NewFileLocation);
        Finish(screen);
    });

    [Fact]
    public Task A_fifo_with_a_backup_beside_it_does_not_hold_the_window() => HeadlessSession.On(async () =>
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no FIFO in its file system");
        }

        var screen = Unlock();
        var fifo = Path.Combine(screen.Vault.Home, "fifo.kdbx");
        using (var made = System.Diagnostics.Process.Start("mkfifo", [fifo]))
        {
            await made.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        BackupBeside(fifo);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var stop = new CancellationTokenSource();

        // Lets a read that wrongly opened the FIFO go on, so this fails rather than hanging the run.
        var release = Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ContinueWith(
            waited =>
            {
                if (waited.IsCompletedSuccessfully)
                {
                    using var writer = File.OpenWrite(fifo);
                }
            },
            TaskScheduler.Default);

        screen.Model.TakeHanded(fifo);
        Assert.True(screen.Model.TakesDrop(fifo));
        await stop.CancelAsync();
        await release;

        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(4), "the screen opened the FIFO");
        Assert.True(screen.Model.IsRestoreOnly);
        Finish(screen);
    });

    [Fact]
    public Task A_handed_file_never_takes_the_place_of_a_typed_password_or_a_form() => HeadlessSession.On(async () =>
    {
        var screen = Unlock();
        var other = AnotherVault(screen.Vault.Home, "other.kdbx");
        var selected = screen.Model.SelectedPath;
        screen.Model.Type('x');

        screen.Model.TakeHanded(other);

        Assert.Equal(selected, screen.Model.SelectedPath);
        Assert.Equal(1, screen.Model.MaskedLength);
        Assert.Contains("left other.kdbx unopened while a password is typed here", screen.Model.Message, StringComparison.Ordinal);

        screen.Model.ClearPassword();
        var picker = new FakeVaultFilePicker { NewPath = Path.Combine(screen.Vault.Home, "new.kdbx") };
        using var creating = new UnlockViewModel(screen.Session, screen.Vault.Home, picker, () => { });
        await creating.StartCreateAsync();

        creating.TakeHanded(other);

        Assert.True(creating.IsCreating);
        Assert.Equal(selected, creating.SelectedPath);
        Assert.Contains("this form is open", creating.Message, StringComparison.Ordinal);
        Finish(screen);
    });

    [Fact]
    public Task A_handed_file_that_is_no_vault_or_a_name_built_to_mislead_selects_nothing_or_reads_clean() => HeadlessSession.On(() =>
    {
        var screen = Unlock();
        var selected = screen.Model.SelectedPath;

        screen.Model.TakeHanded(FileIn(screen.Vault.Home, "notes.txt", "hello"));
        Assert.Equal(selected, screen.Model.SelectedPath);
        Assert.True(screen.Model.IsError);

        var bent = AnotherVault(screen.Vault.Home, "safe‮xcod.kdbx");
        screen.Model.TakeHanded(bent);
        Assert.Equal(bent, screen.Model.SelectedPath);
        Assert.DoesNotContain('‮', screen.Model.SelectedName);
        Assert.DoesNotContain('‮', screen.Model.Heading);
        Assert.DoesNotContain('‮', screen.Model.Message);

        // The location hides nothing: it is what tells a name with an invisible character from the name it looks like.
        Assert.Contains(@"\u202E", screen.Model.NewFileLocation, StringComparison.Ordinal);
        Assert.DoesNotContain('‮', screen.Model.NewFileLocation!);
        Assert.Contains(@"\u202E", screen.Model.SelectedLocation, StringComparison.Ordinal);
        Finish(screen);
    });

    [Fact]
    public Task An_export_lights_the_content_and_opens_the_import_preview_without_writing() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-export-");
        var export = FileIn(shell.Home, "bitwarden.json", _bitwardenExport);
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));
        var titles = shell.Session.Unlocked!.ReadEntries().Count;

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(export));
        Assert.True(Cue(shell.Window).IsVisible);
        Assert.Equal("Import bitwarden.json", Caption(Cue(shell.Window)));

        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(export));
        Assert.False(Cue(shell.Window).IsVisible);
        Assert.True(shell.Shell.HasExportImport);
        Assert.Equal(titles, shell.Session.Unlocked!.ReadEntries().Count);

        // Over an import nothing else is taken.
        WindowDrag.Over(shell.Window, at, WindowDrag.Files(export));
        Assert.False(Cue(shell.Window).IsVisible);
    });

    [Fact]
    public Task A_vault_opens_the_import_dialog_where_a_vault_is_open() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-kdbx-");
        var other = AnotherVault(shell.Home, "other.kdbx");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(other));

        Assert.True(shell.Shell.HasKdbxImport);
        Assert.True(shell.Session.IsUnlocked);
    });

    [Fact]
    public Task A_dotenv_goes_to_the_projects_preview_and_past_the_discard_ask() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-env-");
        var env = FileIn(shell.Home, ".env", "ADDED_KEY=value\n");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(env));
        Assert.Equal("Import .env into billing", Caption(Cue(shell.Window)));

        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(env));

        var project = Assert.IsType<EnvSetsViewModel>(shell.Shell.Content).OpenProject!;
        Assert.Equal("billing", project.Name);
        Assert.True(project.Import.IsPreviewing);
        Assert.Null(shell.Session.Unlocked!.Find(new EntryName(string.Empty, "ADDED_KEY")));
    });

    [Fact]
    public Task Another_file_attaches_to_the_open_item_and_the_cap_still_holds() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-attach-");
        var detail = shell.OpenPane();
        var photo = FileIn(shell.Home, "photo.png", "not much");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(photo));
        Assert.True(Cue(shell.Window).IsVisible);
        Assert.Equal("Attach photo.png to github", Caption(Cue(shell.Window)));

        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(photo));
        Assert.Contains(detail.Attachments.Rows, row => row.Name == "photo.png");

        // Over the cap, or under a name the item already has, it is not lit, and the drop says why.
        var big = Path.Combine(shell.Home, "big.bin");
        File.WriteAllBytes(big, new byte[EntryAttachment.MaximumSize + 1]);
        WindowDrag.Over(shell.Window, at, WindowDrag.Files(big));
        Assert.False(Cue(shell.Window).IsVisible);
        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(big));
        Assert.DoesNotContain(detail.Attachments.Rows, row => row.Name == "big.bin");
        Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains("big.bin is larger than 5 MiB", StringComparison.Ordinal));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(photo));
        Assert.False(Cue(shell.Window).IsVisible);
        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(photo));
        Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains("already has an attachment called photo.png", StringComparison.Ordinal));
        Assert.Single(detail.Attachments.Rows, row => row.Name == "photo.png");

        // The write still holds the cap for a file that grew after it was judged.
        detail.Attachments.AddDropped(big);
        Assert.DoesNotContain(detail.Attachments.Rows, row => row.Name == "big.bin");
        Assert.NotNull(Assert.IsType<EntriesViewModel>(shell.Shell.Content).Error);
    });

    [Fact]
    public Task A_typed_group_name_takes_no_attachment() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-group-form-");
        var detail = shell.OpenPane();
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        entries.BeginCreateGroupCommand.Press();
        WindowInput.Drain();
        entries.DraftGroupName = "typed";
        var photo = FileIn(shell.Home, "photo.png", "not much");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(photo));
        Assert.False(Cue(shell.Window).IsVisible);
        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(photo));
        Assert.Empty(detail.Attachments.Rows);
    });

    [Fact]
    public Task Another_file_with_no_item_open_is_not_lit_and_says_so_if_it_lands() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-nothing-");
        var photo = FileIn(shell.Home, "photo.png", "not much");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(photo));
        Assert.False(Cue(shell.Window).IsVisible);

        WindowDrag.Drop(shell.Window, at, WindowDrag.Files(photo));
        Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains("can't take photo.png", StringComparison.Ordinal));
    });

    [Fact]
    public Task An_edit_in_progress_takes_no_attachment() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drop-editing-");
        var detail = shell.OpenPane();
        detail.EditCommand.Press();
        WindowInput.Drain();
        var photo = FileIn(shell.Home, "photo.png", "not much");
        var at = WindowDrag.Centre(shell.Window, shell.Named<Border>("ContentArea"));

        WindowDrag.Over(shell.Window, at, WindowDrag.Files(photo));
        Assert.False(Cue(shell.Window).IsVisible);
        Assert.Empty(detail.Attachments.Rows);
    });

    [Fact]
    public Task An_item_dropped_on_a_group_moves_there_and_undo_takes_it_back() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-item-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        var github = entries.Rows.Single(row => row.Title == "github");
        var view = shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        view.HoldItem(github);
        var at = WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers"));
        using var data = FileDragData.ItemPayload();

        WindowDrag.Over(shell.Window, at, data);
        Assert.True(Cue(shell.Window).IsVisible);
        Assert.Equal("Move to servers", Caption(Cue(shell.Window)));

        WindowDrag.Drop(shell.Window, at, data);
        view.ReleaseItem();

        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName("servers", "github")));
        Assert.True(entries.CanUndo);

        entries.UndoCommand.Press();
        WindowInput.Drain();
        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName(string.Empty, "github")));
    });

    private static EntryExposure Patterns(string pattern) =>
        EntryExposure.TryCreate([pattern], out var exposure, out var error) ? exposure : throw new InvalidOperationException(error);

    [Fact]
    public Task An_item_over_its_own_group_or_the_list_is_not_lit() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-own-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
        WindowInput.Drain();
        var db = entries.Rows.Single(row => row.Title == "db");
        var view = shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        view.HoldItem(db);
        using var data = FileDragData.ItemPayload();

        WindowDrag.Over(shell.Window, WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers")), data);
        Assert.False(Cue(shell.Window).IsVisible);

        WindowDrag.Over(shell.Window, WindowDrag.Centre(shell.Window, shell.Named<ListBox>("Rows")), data);
        Assert.False(Cue(shell.Window).IsVisible);
        view.ReleaseItem();
    });

    [Fact]
    public Task An_item_over_a_group_already_holding_its_title_is_not_lit_and_stays() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-occupied-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        shell.Session.Unlocked!.AddEntry(new VaultEntry { Title = "github", GroupPath = "servers", Password = "SENTINEL-OTHER" });
        var github = entries.Rows.First(row => row.Title == "github" && row.GroupPath.Length == 0);
        var view = shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        view.HoldItem(github);
        var at = WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers"));
        using var data = FileDragData.ItemPayload();

        WindowDrag.Over(shell.Window, at, data);
        Assert.False(Cue(shell.Window).IsVisible);

        WindowDrag.Drop(shell.Window, at, data);
        view.ReleaseItem();

        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName(string.Empty, "github")));
        Assert.Null(entries.Error);
    });

    [Fact]
    public Task A_drag_nothing_in_this_process_holds_moves_nothing() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-foreign-");
        var at = WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers"));
        using var data = FileDragData.ItemPayload();

        WindowDrag.Over(shell.Window, at, data);
        Assert.False(Cue(shell.Window).IsVisible);

        WindowDrag.Drop(shell.Window, at, data);
        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName(string.Empty, "github")));
    });

    [Fact]
    public Task Pressing_a_row_and_moving_starts_a_drag_that_holds_the_item_until_it_ends() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-start-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        var github = entries.Rows.Single(row => row.Title == "github");
        var rows = shell.Named<ListBox>("Rows");
        shell.Frame();
        var row = rows.ContainerFromItem(github)!;
        var view = shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        var at = WindowDrag.Centre(shell.Window, row);

        shell.Window.MouseDown(at, MouseButton.Left);
        shell.Window.MouseMove(at + new Avalonia.Point(2, 0));
        WindowInput.Drain();
        Assert.False(view.IsHoldingItem);

        shell.Window.MouseMove(at + new Avalonia.Point(0, 12));
        WindowInput.Drain();
        Assert.True(view.IsHoldingItem);

        shell.Window.MouseUp(at + new Avalonia.Point(0, 12), MouseButton.Left);
        WindowInput.Drain();
        Assert.False(view.IsHoldingItem);
    });

    [Fact]
    public Task A_whole_drag_from_a_row_to_a_group_moves_the_item() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-drag-whole-");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        var rows = shell.Named<ListBox>("Rows");
        shell.Frame();
        var at = WindowDrag.Centre(shell.Window, rows.ContainerFromItem(entries.Rows.Single(row => row.Title == "github"))!);
        var group = WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers"));

        shell.Window.MouseDown(at, MouseButton.Left, Avalonia.Input.RawInputModifiers.LeftMouseButton);
        shell.Window.MouseMove(at + new Avalonia.Point(0, 12), Avalonia.Input.RawInputModifiers.LeftMouseButton);
        WindowInput.Drain();
        shell.Window.MouseMove(group, Avalonia.Input.RawInputModifiers.LeftMouseButton);
        WindowInput.Drain();
        Assert.True(shell.Window.GetVisualDescendants().OfType<ShellView>().Single().IsHoldingItem);
        Assert.True(Cue(shell.Window).IsVisible);
        shell.Window.MouseUp(group, MouseButton.Left, Avalonia.Input.RawInputModifiers.LeftMouseButton);
        WindowInput.Drain();

        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName("servers", "github")));
        Assert.True(entries.CanUndo);
    });

    [Fact]
    public Task An_items_drag_gives_the_platform_nothing_of_it() => HeadlessSession.On(() =>
    {
        using var payload = FileDragData.ItemPayload();
        var item = Assert.Single(payload.Items);
        var format = Assert.Single(item.Formats);

        Assert.Equal(new byte[] { 1 }, Assert.IsType<byte[]>(item.TryGetRaw(format)));
        Assert.DoesNotContain(DataFormat.Text, payload.Formats);
        Assert.DoesNotContain(DataFormat.File, payload.Formats);
    });

    [Fact]
    public Task On_the_rail_holding_the_groups_button_opens_the_groups_and_the_drop_closes_them() => HeadlessSession.On(async () =>
    {
        using var shell = Shell("keypaste-drag-rail-", width: 720);
        Assert.True(shell.Shell.ShowsRail);
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        var github = entries.Rows.Single(row => row.Title == "github");
        var view = shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        view.HoldItem(github);
        using var data = FileDragData.ItemPayload();

        WindowDrag.Over(shell.Window, WindowDrag.Centre(shell.Window, shell.Named<Button>("RailGroups")), data);
        Assert.True(Cue(shell.Window).IsVisible);
        Assert.False(shell.Shell.SidebarOverlays);

        await Task.Delay(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);
        WindowInput.Drain();
        Assert.True(shell.Shell.SidebarOverlays);
        Assert.False(Cue(shell.Window).IsVisible);
        shell.Frame();

        var at = WindowDrag.Centre(shell.Window, GroupRowOf(shell, "servers"));
        WindowDrag.Over(shell.Window, at, data);
        Assert.Equal("Move to servers", Caption(Cue(shell.Window)));
        WindowDrag.Drop(shell.Window, at, data);
        view.ReleaseItem();

        Assert.False(shell.Shell.SidebarOverlays);
        Assert.NotNull(shell.Session.Unlocked!.Find(new EntryName("servers", "github")));
    });

    [Fact]
    public Task A_handed_file_with_a_vault_open_asks_and_only_the_answer_acts() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-handed-open-");
        var other = AnotherVault(shell.Home, "other.kdbx");
        var opened = 0;

        shell.Shell.AskOpenHanded(other, () => opened++);
        WindowInput.Drain();
        shell.Frame();

        Assert.Equal("Lock vault.kdbx and open other.kdbx?", shell.Shell.HandedPrompt);
        Assert.True(shell.Session.IsUnlocked);
        Assert.Equal(0, opened);
        Assert.True(shell.Named<Button>("KeepVault").IsFocused);

        // Return on the focused Cancel, and Escape, leave the vault as it was.
        WindowInput.Escape(shell.Window);
        Assert.False(shell.Shell.HasHandedPrompt);
        Assert.Equal(0, opened);

        shell.Shell.AskOpenHanded(other, () => opened++);
        shell.Shell.LockAndOpenCommand.Press();
        Assert.Equal(1, opened);
    });

    [Fact]
    public Task A_handed_file_the_unlock_screen_would_refuse_never_asks_to_lock() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-handed-refused-");
        var opened = 0;

        foreach (var (path, says) in new[]
        {
            (FileIn(shell.Home, "notes.txt", "hello"), "can't open notes.txt"),
            (Path.Combine(shell.Home, "missing.kdbx"), "can't open missing.kdbx"),
            (shell.Session.VaultPath!, "vault.kdbx is already open"),
        })
        {
            shell.Shell.AskOpenHanded(path, () => opened++);

            Assert.False(shell.Shell.HasHandedPrompt);
            Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains(says, StringComparison.Ordinal));
        }

        Assert.Equal(0, opened);
        Assert.True(shell.Session.IsUnlocked);
    });

    [Fact]
    public Task A_handed_file_does_not_rise_over_another_dialog() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-handed-over-");
        var other = AnotherVault(shell.Home, "other.kdbx");
        shell.Shell.OpenGeneratorCommand.Press();
        WindowInput.Drain();
        Assert.True(shell.Shell.HasGeneratorDialog);

        shell.Shell.AskOpenHanded(other, () => { });

        Assert.False(shell.Shell.HasHandedPrompt);
        Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains("left other.kdbx unopened", StringComparison.Ordinal));
    });

    [Fact]
    public Task Cancelling_a_handed_file_gives_focus_back_to_where_it_was() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-handed-focus-");
        var other = AnotherVault(shell.Home, "other.kdbx");
        var search = shell.Named<TextBox>("ShellSearch");
        search.Focus();
        WindowInput.Drain();
        Assert.True(search.IsFocused);

        shell.Shell.AskOpenHanded(other, () => { });
        WindowInput.Drain();
        Assert.True(shell.Named<Button>("KeepVault").IsFocused);

        WindowInput.Escape(shell.Window);
        WindowInput.Drain();
        Assert.True(search.IsFocused);
    });

    [Fact]
    public Task A_vault_whose_name_is_built_to_mislead_reads_clean_in_the_shell() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var bent = AnotherVault(fixture.Home, "safe‮xcod.kdbx");
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using (var master = TempVault.Secret("another password"))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(bent, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, null, clipboard: new Clipboard.FakeClipboard(), clock: new ManualClock(AppClock.Start));

        Assert.DoesNotContain('‮', shell.VaultName);
    });

    [Fact]
    public Task A_handed_file_past_a_draft_goes_through_the_discard_ask() => HeadlessSession.On(() =>
    {
        using var shell = Shell("keypaste-handed-draft-");
        var other = AnotherVault(shell.Home, "other.kdbx");
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        entries.BeginAddCommand.Press();
        WindowInput.Drain();
        entries.NewItem!.Title = "typed";
        var opened = 0;

        shell.Shell.AskOpenHanded(other, () => opened++);
        shell.Shell.LockAndOpenCommand.Press();

        Assert.True(shell.Shell.HasDiscardPrompt);
        Assert.Equal(0, opened);

        // A second file while that is asked leaves it asked, and says so.
        shell.Shell.AskOpenHanded(other, () => opened++);
        Assert.False(shell.Shell.HasHandedPrompt);
        Assert.Contains(shell.Shell.Toasts.Shown, toast => toast.Message.Contains("left other.kdbx unopened", StringComparison.Ordinal));

        shell.Shell.KeepDraftCommand.Press();
        Assert.Equal(0, opened);
        Assert.Equal("typed", entries.NewItem!.Title);
    });
}
