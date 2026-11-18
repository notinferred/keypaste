using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The person picked a login in a page's list on a site it was not filled on before, or one every fill is asked on: Fill fills
/// it there once, and keypaste remembers the site unless it asks every time. No password is drawn.
/// </summary>
/// <remarks>The page and the build are what the extension claims, so the window says to fill only where they are signing in now.</remarks>
internal sealed class FillViewModel : PromptViewModel
{
    private readonly FillPrompt _prompt;

    internal FillViewModel(FillPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        QuestionBefore = $"Fill {prompt.Title} on ";
        Login = string.Join(" · ", new[] { prompt.Username, prompt.Vault }.Where(part => part.Length > 0));
        Identity = prompt.IdentityText;
        Pairing = prompt.Pairing;
        Note = prompt.Warning;
    }

    /// <summary>The question before its site: <c>Fill GitHub on </c>.</summary>
    internal string QuestionBefore { get; }

    /// <summary>The page's host, emphasised.</summary>
    internal string Site => _prompt.Site;

    /// <summary>The whole question, as a screen reader says it.</summary>
    internal string Question => _prompt.Question;

    /// <summary>The login's username and vault, those it has.</summary>
    internal string Login { get; }

    /// <summary>Whether the login has a username or a vault to show.</summary>
    internal bool HasLogin => Login.Length > 0;

    /// <summary>The build the browser named.</summary>
    internal string Identity { get; }

    /// <summary>The pairing's name.</summary>
    internal string Pairing { get; }

    /// <summary>The page's origin.</summary>
    internal string Origin => _prompt.Origin;

    /// <summary>Why this page is asked every time, such as its form sending to another site, or nothing.</summary>
    internal string Note { get; }

    /// <summary>Whether the page is asked every time.</summary>
    internal bool HasNote => Note.Length > 0;

    /// <inheritdoc/>
    protected override string Asks => $"A browser asks to fill {_prompt.Title} on {_prompt.Site}";

    /// <summary>Why the page and the build are claims.</summary>
    internal string Explainer { get; } =
        "The browser says which page asks, and any program on this computer can say it too. Fill only on a page you are signing in to now.";
}
