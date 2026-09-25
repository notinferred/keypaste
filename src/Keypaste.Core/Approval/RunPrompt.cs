using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>One variable a run would inject, as a person is shown it: names, never a value.</summary>
/// <param name="Name">The variable name the command sees.</param>
/// <param name="Entry">Where its value lives, as <see cref="ApprovalPrompt.Shown"/> writes it.</param>
/// <param name="Field">Which field of that entry.</param>
public sealed record RunPromptVariable(string Name, string Entry, string Field)
{
    /// <summary>Whether the name changes how programs start, which the prompt warns about.</summary>
    public bool ChangesHowProgramsStart => RunRequestRules.ChangesHowProgramsStart(Name);
}

/// <summary>
/// What a person is shown before an agent's command is started with secrets in its environment:
/// who asks, the exact program and command line, where, which variables and why.
/// </summary>
/// <remarks>
/// <para>
/// The command and directory reach here only when <see cref="RunRequestRules"/> found nothing in them
/// a prompt would have to scrub, so what is shown is what runs. The program is the absolute path the
/// bridge resolved and will start, which is what an agent-written <c>./npm</c> cannot hide behind.
/// </para>
/// <para>
/// Like <see cref="ApprovalPrompt"/> it has nowhere to express a default button, a deadline or a
/// layout; the gate owns the deadline.
/// </para>
/// </remarks>
public sealed record RunPrompt
{
    /// <summary>The tool an agent called, as the prompt names it.</summary>
    public const string ToolName = "keypaste.run";

    /// <summary>What the requesting client calls itself, sanitized, or <c>an unnamed client</c>.</summary>
    public required string Client { get; init; }

    /// <summary>The bridge's <c>--client-label</c>, sanitized, or null.</summary>
    public string? Label { get; init; }

    /// <summary>The agent's reason, sanitized and capped as a credential prompt's is.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason shown is shorter than the one the agent sent.</summary>
    public required bool ReasonWasTruncated { get; init; }

    /// <summary>Whether the reason had anything scrubbed out of it.</summary>
    public required bool ReasonWasAltered { get; init; }

    /// <summary>The absolute path of the program that starts.</summary>
    public required string Program { get; init; }

    /// <summary>The command as one line, exactly as the agent named it.</summary>
    public required string Command { get; init; }

    /// <summary>The directory it starts in, with every link resolved.</summary>
    public required string Directory { get; init; }

    /// <summary>The project, or <see cref="EnvReferenceResolution.MixedProject"/> for references.</summary>
    public required string Project { get; init; }

    /// <summary>The profile, or <see cref="EnvReferenceResolution.MixedProfile"/> for references across profiles.</summary>
    public required string Profile { get; init; }

    /// <summary>The variables, in the order the command receives them.</summary>
    public required IReadOnlyList<RunPromptVariable> Variables { get; init; }

    /// <summary>How long the timed grant lasts if the person chooses it; zero offers only "allow once".</summary>
    public int GrantSeconds { get; init; }

    /// <summary>Why <see cref="GrantSeconds"/> is zero, when it is.</summary>
    public OnceOnly OnceOnly { get; init; }

    /// <summary>Builds the prompt for a request <see cref="RunRequestRules"/> accepted.</summary>
    /// <param name="request">The request.</param>
    /// <param name="project">The project, or the mixed name for references.</param>
    /// <param name="profile">The profile, or the mixed name.</param>
    /// <param name="variables">The variables and where each comes from.</param>
    /// <param name="grantSeconds">The timed grant offered, or zero.</param>
    /// <param name="onceOnly">Why none is offered.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static RunPrompt For(
        RunRequest request,
        string project,
        string profile,
        IReadOnlyList<RunPromptVariable> variables,
        int grantSeconds,
        OnceOnly onceOnly)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(variables);

        var named = ApprovalPrompt.For(request.ClientName, new EntryName(string.Empty, project), "password", request.Reason, 0, request.ClientLabel);

        return new RunPrompt
        {
            Client = named.Client,
            Label = named.Label,
            Reason = named.Reason,
            ReasonWasTruncated = named.ReasonWasTruncated,
            ReasonWasAltered = named.ReasonWasAltered,
            Program = request.Program,
            Command = EnvReleasePrompt.CommandLine(request.Command),
            Directory = request.Directory,
            Project = EntryNameSanitizer.Sanitize(project).Text,
            Profile = EntryNameSanitizer.Sanitize(profile).Text,
            Variables = variables,
            GrantSeconds = grantSeconds,
            OnceOnly = onceOnly,
        };
    }
}
