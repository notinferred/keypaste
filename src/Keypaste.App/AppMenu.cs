using Avalonia.Controls;
using Keypaste.App.Controls;

namespace Keypaste.App;

/// <summary>
/// Draws <see cref="AppCommands"/> as the macOS menu bar and Dock menu, or as the menu button's flyout on Windows and
/// Linux (Q.2).
/// </summary>
/// <remarks>
/// Each item's command asks its <see cref="AppCommand.CanRun"/> again just before macOS shows a menu or matches a
/// chord against it, and before the flyout opens, so an item is on exactly while its command can run.
/// </remarks>
internal static class AppMenu
{
    /// <summary>Fills a menu in place: the macOS app menu, which Avalonia reads once at start, is made empty early and filled here.</summary>
    internal static void Fill(NativeMenu menu, IReadOnlyList<AppMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var item in NativeItems(entries))
        {
            menu.Add(item);
        }

        menu.NeedsUpdate += (_, _) => Refresh(menu);
        menu.Opening += (_, _) => RevealedValue.HideShown();
    }

    /// <summary>A menu of <paramref name="entries"/>, as the Dock's.</summary>
    internal static NativeMenu Native(IReadOnlyList<AppMenuEntry> entries)
    {
        var menu = new NativeMenu();
        Fill(menu, entries);
        return menu;
    }

    /// <summary>The main window's menus on macOS: File, Edit, Item, Project, Agents, View, Window and Help, after the app menu.</summary>
    internal static NativeMenu Bar(AppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var bar = new NativeMenu();

        foreach (var group in commands.Menus)
        {
            bar.Add(new NativeMenuItem(group.Header) { Menu = Native(group.Entries) });
        }

        return bar;
    }

    /// <summary>The menu button's flyout on Windows and Linux: keypaste, then File, Edit, Item, Project, Agents, View, Window and Help.</summary>
    /// <remarks>A chord is shown, never bound: the window's chords already run it.</remarks>
    internal static MenuFlyout Flyout(AppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var made = new List<Action>();
        var flyout = new MenuFlyout();
        flyout.Items.Add(Group("keypaste", commands.AppMenu, made));

        foreach (var group in commands.Menus)
        {
            flyout.Items.Add(Group(group.Header, group.Entries, made));
        }

        flyout.Opening += (_, _) =>
        {
            RevealedValue.HideShown();
            made.ForEach(refresh => refresh());
        };
        GiveTheKeyboardToWhatItOpens(flyout);
        return flyout;
    }

    /// <summary>A row's context menu: the same commands as the menus, on exactly while each can run.</summary>
    internal static MenuFlyout Context(IReadOnlyList<AppMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var made = new List<Action>();
        var flyout = new MenuFlyout();

        foreach (var entry in entries)
        {
            flyout.Items.Add(entry is AppCommand command ? Item(command, made) : new Separator());
        }

        flyout.Opening += (_, _) => made.ForEach(refresh => refresh());
        GiveTheKeyboardToWhatItOpens(flyout);
        return flyout;
    }

    // Closing hands the keyboard back to where it was, so a form a choice opened takes it only after that.
    private static void GiveTheKeyboardToWhatItOpens(MenuFlyout flyout)
    {
        Control? opened = null;
        void Heard(Control form) => opened = form;

        flyout.Opening += (_, _) =>
        {
            opened = null;
            ShowInView.Shown += Heard;
        };
        flyout.Closed += (_, _) =>
        {
            ShowInView.Shown -= Heard;

            if (opened is { } form)
            {
                ShowInView.TakeTheKeyboard(form);
            }
        };
    }

    private static IEnumerable<NativeMenuItemBase> NativeItems(IReadOnlyList<AppMenuEntry> entries) =>
        entries.Select(entry => entry switch
        {
            AppCommand command => NativeItem(command),
            AppSubmenu submenu => NativeSubmenu(submenu),
            _ => (NativeMenuItemBase)new NativeMenuItemSeparator(),
        });

    private static NativeMenuItem NativeItem(AppCommand command)
    {
        var item = new NativeMenuItem(command.HeaderNow)
        {
            Command = new RelayCommand(command.Run, command.CanRun),
            CommandParameter = command,
            Gesture = command.Shown,
        };
        command.Reworded += () => item.Header = command.HeaderNow;
        return item;
    }

    // Its entries are read again as it is about to show, so Open recent lists what recent.toml holds now.
    private static NativeMenuItem NativeSubmenu(AppSubmenu submenu)
    {
        var menu = new NativeMenu();
        IReadOnlyList<string> shown = [];

        void Read()
        {
            var entries = submenu.Entries();
            var headers = entries.Select(Header).ToList();

            if (!headers.SequenceEqual(shown, StringComparer.Ordinal))
            {
                menu.Items.Clear();

                foreach (var item in NativeItems(entries))
                {
                    menu.Add(item);
                }

                shown = headers;
            }

            Refresh(menu);
        }

        Read();
        menu.NeedsUpdate += (_, _) => Read();
        return new NativeMenuItem(submenu.Header) { Menu = menu };
    }

    private static void Refresh(NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            (item.Command as RelayCommand)?.NotifyCanExecuteChanged();
            (item.CommandParameter as AppCommand)?.Reword();
        }
    }

    private static MenuItem Group(string header, IReadOnlyList<AppMenuEntry> entries, List<Action> made)
    {
        var group = new MenuItem { Header = header };
        AddItems(group, entries, made);
        return group;
    }

    private static void AddItems(MenuItem parent, IReadOnlyList<AppMenuEntry> entries, List<Action> made)
    {
        foreach (var entry in entries)
        {
            parent.Items.Add(entry switch
            {
                AppCommand command => Item(command, made),
                AppSubmenu submenu => Submenu(submenu, made),
                _ => new Separator(),
            });
        }
    }

    private static MenuItem Item(AppCommand command, List<Action> made)
    {
        var relay = new RelayCommand(command.Run, command.CanRun);
        var item = new MenuItem { Header = command.HeaderNow, Command = relay, InputGesture = command.Shown };
        command.Reworded += () => item.Header = command.HeaderNow;
        made.Add(() =>
        {
            relay.NotifyCanExecuteChanged();
            item.Header = command.HeaderNow;
        });
        return item;
    }

    private static MenuItem Submenu(AppSubmenu submenu, List<Action> made)
    {
        var item = new MenuItem { Header = submenu.Header };
        var own = new List<Action>();
        var shown = submenu.Entries();
        AddItems(item, shown, own);
        made.Add(() => own.ForEach(refresh => refresh()));
        item.SubmenuOpened += (_, _) =>
        {
            // A folder's entries are the same objects each time, whose handlers would pile up on them if drawn again; Open recent's are new each read.
            var entries = submenu.Entries();

            if (!ReferenceEquals(entries, shown))
            {
                shown = entries;
                own.Clear();
                item.Items.Clear();
                AddItems(item, entries, own);
            }

            own.ForEach(refresh => refresh());
        };

        return item;
    }

    private static string Header(AppMenuEntry entry) => entry switch
    {
        AppCommand command => command.Header,
        AppSubmenu submenu => submenu.Header,
        _ => "-",
    };
}
