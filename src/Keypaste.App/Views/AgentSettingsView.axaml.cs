using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>One agent's settings; everything it reads and does is <see cref="AgentSettingsViewModel"/>'s.</summary>
/// <remarks>
/// A choice re-reads the settings, which rebuilds the chips, so the keyboard goes back to the chip that
/// had it, found by what it sets, or to its group's first chip once that one is gone.
/// </remarks>
internal sealed partial class AgentSettingsView : UserControl
{
    private static readonly HashSet<string> _chipLists = new(StringComparer.Ordinal)
    {
        nameof(AgentSettingsViewModel.ModeChoices),
        nameof(AgentSettingsViewModel.ValueChoices),
        nameof(AgentSettingsViewModel.RunChoices),
        nameof(AgentSettingsViewModel.SeeScopes),
        nameof(AgentSettingsViewModel.SeeChoices),
        nameof(AgentSettingsViewModel.Rows),
    };

    private AgentSettingsViewModel? _watched;
    private ChipKey? _focused;
    private NavigationMethod _focusedBy;
    private bool _restoring;

    public AgentSettingsView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(GotFocusEvent, OnChipFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnFocusLeft, RoutingStrategies.Bubble, handledEventsToo: true);

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnModelChanged;
            }

            _watched = DataContext as AgentSettingsViewModel;
            _focused = null;

            if (_watched is not null)
            {
                _watched.PropertyChanged += OnModelChanged;
            }
        };
    }

    private void OnChipFocused(object? sender, FocusChangedEventArgs e)
    {
        _focused = e.Source is Button { DataContext: SettingChoice choice } ? ChipKey.Of(choice) : null;
        _focusedBy = e.NavigationMethod;
    }

    /// <summary>Forgets the chip once the keyboard has gone somewhere else that is still on screen.</summary>
    private void OnFocusLeft(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is Visual next && TopLevel.GetTopLevel(next) is not null && !this.IsVisualAncestorOf(next) && !next.IsVisualAncestorOf(this))
        {
            _focused = null;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_focused is null || _restoring || e.PropertyName is not { } name || !_chipLists.Contains(name))
        {
            return;
        }

        _restoring = true;
        Dispatcher.UIThread.Post(Restore, DispatcherPriority.Background);
    }

    /// <summary>Gives the keyboard back to the chip that had it, unless it has gone somewhere a person can still see.</summary>
    private void Restore()
    {
        _restoring = false;

        if (_focused is not { } key || TopLevel.GetTopLevel(this)?.FocusManager is not { } focus)
        {
            return;
        }

        var current = focus.GetFocusedElement() as Visual;
        var lost = current is null
            || !current.IsEffectivelyVisible
            || TopLevel.GetTopLevel(current) is null
            || current.IsVisualAncestorOf(this)
            || current is InputElement { IsEffectivelyEnabled: false };

        if (!lost)
        {
            return;
        }

        var chips = this.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button is { DataContext: SettingChoice, IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
            .ToList();

        var chip = chips.FirstOrDefault(button => ChipKey.Of((SettingChoice)button.DataContext!) == key)
            ?? chips.FirstOrDefault(button => ChipKey.Of((SettingChoice)button.DataContext!).SameGroup(key));

        // As the chip was reached, so a chip clicked with the pointer gets no keyboard focus ring.
        chip?.Focus(_focusedBy);
    }

    /// <summary>What a chip sets, which outlives the chip the panel rebuilds.</summary>
    private readonly record struct ChipKey(string Setting, string Word, string? Project)
    {
        internal static ChipKey Of(SettingChoice choice) => new(choice.Setting, choice.Word, choice.Project?.Name);

        internal bool SameGroup(ChipKey other) =>
            string.Equals(Setting, other.Setting, StringComparison.Ordinal) && string.Equals(Project, other.Project, StringComparison.Ordinal);
    }
}
