using Keypaste.App.Clipboard;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Generate password…, on its own (V.12): a generated password to copy, to start a new item with, or to add to the
/// chosen item as a protected field.
/// </summary>
/// <remarks>
/// The value lives in a <see cref="SecretField"/>, drawn from the generator straight into its buffer, and leaves it
/// only for the clearing clipboard, a reveal, or the field of the form that keeps it. Anchored to a form's password
/// field, the panel instead shows that field's own options and acts on the value the field holds, which the form draws.
/// </remarks>
internal sealed partial class GeneratePasswordViewModel : ObservableObject, IRevealSource, IDisposable
{
    private readonly ClipboardCountdown _clipboard;
    private readonly Action<SecretField> _newItem;
    private readonly Action<SecretField, string>? _addField;
    private readonly Action _close;
    private readonly Action? _refresh;
    private bool _disposed;

    /// <param name="clipboard">The clipboard that clears itself.</param>
    /// <param name="newItem">Opens New item with the value as its password.</param>
    /// <param name="addField">Adds the value to the chosen item as a protected field of the name given, or null with no item chosen.</param>
    /// <param name="item">The chosen item's title, or null.</param>
    /// <param name="close">Closes the dialog.</param>
    internal GeneratePasswordViewModel(
        ClipboardCountdown clipboard,
        Action<SecretField> newItem,
        Action<SecretField, string>? addField,
        string? item,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(newItem);
        ArgumentNullException.ThrowIfNull(close);

        _clipboard = clipboard;
        _newItem = newItem;
        _addField = addField;
        _close = close;
        Item = item;
        Generator = new GeneratorViewModel();
        Value = new SecretField(clipboard);
        Generator.PropertyChanged += (_, _) => Regenerate();
        Regenerate();
    }

    /// <summary>The panel anchored to a form's field: the form's generator and the value its field holds, which the form fills.</summary>
    /// <param name="clipboard">The clipboard that clears itself.</param>
    /// <param name="generator">The options the form draws its value by.</param>
    /// <param name="field">The form's field, which stays the form's and is not disposed with the panel.</param>
    /// <param name="refresh">Draws a new value into the field.</param>
    /// <param name="newItem">Opens New item with the value as its password.</param>
    /// <param name="addField">Adds the value as a protected field of the name given.</param>
    /// <param name="item">The title of the item the value would be added to.</param>
    /// <param name="close">Closes the panel.</param>
    internal GeneratePasswordViewModel(
        ClipboardCountdown clipboard,
        GeneratorViewModel generator,
        SecretField field,
        Action refresh,
        Action<SecretField> newItem,
        Action<SecretField, string>? addField,
        string? item,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(newItem);
        ArgumentNullException.ThrowIfNull(close);

        _clipboard = clipboard;
        _newItem = newItem;
        _addField = addField;
        _close = close;
        _refresh = refresh;
        Item = item;
        Generator = generator;
        Value = field;
        ShowsValue = false;
        Generator.PropertyChanged += OnAnchoredGeneratorChanged;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    internal GeneratorViewModel Generator { get; }

    internal SecretField Value { get; }

    /// <summary>Whether the panel draws the value itself; a form's field already does.</summary>
    internal bool ShowsValue { get; } = true;

    /// <summary>The chosen item's title, which Add as a field adds to.</summary>
    internal string? Item { get; }

    internal bool CanAddField => _addField is not null;

    /// <summary>What Add as a field calls the new field.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFieldCommand))]
    internal partial string FieldName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public int MaskedLength => Value.MaskedLength;

    /// <inheritdoc/>
    public string? Reveal() => _disposed || !Value.HasValue ? null : Value.Compose();

    /// <inheritdoc/>
    public void Conceal()
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_refresh is null)
        {
            Value.Dispose();
        }
        else
        {
            Generator.PropertyChanged -= OnAnchoredGeneratorChanged;
        }
    }

    // The form draws the field's value on each choice; the panel only follows whether its commands still apply.
    private void OnAnchoredGeneratorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotifyCommands();

    // Choices the generator refuses leave nothing to hand over, so no value made with earlier ones stays usable.
    private bool CanGenerate() => Generator.Error is null;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Regenerate()
    {
        if (_disposed)
        {
            return;
        }

        if (_refresh is not null)
        {
            _refresh();
        }
        else if (Generator.Recipe is { } recipe)
        {
            Value.Generate(recipe);
        }
        else
        {
            Value.Clear();
        }

        OnPropertyChanged(nameof(MaskedLength));
        Changed?.Invoke(this, EventArgs.Empty);
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RegenerateCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        NewItemCommand.NotifyCanExecuteChanged();
        AddFieldCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task CopyAsync() => _disposed || !Value.HasValue ? Task.CompletedTask : _clipboard.CopyAsync(Value.Compose(), "Generated password");

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void NewItem()
    {
        _newItem(Value);
    }

    private bool CanAdd() => _addField is not null && FieldName.Trim().Length > 0 && CanGenerate();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddField()
    {
        _addField?.Invoke(Value, FieldName.Trim());
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}
