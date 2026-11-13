using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Clients;
using Keypaste.Core.Linking;

namespace Keypaste.Core.Browser;

/// <summary>The <c>keypaste</c> a manifest names, which is the one a terminal finds.</summary>
/// <param name="Path">Its absolute path.</param>
/// <param name="Prepare">Makes it before a manifest first names it, returning why it could not; null when it already exists.</param>
public sealed record HostProgram(string Path, Func<string?>? Prepare = null)
{
    /// <summary>The desktop's: <c>~/.local/bin/keypaste</c> on Linux, made when absent, else the <c>keypaste</c> beside the app, which is the MSI's per-user copy or the bundle's.</summary>
    /// <param name="linuxLink">The Linux link, from <see cref="CliLinks.ForThisProcess"/>; null elsewhere.</param>
    /// <param name="appDirectory">The desktop's own directory.</param>
    /// <param name="appImage">The AppImage the desktop runs from, or null.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <param name="program">The program, when there is one.</param>
    /// <param name="error">Why not.</param>
    /// <returns>Whether there is a program to name.</returns>
    public static bool TryForTheApp(
        ICliLink? linuxLink,
        string appDirectory,
        McpAppImage? appImage,
        string? pathVariable,
        [NotNullWhen(true)] out HostProgram? program,
        out string error)
    {
        if (linuxLink is not null)
        {
            program = new HostProgram(linuxLink.LinkPath, linuxLink.Read().State == CliLinkState.Absent ? () => Linked(linuxLink) : null);
            error = string.Empty;
            return true;
        }

        return From(McpServerLocator.TryForDesktop(appDirectory, appImage, pathVariable, out var target, out error), target, out program);
    }

    /// <summary>The CLI's: the running <c>keypaste</c>, or inside an AppImage its link.</summary>
    /// <param name="processPath">The running program.</param>
    /// <param name="appImage">The AppImage keypaste runs from, or null.</param>
    /// <param name="program">The program, when there is one.</param>
    /// <param name="error">Why not.</param>
    /// <returns>Whether there is a program to name.</returns>
    public static bool TryForTheCli(string? processPath, McpAppImage? appImage, [NotNullWhen(true)] out HostProgram? program, out string error) =>
        From(McpServerLocator.TryForSetup(processPath, appImage, out var target, out error), target, out program);

    private static bool From(bool found, McpBridgeTarget? target, [NotNullWhen(true)] out HostProgram? program)
    {
        program = found && target is not null
            ? new HostProgram(target.Server.Path, target.Link is { } link ? () => McpClientSetup.Apply(link) is { Status: McpSetupStatus.Done } ? null : $"{link.Link.LinkPath} could not be made" : null)
            : null;
        return program is not null;
    }

    private static string? Linked(ICliLink link)
    {
        var result = link.Link();
        return result.Outcome == CliLinkOutcome.Linked ? null : $"{link.LinkPath} could not be made: {result.Reason ?? result.Refusal?.ToString() ?? result.Outcome.ToString()}";
    }
}
