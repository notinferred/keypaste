using Keypaste.App.Clipboard;
using Keypaste.Core.Agents;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Connect an AI tool, as a sheet over Agents: below the setup prompt the screen copies, the server entry for a tool set up
/// by hand under the name typed, and the tools found on this computer, each opening its own part of the sheet.
/// </summary>
/// <remarks>
/// The name is the label agents' settings are kept under, so sessions started with the same name share one set of rules,
/// and an entry without one uses the defaults. Nothing here writes a file; a tool's own part does, once asked.
/// </remarks>
internal sealed partial class ConnectSheetViewModel : ObservableObject
{
    private readonly ClipboardCountdown? _clipboard;
    private readonly Action<string, ToastTone> _toast;
    private readonly Action<AgentRow> _connect;

    /// <param name="found">The tools found on this computer that Connect… offers.</param>
    /// <param name="connect">Opens a found tool's own part of the sheet.</param>
    /// <param name="clipboard">Where Copy puts the entry, or null to copy nothing.</param>
    /// <param name="toast">Says what a copy did.</param>
    /// <param name="name">The name the entry starts with, or empty for none.</param>
    internal ConnectSheetViewModel(IReadOnlyList<AgentRow> found, Action<AgentRow> connect, ClipboardCountdown? clipboard, Action<string, ToastTone> toast, string name = "")
    {
        Found = found;
        _connect = connect;
        _clipboard = clipboard;
        _toast = toast;
        Name = name;
    }

    /// <summary>What Copy setup prompt puts on the clipboard, for an AI tool to connect itself.</summary>
    internal static string SetupPrompt => Resources.Strings.SetupPromptText;

    /// <summary>The tools found on this computer, each with what it is and Connect… where it is not connected.</summary>
    internal IReadOnlyList<AgentRow> Found { get; }

    internal bool HasFound => Found.Count > 0;

    /// <summary>The label the entry starts keypaste with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Config), nameof(NameProblem), nameof(HasNameProblem))]
    [NotifyCanExecuteChangedFor(nameof(CopyConfigCommand))]
    internal partial string Name { get; set; }

    /// <summary>Why the name can't be a label, or empty.</summary>
    internal string NameProblem => Name.Trim() is { Length: > 0 } name && (!AgentNames.IsNamed(name) || !AgentNames.IsValid(name, out _))
        ? Resources.Strings.ANameIsUpTo64CharactersWithNoQuoteOrSlash
        : string.Empty;

    internal bool HasNameProblem => NameProblem.Length > 0;

    /// <summary>The server entry under the name typed, or without a label while the name is empty.</summary>
    internal string Config => ServerEntry(HasNameProblem ? null : Name.Trim());

    /// <summary>The server block for any MCP client's configuration, with the label that gives it settings of its own.</summary>
    /// <param name="label">The label, or null or empty for an agent that uses the defaults.</param>
    internal static string ServerEntry(string? label)
    {
        List<string> args = label is { Length: > 0 } ? ["mcp", "--client-label", label] : ["mcp"];
        // A label holds no quote, backslash or control character, so each argument is a JSON string as it is.
        var arguments = string.Join(", ", args.Select(argument => $"\"{argument}\""));

        return $$"""
            {
              "mcpServers": {
                "keypaste": {
                  "command": "keypaste",
                  "args": [{{arguments}}]
                }
              }
            }
            """;
    }

    /// <summary>A label suggested from the name an agent gave: lower case, with a hyphen for each character a label can't hold.</summary>
    internal static string LabelFrom(string given)
    {
        ArgumentNullException.ThrowIfNull(given);

        var label = new string([.. given.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')]).Trim('-');
        label = label.Length > AgentNames.MaximumLength ? label[..AgentNames.MaximumLength] : label;

        return AgentNames.IsNamed(label) ? label : string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanCopyConfig))]
    private async Task CopyConfigAsync()
    {
        if (_clipboard is not null)
        {
            await _clipboard.CopyPlainAsync(Config, Resources.Strings.ConfigurationCopied).ConfigureAwait(true);
            _toast(Resources.Strings.ConfigurationCopied, ToastTone.Success);
        }
    }

    private bool CanCopyConfig() => !HasNameProblem;

    [RelayCommand]
    private void Connect(AgentRow? tool)
    {
        if (tool is { OffersConnect: true })
        {
            _connect(tool);
        }
    }
}
