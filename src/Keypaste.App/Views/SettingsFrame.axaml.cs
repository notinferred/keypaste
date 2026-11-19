using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// Settings' section list beside the section in view, which every Settings screen draws itself in, so all seven read as one screen.
/// </summary>
internal sealed partial class SettingsFrame : UserControl
{
    internal static readonly StyledProperty<SettingsSectionChoice> SectionProperty =
        AvaloniaProperty.Register<SettingsFrame, SettingsSectionChoice>(nameof(Section), SettingsSectionChoice.General, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    internal static readonly StyledProperty<bool> SectionIsOpenProperty =
        AvaloniaProperty.Register<SettingsFrame, bool>(nameof(SectionIsOpen), defaultValue: true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    internal static readonly StyledProperty<object?> PageProperty =
        AvaloniaProperty.Register<SettingsFrame, object?>(nameof(Page));

    private readonly ListBox _sections;

    public SettingsFrame()
    {
        AvaloniaXamlLoader.Load(this);

        var split = this.FindControl<ListDetail>("Split")!;
        _sections = this.FindControl<ListBox>("SettingsSections")!;
        _sections.SelectedItem = Section;
        _sections.SelectionChanged += (_, _) => Choose(_sections.SelectedItem as SettingsSectionChoice);

        // Choosing the section already selected is no change, so a press or Return on its row reopens it.
        _sections.AddHandler(TappedEvent, (_, _) => SectionIsOpen = true);
        _sections.AddHandler(
            KeyUpEvent,
            (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space)
                {
                    SectionIsOpen = true;
                }
            });

        this.FindControl<Button>("BackToSections")!.Click += (_, _) => SectionIsOpen = false;

        split.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListDetail.ShowsBackProperty)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } window && KeyboardIsHere(window))
                        {
                            Control next = split.ShowsBack ? this.FindControl<Button>("BackToSections")! : _sections;
                            next.Focus(NavigationMethod.Tab);
                        }
                    },
                    DispatcherPriority.Loaded);
            }
        };
    }

    /// <summary>The section in view; one shown by a screen of its own is fixed by that screen.</summary>
    internal SettingsSectionChoice Section
    {
        get => GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    /// <summary>Whether the section stands where a narrow window would otherwise show the list.</summary>
    internal bool SectionIsOpen
    {
        get => GetValue(SectionIsOpenProperty);
        set => SetValue(SectionIsOpenProperty, value);
    }

    /// <summary>The section's content, under its title.</summary>
    internal object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == SectionProperty && _sections is not null)
        {
            _sections.SelectedItem = Section;
        }
    }

    private void Choose(SettingsSectionChoice? chosen)
    {
        if (chosen is null || chosen == Section)
        {
            return;
        }

        if (chosen.Screen is DestinationKind.Settings && Section.Screen is DestinationKind.Settings)
        {
            Section = chosen;
            SectionIsOpen = true;
            return;
        }

        // Another screen draws the section, so the list keeps showing this one until the shell moves there.
        _sections.SelectedItem = Section;
        (this.FindAncestorOfType<ShellView>()?.DataContext as ShellViewModel)?.OpenSettings(chosen);
    }

    private bool KeyboardIsHere(Window window) => window.FocusManager?.GetFocusedElement() is not Visual focused || this.IsVisualAncestorOf(focused);
}
