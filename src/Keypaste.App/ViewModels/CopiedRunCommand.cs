using System.Text;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The run command the Projects screen copies, quoted so that every shell commonly used on the platform hands
/// <c>keypaste</c> the project whole and runs no part of its name, which is vault text (T-1): Command Prompt,
/// PowerShell and Git Bash on Windows, and <c>sh</c>, <c>bash</c>, <c>zsh</c>, <c>fish</c> and PowerShell elsewhere.
/// A name one of them would run or change however it is quoted gets no command (D-0453).
/// </summary>
internal static class CopiedRunCommand
{
    /// <summary>What the card says in place of the command for a project whose name gets none.</summary>
    internal const string NoCommandNote =
        "This project's name holds characters a terminal could run, so keypaste offers no command to copy. Rename the project, or use Run in a terminal below.";

    /// <summary>The line for this platform's shells, or null when the project's name gets none.</summary>
    /// <param name="project">The project, as the vault holds it.</param>
    /// <param name="environment">The environment it names with <c>-p</c>, or null for none.</param>
    /// <param name="command">What follows <c>--</c>, which may be empty for the person to finish.</param>
    /// <returns>The line, or null.</returns>
    internal static string? For(string project, string? environment, string command) =>
        For(project, environment, command, OperatingSystem.IsWindows());

    /// <summary>The line for Windows' shells or for the others, or null when the project's name gets none.</summary>
    internal static string? For(string project, string? environment, string command, bool windows)
    {
        if (!IsCopyable(project, windows))
        {
            return null;
        }

        var profile = environment is null ? string.Empty : $"-p {environment} ";
        return $"keypaste run {profile}{ProjectArgument(project, windows)} -- {command}";
    }

    /// <summary>
    /// Whether the name can be quoted for every shell the line is for. On Windows double quotes are the only ones
    /// Command Prompt has, and a name holding <c>%</c>, which it reads inside them, <c>$</c> or a backtick, which
    /// PowerShell and Git Bash read there, <c>!</c>, which Git Bash does, or a double quote, typographic ones included,
    /// which ends them in PowerShell, cannot be. Elsewhere single quotes hold any project name, which never holds the
    /// backslash <c>fish</c> reads inside them.
    /// </summary>
    internal static bool IsCopyable(string project, bool windows) =>
        !windows || !project.Any(c => c is '%' or '$' or '`' or '!' || IsDoubleQuote(c));

    /// <summary>
    /// The project as the line names it: by <c>--project=</c> when the name starts with <c>-</c>, which
    /// <c>keypaste run</c> would read as an option, and quoted unless the name holds only ASCII letters, digits,
    /// <c>_</c>, <c>.</c> and <c>-</c>.
    /// </summary>
    private static string ProjectArgument(string project, bool windows)
    {
        var option = project.StartsWith('-') ? "--project=" : string.Empty;

        if (project.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
        {
            return option + project;
        }

        if (windows)
        {
            return "\"" + option + project + "\"";
        }

        if (!project.Any(IsSingleQuote))
        {
            return "'" + option + project + "'";
        }

        // Every shell here reads a run of single quotes, or of the typographic ones PowerShell takes for them, alone in
        // double quotes and every other character in single quotes; the unquoted --project= in front keeps PowerShell
        // reading the pieces as one argument.
        var word = new StringBuilder("--project=");
        var start = 0;

        while (start < project.Length)
        {
            var quotes = IsSingleQuote(project[start]);
            var end = start;

            while (end < project.Length && IsSingleQuote(project[end]) == quotes)
            {
                end++;
            }

            var mark = quotes ? '"' : '\'';
            word.Append(mark).Append(project, start, end - start).Append(mark);
            start = end;
        }

        return word.ToString();
    }

    private static bool IsSingleQuote(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';

    private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”' or '„';
}
