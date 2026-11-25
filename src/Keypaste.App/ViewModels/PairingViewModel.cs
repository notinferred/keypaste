using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A browser's request to pair, in front of the person with the code its extension shows, allowed once under a name they may change.</summary>
/// <remarks>
/// Pair can be pressed only while the name is one a new pairing can take, and a key presses it only once the prompt is armed,
/// as with every allow. The build is what the browser named when it started keypaste, which any program can name, so the window asks the
/// person to pair only when the code matches the one their browser shows, and its requester line says Not verified. Return pairs nothing.
/// </remarks>
internal sealed class PairingViewModel : PromptViewModel
{
    private readonly PairingPrompt _prompt;
    private string _name;

    internal PairingViewModel(PairingPrompt prompt)
        : base(FillViewModel.Extension(prompt?.Identity))
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        _name = prompt.SuggestedName;
        Identity = prompt.IdentityText;
        Code = prompt.Code;
        Title = $"Pair {prompt.Identity.Browser} with keypaste?";
        Check = $"Pair only if {prompt.Identity.Browser} shows the same code.";
    }

    /// <summary>The question: <c>Pair Chrome with keypaste?</c>.</summary>
    internal string Title { get; }

    /// <summary>The six digits the extension shows too, as <c>472 913</c>.</summary>
    internal string Code { get; }

    /// <summary>What the person compares before pairing.</summary>
    internal string Check { get; }

    /// <summary>The build the browser named: <c>keypaste's extension from the Chrome Web Store (kabnajpeogcbndlfgibmhiichfkgpdla)</c>.</summary>
    internal string Identity { get; }

    /// <summary>The pairing's name, which becomes its id in the extension.</summary>
    internal string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NameProblem));
                OnPropertyChanged(nameof(HasNameProblem));
                Refresh();
            }
        }
    }

    /// <inheritdoc/>
    internal override string VerificationExplained => FillViewModel.Explained(_prompt.Identity);

    /// <inheritdoc/>
    internal override bool AnswersReturn => false;

    /// <inheritdoc/>
    protected override bool CanAllow => NameProblem is null;

    /// <inheritdoc/>
    protected override string Asks => $"{_prompt.Identity.Browser} asks to pair, showing the code {Code}";

    /// <summary>Why the name cannot be used, or null.</summary>
    internal string? NameProblem => _prompt.NameProblem(_name);

    internal bool HasNameProblem => NameProblem is not null;
}
