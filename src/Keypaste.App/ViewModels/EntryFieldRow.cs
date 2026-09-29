using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One custom field in the entry pane: its name, whether it is protected, a mask, and a way to see
/// or copy the value briefly.
/// </summary>
/// <remarks>
/// <para>
/// <b>The value is not a property of this object</b>, whether or not the field is protected.
/// <see cref="Reveal"/> reads it out of the open vault at the moment of the press, as
/// <see cref="EnvVariableRow"/> does, and the mask is a fixed width so nothing is read to draw it.
/// KeePassXC protects a new attribute only when asked, so a plain field is as likely as a protected
/// one to hold an API key.
/// </para>
/// <para>
/// A field keypaste does not write (<see cref="EntryField.IsReadOnly"/>), such as KeePassXC's
/// <c>otp</c>, is revealed and copied like any other and offers nothing that changes it.
/// </para>
/// </remarks>
internal sealed class EntryFieldRow : ObservableObject, IRevealSource
{
    /// <summary>How many dots every field's mask draws, whatever its length.</summary>
    internal const int MaskWidth = 12;

    private readonly EntryDetailViewModel _owner;

    internal EntryFieldRow(EntryDetailViewModel owner, EntryField field)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(field);

        _owner = owner;
        Name = field.Name;
        IsProtected = field.IsProtected;
        IsReadOnly = field.IsReadOnly;

        CopyValueCommand = new AsyncRelayCommand(CopyAsync);
        ReplaceCommand = new RelayCommand(() => _owner.BeginReplaceField(this), () => !IsReadOnly);
        ToggleProtectionCommand = new RelayCommand(() => _owner.ToggleProtection(this), () => !IsReadOnly);
        RemoveCommand = new RelayCommand(() => _owner.BeginRemoveField(this), () => !IsReadOnly);
    }

    /// <summary>The field's name, as the vault holds it. Addresses the field on every act.</summary>
    internal string Name { get; }

    /// <summary>The name as the pane draws it.</summary>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    internal bool IsProtected { get; }

    /// <summary>Whether keypaste leaves the field to KeePassXC.</summary>
    internal bool IsReadOnly { get; }

    /// <summary>Whether the field can be changed from here.</summary>
    internal bool IsWritable => !IsReadOnly;

    /// <summary>What the row says about the field beside its name.</summary>
    internal string Kind => IsReadOnly ? "KeePassXC" : IsProtected ? "protected" : "plain";

    /// <inheritdoc/>
    public int MaskedLength => MaskWidth;

    /// <summary>What a screen reader is told the reveal does. Names the field, never the value.</summary>
    internal string RevealLabel => $"Hold to reveal {DisplayName}";

    /// <summary>What the protection button does now.</summary>
    internal string ProtectionLabel => IsProtected ? $"Stop protecting {DisplayName}" : $"Protect {DisplayName}";

    internal AsyncRelayCommand CopyValueCommand { get; }

    internal RelayCommand ReplaceCommand { get; }

    internal RelayCommand ToggleProtectionCommand { get; }

    internal RelayCommand RemoveCommand { get; }

    /// <inheritdoc/>
    public string? Reveal() => _owner.ReadField(Name);

    /// <inheritdoc/>
    /// <remarks>The value was only ever the control's; there is nothing here to give back.</remarks>
    public void Conceal()
    {
    }

    private async Task CopyAsync()
    {
        if (_owner.ReadField(Name) is not { Length: > 0 } value)
        {
            _owner.Report($"{DisplayName} is empty, or could not be read. The vault may have locked.");
            return;
        }

        await _owner.Clipboard.CopyAsync(value, DisplayName).ConfigureAwait(true);
    }
}
