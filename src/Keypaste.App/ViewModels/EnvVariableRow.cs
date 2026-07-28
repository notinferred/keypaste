namespace Keypaste.App.ViewModels;

/// <summary>
/// One row of a project's variable table: a name, a mask, and a way to see the value briefly.
/// </summary>
/// <remarks>
/// <para>
/// <b>The value is not a property of this object.</b> <see cref="Reveal"/> reads it out of the open
/// vault at the moment of the press and hands it to the control that draws it, which keeps it in a
/// private field until the finger comes up. A row that carried its value would put every variable's
/// value in memory, in a view model, for as long as the screen is open — the thing
/// <c>SecretHygieneTests</c> exists to catch.
/// </para>
/// <para>
/// <see cref="IsUsableName"/> comes from core rather than from a rule written here.
/// <c>keypaste run</c> cannot export a name outside the POSIX set, and a table that quietly showed
/// such a variable as ordinary would be the GUI disagreeing with the CLI about the same file.
/// </para>
/// </remarks>
internal sealed class EnvVariableRow : ObservableObject, IRevealSource
{
    private readonly EnvProjectViewModel _owner;
    private int _maskedLength;

    internal EnvVariableRow(EnvProjectViewModel owner, string key, int length, bool isUsableName)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _owner = owner;
        Key = key;
        _maskedLength = length;
        IsUsableName = isUsableName;

        CopyValueCommand = new AsyncRelayCommand(CopyAsync);
        RemoveCommand = new RelayCommand(() => _owner.BeginRemove(this));
    }

    /// <summary>The variable's name.</summary>
    internal string Key { get; }

    /// <summary>How long its value is, for the mask.</summary>
    public int MaskedLength => _maskedLength;

    /// <summary>Whether <c>keypaste run</c> could export this name.</summary>
    internal bool IsUsableName { get; }

    /// <summary>The sentence shown beside a name core would refuse to create.</summary>
    internal string Warning => IsUsableName
        ? string.Empty
        : "keypaste run cannot export this name.";

    /// <summary>What a screen reader is told the reveal button does. Names the key, never the value.</summary>
    internal string RevealLabel => $"Hold to reveal {Key}";

    /// <summary>Copies the value, with the auto-clearing countdown.</summary>
    internal AsyncRelayCommand CopyValueCommand { get; }

    /// <summary>Asks to remove this variable.</summary>
    internal RelayCommand RemoveCommand { get; }

    /// <inheritdoc/>
    public string? Reveal() => _owner.Reveal(this);

    /// <inheritdoc/>
    public void Conceal() => _owner.Conceal(this);

    /// <summary>Picks up a new length after the value changed.</summary>
    internal void Resize(int length) => Set(ref _maskedLength, length, nameof(MaskedLength));

    private async Task CopyAsync()
    {
        if (_owner.Read(Key) is not { } value)
        {
            _owner.Report("That value could not be read. The vault may have locked.");
            return;
        }

        await _owner.Clipboard.CopyAsync(value, Key).ConfigureAwait(true);
    }
}
