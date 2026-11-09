using Keypaste.Core;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>
/// An agent's request that the person save a value, in front of the person at this desktop (F.79): they type, paste or
/// generate it, and Save writes it where the agent named; the agent never receives it.
/// </summary>
/// <remarks>
/// Nothing is entered for them. Save works a moment after the prompt is up, as every allow does, only once a value is
/// entered, and where it replaces a value only once they confirm that; Cancel, Escape and closing the window save nothing.
/// </remarks>
internal sealed partial class SaveValueViewModel : PromptViewModel, IDisposable
{
    private readonly Clipboard.ClipboardCountdown _clipboard;

    /// <param name="prompt">What the agent asked.</param>
    /// <param name="clipboard">Where a paste reads from.</param>
    /// <param name="clock">What time it is, for the clipboard.</param>
    internal SaveValueViewModel(SavePrompt prompt, Clipboard.IAppClipboard clipboard, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _clipboard = new Clipboard.ClipboardCountdown(clipboard, clock);
        Value = new SecretField(_clipboard);
        Value.PropertyChanged += (_, _) => Refresh();
        GenerateCommand = new RelayCommand(() => Value.Generate(SecretRecipe.Default), () => !IsAnswered);
        GeneratePassphraseCommand = new RelayCommand(() => Value.Generate(SecretRecipe.For(PassphraseRecipe.Default)), () => !IsAnswered);

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Where = prompt.Where;
        Field = prompt.Field;
        Replaces = prompt.Replaces;
        Reason = prompt.Reason;
        Scrubbed = prompt.WasAltered ? "The place or the reason is not what the agent sent: it was scrubbed." : string.Empty;
        Truncation = prompt.ReasonWasTruncated ? "Cut short: the full text is hashed in the audit log." : string.Empty;
        Placement = (prompt.Replaces, prompt.Creates, prompt.ForProject) switch
        {
            (true, _, _) => $"Replaces {Field} at {Where}. The old value stays in the item's history.",
            (_, true, false) => $"Creates the item {Where} and saves {Field} there.",
            (_, true, true) => $"Creates {Where}'s item and saves {Field} there.",
            _ => $"Adds {Field} to {Where}.",
        };
    }

    /// <summary>The value the person enters, never shown in full and never sent to the agent.</summary>
    internal SecretField Value { get; }

    /// <summary>Fills the value with a generated password.</summary>
    internal RelayCommand GenerateCommand { get; }

    /// <summary>Fills the value with a generated passphrase.</summary>
    internal RelayCommand GeneratePassphraseCommand { get; }

    /// <summary>Whether the person confirmed replacing the value the place holds.</summary>
    [ObservableProperty]
    internal partial bool ConfirmsReplace { get; set; }

    partial void OnConfirmsReplaceChanged(bool value) => Refresh();

    /// <summary>Whether saving replaces a value, which the person confirms first.</summary>
    internal bool Replaces { get; }

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    /// <summary>Where the value goes.</summary>
    internal string Where { get; }

    /// <summary>The field or variable it is saved as.</summary>
    internal string Field { get; }

    /// <summary>What saving does there, in keypaste's words.</summary>
    internal string Placement { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    /// <summary>Whose words the reason is, said under it whatever it says.</summary>
    internal string Attribution { get; } = ApprovalViewModel.AttributionText;

    /// <inheritdoc/>
    protected override string Asks => $"{Client} asks you to save a value at {Where}";

    /// <inheritdoc/>
    protected override bool CanAllow => Value.HasValue && (!Replaces || ConfirmsReplace);

    /// <inheritdoc/>
    protected override void OnAllowsChanged()
    {
        GenerateCommand.NotifyCanExecuteChanged();
        GeneratePassphraseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The value as entered, read once the person saved it.</summary>
    /// <returns>The value.</returns>
    internal string Composed() => Value.Compose();

    /// <inheritdoc/>
    public void Dispose()
    {
        Value.Dispose();
        _clipboard.Dispose();
    }
}
