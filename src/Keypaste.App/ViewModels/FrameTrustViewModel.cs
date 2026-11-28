using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>A paired browser's question whether a sign-in frame of another site may be offered its own logins in a page, answered once.</summary>
/// <remarks>
/// Trusting is kept for the pairing until Settings › Browser fill removes it, so nothing on the card is the default: Return trusts
/// nothing, and Escape denies. The browser named itself when it started keypaste, so the requester line says Not verified.
/// </remarks>
internal sealed class FrameTrustViewModel : PromptViewModel
{
    private readonly FrameTrustPrompt _prompt;

    internal FrameTrustViewModel(FrameTrustPrompt prompt)
        : base(prompt is null ? null : new RequesterIdentity(prompt.Identity.Browser, "keypaste extension", false))
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _prompt = prompt;
        Title = prompt.Question;
        Explanation = prompt.Explanation;
        Frame = prompt.Frame;
        Top = prompt.Top;
        Pairing = prompt.Pairing;
    }

    /// <summary>The question: <c>Trust sign-in from idmsa.apple.com on www.icloud.com?</c>.</summary>
    internal string Title { get; }

    /// <summary>What trusting it does.</summary>
    internal string Explanation { get; }

    /// <summary>The frame's host.</summary>
    internal string Frame { get; }

    /// <summary>The host of the page it is in.</summary>
    internal string Top { get; }

    /// <summary>The pairing that asked.</summary>
    internal string Pairing { get; }

    /// <inheritdoc/>
    internal override string VerificationExplained =>
        $"keypaste can't check that {_prompt.Identity.Browser} is asking: any program on this computer can name keypaste's extension.";

    /// <inheritdoc/>
    internal override bool AnswersReturn => false;

    /// <inheritdoc/>
    protected override string Asks => $"{_prompt.Identity.Browser} asks whether to trust sign-in from {Frame} on {Top}";
}
