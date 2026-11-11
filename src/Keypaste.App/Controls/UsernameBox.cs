using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Controls;

/// <summary>
/// A username box that completes its best match in grey inside the field as it is typed: Tab accepts the completion, Down lists every match
/// in the box's own list, and Escape dismisses the completion or the list. The matches are <see cref="Suggestions"/>, usernames only.
/// </summary>
internal sealed class UsernameBox : AutoCompleteBox
{
    public static readonly StyledProperty<IReadOnlyList<string>?> SuggestionsProperty =
        AvaloniaProperty.Register<UsernameBox, IReadOnlyList<string>?>(nameof(Suggestions));

    private readonly TextBlock _completion = new() { IsHitTestVisible = false };
    private TextBox? _field;
    private bool _listAsked;
    private bool _dismissed;

    public UsernameBox()
    {
        ItemTemplate = new FuncDataTemplate<string>((username, _) => new TextBlock { Text = username, Classes = { "mono" } });
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DropDownOpening += OnDropDownOpening;
        DropDownClosed += (_, _) => _listAsked = false;
        ShowCompletion();
    }

    /// <summary>What the box offers for its text, by the vault's suggestion rule.</summary>
    public IReadOnlyList<string>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>The grey rest of the best match, drawn after what is typed, or null while nothing completes it.</summary>
    internal string? CompletionShown => _completion.IsVisible && Completion is { } match ? match[(Text ?? string.Empty).Length..] : null;

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    private string? Completion => _dismissed ? null : UsernameSuggestions.Completion(Text ?? string.Empty, Suggestions ?? []);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_field is not null)
        {
            _field.TemplateApplied -= OnFieldTemplateApplied;
        }

        _field = e.NameScope.Find<TextBox>("PART_TextBox");

        if (_field is not null)
        {
            _field.TemplateApplied += OnFieldTemplateApplied;
        }
    }

    // The completion lies under the field's own text, in the same panel, so its typed part, drawn clear, keeps the grey part where the caret ends.
    private void OnFieldTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<TextPresenter>("PART_TextPresenter") is not { } presenter || presenter.GetVisualParent() is not Panel panel)
        {
            return;
        }

        (_completion.Parent as Panel)?.Children.Remove(_completion);
        _completion.Margin = presenter.Margin;
        _completion.HorizontalAlignment = presenter.HorizontalAlignment;
        _completion.VerticalAlignment = presenter.VerticalAlignment;
        _completion.Bind(TextBlock.FontFamilyProperty, presenter.GetObservable(TextElement.FontFamilyProperty));
        _completion.Bind(TextBlock.FontSizeProperty, presenter.GetObservable(TextElement.FontSizeProperty));
        _completion.Bind(TextBlock.LetterSpacingProperty, presenter.GetObservable(TextElement.LetterSpacingProperty));
        panel.Children.Insert(0, _completion);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            _dismissed = false;
            ShowCompletion();
        }
        else if (change.Property == SuggestionsProperty || change.Property == IsKeyboardFocusWithinProperty || change.Property == IsDropDownOpenProperty)
        {
            ShowCompletion();
        }
    }

    private void ShowCompletion()
    {
        var typed = Text ?? string.Empty;

        if (!IsKeyboardFocusWithin || IsDropDownOpen || Completion is not { } match)
        {
            _completion.IsVisible = false;
            return;
        }

        var rest = new Run(match[typed.Length..]);
        rest.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("KpTextMuted"));
        _completion.Inlines = [new Run(typed) { Foreground = Brushes.Transparent }, rest];
        _completion.IsVisible = true;
    }

    // Typing completes in the field; only Down lists the matches.
    private void OnDropDownOpening(object? sender, CancelEventArgs e) => e.Cancel = !_listAsked;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || IsDropDownOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Tab when CompletionShown is not null && Completion is { } match:
                Text = match;
                _field?.CaretIndex = match.Length;
                e.Handled = true;
                break;
            case Key.Down when Suggestions is { Count: > 0 }:
                _listAsked = true;
                IsDropDownOpen = true;
                e.Handled = true;
                break;
            case Key.Escape when CompletionShown is not null:
                _dismissed = true;
                ShowCompletion();
                e.Handled = true;
                break;
        }
    }
}
