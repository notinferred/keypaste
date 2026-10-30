using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A browser's request to pair, in front of the person who armed pairing, allowed once under a name they may change (D-0634).</summary>
/// <remarks>
/// Allow is armed after the prompt has been up a moment, as every allow is, and only while the name is one a new pairing can
/// take. The build is what the browser named when it started keypaste, which any program can name, so the window says to
/// allow only a pairing the person started.
/// </remarks>
internal sealed class PairingViewModel : PromptViewModel
{
    private readonly PairingPrompt _prompt;
    private string _name;

    internal PairingViewModel(PairingPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        _name = prompt.SuggestedName;
        Identity = prompt.IdentityText;
        AllowCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => IsArmed && !IsAnswered && NameProblem is null);

        DependsOn(nameof(NameProblem), nameof(Name));
        DependsOn(nameof(HasNameProblem), nameof(NameProblem));
        DependsOn(AllowCommand, nameof(IsArmed), nameof(IsAnswered), nameof(Name));
    }

    /// <summary>Pairs the browser under <see cref="Name"/>.</summary>
    internal RelayCommand AllowCommand { get; }

    /// <summary>The build the browser named: <c>KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)</c>.</summary>
    internal string Identity { get; }

    /// <summary>The pairing's name, which becomes its id in the extension.</summary>
    internal string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>Why the name cannot be used, or null.</summary>
    internal string? NameProblem => _prompt.NameProblem(_name);

    internal bool HasNameProblem => NameProblem is not null;

    /// <summary>What allowing does, and why the build named is a claim.</summary>
    internal string Explainer { get; } =
        "The browser names this build when it starts keypaste, and any program on this computer can name it too. Allow only a pairing you started in your browser just now.";

    protected override int TimedSeconds => 0;
}
