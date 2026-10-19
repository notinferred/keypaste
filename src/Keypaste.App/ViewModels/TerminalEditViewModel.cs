using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A terminal's edit to the vault, in front of the person at this desktop, allowed once (D-0464).</summary>
/// <remarks>
/// Allow is armed after the prompt has been up a moment, as every allow is, and is the only answer that
/// writes anything. The command and where it came from are a claim: any program can type one. What
/// changes is keypaste's own words, naming items, fields and variables and never a value.
/// </remarks>
internal sealed class TerminalEditViewModel : PromptViewModel
{
    internal TerminalEditViewModel(TerminalEditPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        Command = prompt.Command;
        Item = prompt.Item;
        Origin = prompt.OriginText;
        Changes = prompt.Changes;
    }

    /// <summary>The command that asked, as a person types it.</summary>
    internal string Command { get; }

    /// <summary>The item, or the environment, sanitized.</summary>
    internal string Item { get; }

    /// <summary>Where the request says it came from, said as a claim.</summary>
    internal string Origin { get; }

    /// <summary>What changes, in keypaste's own words.</summary>
    internal IReadOnlyList<string> Changes { get; }

    /// <summary>What allowing does.</summary>
    internal string Explainer { get; } = "Allowing saves this in the vault now. Allow only a change you made yourself.";

    protected override int TimedSeconds => 0;
}
