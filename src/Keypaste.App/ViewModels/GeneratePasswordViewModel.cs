using Keypaste.App.Clipboard;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Generate password…, on its own (V.12): a generated password to copy, to start a new item with, or to add to the
/// chosen item as a protected field.
/// </summary>
/// <remarks>
/// The value lives in a <see cref="SecretField"/>, drawn from the generator straight into its buffer, and leaves it
/// only for the clearing clipboard, a reveal, or the field of the form that keeps it. Opened from a form's field, the
/// same panel offers Apply, which is the only way its value reaches that field.
/// </remarks>
internal sealed partial class GeneratePasswordViewModel : ObservableObject, IRevealSource, IDisposable
{
    private readonly ClipboardCountdown _clipboard;
    private readonly Action<SecretField>? _newItem;
    private readonly Action<SecretField, string>? _addField;
    private readonly Action _close;
    private readonly GeneratorViewModel? _choices;
    private readonly Action<SecretField>? _apply;
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

    /// <summary>The generator a form's field opens: its own value drawn by a copy of the form's choices, which Apply hands to the field with those choices and Cancel leaves.</summary>
    /// <param name="clipboard">The clipboard that clears itself.</param>
    /// <param name="choices">The form's choices, which the panel starts from and Apply sets to its own.</param>
    /// <param name="target">The form's field, which stays the form's: a generated value it holds is the one the panel starts with.</param>
    /// <param name="apply">Puts the value in the form's field.</param>
    /// <param name="newItem">Opens New item with the value as its password, or null where the form offers none.</param>
    /// <param name="addField">Adds the value as a protected field of the name given, or null where the form offers none.</param>
    /// <param name="item">The title of the item the value would be added to.</param>
    /// <param name="close">Closes the panel.</param>
    internal GeneratePasswordViewModel(
        ClipboardCountdown clipboard,
        GeneratorViewModel choices,
        SecretField target,
        Action<SecretField> apply,
        Action<SecretField>? newItem,
        Action<SecretField, string>? addField,
        string? item,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(close);

        _clipboard = clipboard;
        _newItem = newItem;
        _addField = addField;
        _close = close;
        _choices = choices;
        _apply = apply;
        Target = target;
        Item = item;
        Generator = new GeneratorViewModel();
        Generator.TakeChoicesFrom(choices);
        Value = new SecretField(clipboard);
        Generator.PropertyChanged += (_, _) => Regenerate();

        if (target is { IsGenerated: true, HasValue: true })
        {
            Value.TakeFrom(target);
            NotifyCommands();
        }
        else
        {
            Regenerate();
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    internal GeneratorViewModel Generator { get; }

    internal SecretField Value { get; }

    /// <summary>The form's field the panel was opened for, or null on its own.</summary>
    internal SecretField? Target { get; }

    /// <summary>Whether Apply hands the value to a form's field; on its own, the panel copies it instead.</summary>
    internal bool Applies => _apply is not null;

    internal bool OffersNewItem => _newItem is not null;

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
        Value.Dispose();
    }

    // Choices the generator refuses leave nothing to hand over, so no value made with earlier ones stays usable.
    private bool CanGenerate() => Generator.Error is null;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void Regenerate()
    {
        if (_disposed)
        {
            return;
        }

        if (Generator.Recipe is { } recipe)
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
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task CopyAsync() => _disposed || !Value.HasValue ? Task.CompletedTask : _clipboard.CopyAsync(Value.Compose(), "Generated password");

    private bool CanStartNewItem() => _newItem is not null && CanGenerate();

    [RelayCommand(CanExecute = nameof(CanStartNewItem))]
    private void NewItem() => _newItem?.Invoke(Value);

    private bool CanApply() => _apply is not null && CanGenerate();

    /// <summary>Puts the value in the form's field, with the choices it was made by, and closes.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_disposed || _apply is null)
        {
            return;
        }

        _choices?.TakeChoicesFrom(Generator);
        _apply(Value);
        _close();
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
