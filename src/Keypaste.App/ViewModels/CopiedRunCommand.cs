using System.Text;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The run command the Environments screen copies, quoted so that every shell commonly used on the platform hands
/// <c>keypaste</c> the vault and the project whole and runs no part of either, the project's name being vault text (T-1):
/// Command Prompt, PowerShell and Git Bash on Windows, and <c>sh</c>, <c>bash</c>, <c>zsh</c>, <c>fish</c> and PowerShell
/// elsewhere. A vault or a name one of them would run or change however it is quoted gets no command, and the line
/// carries <c>--session</c>, so the run asks the app holding the vault and reads no master password.
/// </summary>
internal static class CopiedRunCommand
{
    /// <summary>What the card says in place of the command for a project whose name gets none.</summary>
    internal const string NoCommandNote =
        "This project's name holds characters a terminal could run, so keypaste offers no command to copy. Rename the project, or use Run in a terminal below.";

    /// <summary>What the card says in place of the command when the vault's file path gets none.</summary>
    internal const string NoCommandForVaultNote =
        "This vault's file path holds characters a terminal could run or change, so keypaste offers no command to copy. Move or rename the vault file, or use Run in a terminal below.";

    /// <summary>The line for this platform's shells, or null when the vault's path or the project's name gets none.</summary>
    /// <param name="vault">The vault's file, which the line names so the run asks the app session holding this vault.</param>
    /// <param name="project">The project, as the vault holds it.</param>
    /// <param name="environment">The environment it names with <c>-p</c>, or null for none.</param>
    /// <param name="command">What follows <c>--</c>, which may be empty for the person to finish.</param>
    /// <returns>The line, or null.</returns>
    internal static string? For(string vault, string project, string? environment, string command) =>
        For(vault, project, environment, command, OperatingSystem.IsWindows());

    /// <summary>The line for Windows' shells or for the others, or null when the vault's path or the project's name gets none.</summary>
    internal static string? For(string vault, string project, string? environment, string command, bool windows)
    {
        if (!IsCopyableVault(vault, windows) || !IsCopyable(project, windows))
        {
            return null;
        }

        var profile = environment is null ? string.Empty : $"-p {environment} ";
        return $"keypaste run --session {VaultArgument(vault, windows)} {profile}{ProjectArgument(project, windows)} -- {command}";
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
    /// Whether the vault's path can be quoted for every shell the line is for: by the name's rules, and with no control
    /// character, which a terminal may act on as it is pasted. On Windows Git Bash also reads two backslashes inside double
    /// quotes as one, as a network share's path starts, and a closing backslash as escaping the quote. Elsewhere a path,
    /// unlike a name, may hold a backslash, which <c>fish</c> reads inside single quotes.
    /// </summary>
    internal static bool IsCopyableVault(string vault, bool windows) =>
        !vault.Any(char.IsControl)
        && (windows
            ? IsCopyable(vault, windows: true) && !vault.Contains(@"\\", StringComparison.Ordinal) && !vault.EndsWith('\\')
            : !vault.Contains('\\', StringComparison.Ordinal));

    /// <summary>The vault as the line names it: <c>--vault</c> and its path quoted, or as <see cref="Pieces"/> when single quotes cannot hold it.</summary>
    private static string VaultArgument(string vault, bool windows)
    {
        if (windows)
        {
            return "--vault \"" + vault + "\"";
        }

        return vault.Any(IsSingleQuote) ? Pieces("--vault=", vault) : "--vault '" + vault + "'";
    }

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

        return project.Any(IsSingleQuote) ? Pieces("--project=", project) : "'" + option + project + "'";
    }

    /// <summary>
    /// <paramref name="option"/> and <paramref name="value"/> as one word for the shells other than Windows', for a value
    /// holding single quotes.
    /// </summary>
    private static string Pieces(string option, string value)
    {
        // Every shell here reads a run of single quotes, or of the typographic ones PowerShell takes for them, alone in
        // double quotes and every other character in single quotes; the unquoted option in front keeps PowerShell
        // reading the pieces as one argument.
        var word = new StringBuilder(option);
        var start = 0;

        while (start < value.Length)
        {
            var quotes = IsSingleQuote(value[start]);
            var end = start;

            while (end < value.Length && IsSingleQuote(value[end]) == quotes)
            {
                end++;
            }

            var mark = quotes ? '"' : '\'';
            word.Append(mark).Append(value, start, end - start).Append(mark);
            start = end;
        }

        return word.ToString();
    }

    private static bool IsSingleQuote(char c) => c is '\'' or '‘' or '’' or '‚' or '‛';

    private static bool IsDoubleQuote(char c) => c is '"' or '“' or '”' or '„';
}
