using Keypaste.Core.Approval;
using Keypaste.Core.Browser;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The person picked a login in a page's list on a site it was not filled on before, or one every fill is asked on: Fill fills
/// it there once, and keypaste remembers the site unless it asks every time. No password is drawn.
/// </summary>
/// <remarks>
/// The page and the build are what the extension claims, so the requester line says Not verified and the card says to fill only
/// where the person is signing in now. Return fills nothing.
/// </remarks>
internal sealed class FillViewModel : PromptViewModel
{
    private readonly FillPrompt _prompt;

    internal FillViewModel(FillPrompt prompt)
        : base(Extension(prompt?.Identity))
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        Item = string.Join(" \u00b7 ", new[] { prompt.Title, prompt.Vault }.Where(part => part.Length > 0));
        Login = prompt.Username;
        Identity = prompt.IdentityText;
        Pairing = prompt.Pairing;
        Note = prompt.Warning;
        Check = $"Fill only if you're signing in to {prompt.Site} now.";
    }

    /// <summary>Who the requester line says is asking: the browser and keypaste's extension, which keypaste cannot check.</summary>
    internal static RequesterIdentity? Extension(BrowserIdentity? identity) =>
        identity is null ? null : new(identity.Browser, "keypaste extension", false);

    /// <inheritdoc/>
    internal override string VerificationExplained => Explained(_prompt.Identity);

    /// <summary>What pointing at Not verified says on a browser's card.</summary>
    internal static string Explained(BrowserIdentity identity) =>
        $"keypaste can't check that {identity.Browser} is asking: any program on this computer can name keypaste's extension.";

    /// <summary>The page's host, wrapping only at its dots, so the whole host shows.</summary>
    internal string Site => _prompt.Site.Replace(".", ".\u200b", StringComparison.Ordinal);

    /// <summary>The whole question, as a screen reader says it.</summary>
    internal string Question => _prompt.Question;

    /// <summary>The login's title and vault, as the item line names it.</summary>
    internal string Item { get; }

    /// <summary>The login's username, or empty.</summary>
    internal string Login { get; }

    /// <summary>Whether the login has a username to show.</summary>
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
    internal override bool AnswersReturn => false;

    /// <inheritdoc/>
    protected override string Asks => $"A browser asks to fill {_prompt.Title} on {_prompt.Site}";

    /// <summary>What the person checks before filling, with the site.</summary>
    internal string Check { get; }

    /// <summary>Why the page and the build are claims, for Details.</summary>
    internal string Explainer { get; } = "The browser says which page asks, and any program on this computer can say it too.";
}
