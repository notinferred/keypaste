using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One login on a fill prompt, as its row shows it: never its password, which is not read until the person fills it.</summary>
/// <param name="Title">Its title, sanitized.</param>
/// <param name="Details">Its username and group, those it has, sanitized.</param>
internal sealed record FillRow(string Title, string Details)
{
    /// <summary>What a screen reader says of the row: its names, never a value.</summary>
    internal string Spoken => Details.Length == 0 ? Title : $"{Title}, {Details}";
}

/// <summary>A browser's request to fill a login, in front of the person who clicked in the page: they pick the login and fill it once, or always on this origin.</summary>
/// <remarks>
/// Nothing is picked for them unless one login matches, and Fill cannot be pressed until one is picked and the prompt has armed.
/// The page and the build are what the extension claims, so the window says to fill only where they are signing in now.
/// </remarks>
internal sealed partial class FillViewModel : PromptViewModel
{
    private readonly FillPrompt _prompt;
    private int _picked = -1;
    private int _always;

    internal FillViewModel(FillPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        Rows = [.. prompt.Candidates.Select(candidate => new FillRow(candidate.Title, Details(candidate)))];
        Identity = prompt.IdentityText;
        Pairing = prompt.Pairing;
        var site = prompt.Origin.IndexOf(prompt.Site, StringComparison.Ordinal);
        (OriginBefore, OriginSite, OriginAfter) = site < 0
            ? (prompt.Origin, string.Empty, string.Empty)
            : (prompt.Origin[..site], prompt.Site, prompt.Origin[(site + prompt.Site.Length)..]);
        FormNote = prompt.FormOrigin is { } form ? $"This page's form sends what you fill to {form}, another site." : string.Empty;
        AlwaysText = prompt.AlwaysText;

        if (Rows.Count == 1)
        {
            Picked = Rows[0];
        }
    }

    /// <summary>The logins, in the vault's order.</summary>
    internal IReadOnlyList<FillRow> Rows { get; }

    /// <summary>The build the browser named.</summary>
    internal string Identity { get; }

    /// <summary>The pairing's name.</summary>
    internal string Pairing { get; }

    /// <summary>The origin before its site, such as <c>https://login.</c>.</summary>
    internal string OriginBefore { get; }

    /// <summary>The origin's site, emphasised.</summary>
    internal string OriginSite { get; }

    /// <summary>The origin after its site, such as <c>:443</c>.</summary>
    internal string OriginAfter { get; }

    /// <summary>The whole origin, as a screen reader says it.</summary>
    internal string Origin => _prompt.Origin;

    /// <summary>Where the form sends what is filled, when that is another site, or nothing.</summary>
    internal string FormNote { get; }

    /// <summary>Whether the form sends to another site.</summary>
    internal bool HasFormNote => FormNote.Length > 0;

    /// <summary>Whether the prompt offers to always fill the login here.</summary>
    internal bool ShowsAlways => _prompt.OffersAlways;

    /// <summary>The always box's words, saying how long it lasts.</summary>
    internal string AlwaysText { get; }

    /// <summary>The row the person picked, or null until they pick one.</summary>
    [ObservableProperty]
    internal partial FillRow? Picked { get; set; }

    partial void OnPickedChanged(FillRow? value)
    {
        // Only before the answer, which is a latch: what was picked when the person answered is what is filled.
        if (!IsAnswered)
        {
            Volatile.Write(ref _picked, value is null ? -1 : IndexOf(value));
        }

        Refresh();
    }

    /// <summary>Whether the person ticked always filling the login here.</summary>
    [ObservableProperty]
    internal partial bool Always { get; set; }

    partial void OnAlwaysChanged(bool value)
    {
        if (!IsAnswered)
        {
            Volatile.Write(ref _always, value && _prompt.OffersAlways ? 1 : 0);
        }
    }

    /// <summary>The UUID of the login picked as the person answered, or null; safe from any thread.</summary>
    internal string? PickedUuid => Volatile.Read(ref _picked) is >= 0 and var index ? _prompt.Candidates[index].Uuid : null;

    /// <summary>Whether always was ticked as the person answered; safe from any thread.</summary>
    internal bool AlwaysPicked => Volatile.Read(ref _always) == 1;

    protected override bool CanAllow => Picked is not null;

    /// <inheritdoc/>
    protected override string Asks => $"A browser asks to fill a login on {_prompt.Origin}";

    /// <summary>Why the page and the build are claims.</summary>
    internal string Explainer { get; } =
        "The browser says which page asks, and any program on this computer can say it too. Fill only on a page you are signing in to now.";

    private int IndexOf(FillRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Details(FillCandidate candidate) =>
        string.Join(" · ", new[] { candidate.Username, candidate.Group.Length > 0 ? candidate.Group : "top level" }.Where(part => part.Length > 0));
}
