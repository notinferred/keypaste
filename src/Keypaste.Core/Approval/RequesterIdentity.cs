using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>Who a prompt says is asking, and whether keypaste checked it with the operating system.</summary>
/// <remarks>
/// <para>
/// Verified only when the program on the owner's pipe is keypaste's own and the process that started it is a program of
/// the client its <c>--client-label</c> names, read from the system as the connection was accepted
/// (<see cref="PeerLineage"/>). The label picks which client to check for and is never itself the proof; a bridge with no
/// label, or a label no known client has, is not verified.
/// </para>
/// <para>
/// It decides nothing: the same rules apply whatever it says, and a process of the person's own account can still start
/// the real client with a configuration of its own (THREATS.md T-3).
/// </para>
/// </remarks>
/// <param name="Agent">The name shown: the AI tool's own client name, sanitized; the bridge's label is shown under Details.</param>
/// <param name="App">The app the asking program runs in, such as Terminal, shown only when verified; otherwise null.</param>
/// <param name="Verified">Whether keypaste checked who is asking.</param>
public sealed record RequesterIdentity(string Agent, string? App, bool Verified)
{
    /// <summary>The program on the connection and the one that started it, as the system named them, sanitized; null outside a connection.</summary>
    public string? Process { get; init; }

    private const int _maximumAppLength = 40;

    private static readonly HashSet<string> _passedThrough = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "zsh", "fish", "dash", "ksh", "tcsh", "csh", "nu", "login", "su", "sudo", "doas", "env",
        "cmd", "powershell", "pwsh", "conhost", "openconsole", "wsl", "wslhost", "tmux", "screen", "node", "bun", "deno",
        "python", "python3", "npx", "npm", "ptyxis-agent",
    };

    private static readonly HashSet<string> _roots = new(StringComparer.OrdinalIgnoreCase)
    {
        "launchd", "systemd", "init", "explorer", "sshd", "services", "wininit",
    };

    private static readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windowsterminal"] = "Windows Terminal",
        ["gnome-terminal-server"] = "GNOME Terminal",
        ["konsole"] = "Konsole",
        ["ptyxis"] = "Ptyxis",
        ["xfce4-terminal"] = "Xfce Terminal",
        ["wezterm-gui"] = "WezTerm",
        ["alacritty"] = "Alacritty",
        ["code"] = "VS Code",
        ["cursor"] = "Cursor",
        ["claude"] = "Claude",
    };

    /// <summary>What the requester line says after the agent: Verified or Not verified.</summary>
    public string Verification => Verified ? "Verified" : "Not verified";

    /// <summary>What pointing at <see cref="Verification"/> says, in plain words.</summary>
    public string Explanation => (Verified, App) switch
    {
        (true, null) => $"Checked by keypaste: the program asking is {Agent}.",
        (true, _) => $"Checked by keypaste: the program asking is {Agent}, run from {App}.",
        _ => $"keypaste could not check that {Agent} is asking. The name is the agent's own claim.",
    };

    /// <summary>The requester for a prompt built while answering a connection, checked against what the system said of it.</summary>
    /// <param name="agent">The name to show.</param>
    /// <param name="label">The bridge's label, which names the client to check for, or null.</param>
    /// <returns>The requester.</returns>
    internal static RequesterIdentity Current(string agent, string? label) => Of(PipeCaller.Lineage, agent, label);

    /// <summary>The requester for a runner's prompt, <c>keypaste run</c> itself, which is verified when it is keypaste's own program.</summary>
    /// <param name="agent">The name to show.</param>
    /// <returns>The requester.</returns>
    internal static RequesterIdentity CurrentRunner(string agent) => PipeCaller.Lineage is { } lineage
        ? new RequesterIdentity(agent, lineage.IsKeypaste ? AppOf(lineage.Ancestors) : null, lineage.IsKeypaste) { Process = ProcessOf(lineage) }
        : new RequesterIdentity(agent, null, false);

    /// <summary>Checks a requester against <paramref name="lineage"/>.</summary>
    /// <param name="lineage">What the system said of the connection, or null outside one.</param>
    /// <param name="agent">The name to show.</param>
    /// <param name="label">The bridge's label, or null.</param>
    /// <returns>The requester, verified only as this type says.</returns>
    internal static RequesterIdentity Of(PeerLineage? lineage, string agent, string? label)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var verified = lineage is { IsKeypaste: true, Ancestors: [var parent, ..] }
            && label is not null
            && McpClientCatalog.Find(label) is { } client
            && (Runs(client, parent) || RunsPackage(client, parent, lineage.ParentScript));

        return new RequesterIdentity(agent, verified ? AppOf(lineage!.Ancestors) : null, verified) { Process = lineage is null ? null : ProcessOf(lineage) };
    }

    /// <summary>The connected program and the one that started it, on one line, as Details shows them.</summary>
    private static string? ProcessOf(PeerLineage lineage) => (lineage.Executable, lineage.Ancestors) switch
    {
        (null, _) => null,
        (var program, [var parent, ..]) => DisplayTextSanitizer.Sanitize($"{program}, started by {parent}", DisplayTextSanitizer.MaximumLength).Text,
        (var program, _) => DisplayTextSanitizer.Sanitize(program, DisplayTextSanitizer.MaximumLength).Text,
    };

    /// <summary>Whether <paramref name="executable"/> is one of <paramref name="client"/>'s own programs.</summary>
    internal static bool Runs(McpClient client, string executable)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(executable);

        var path = executable.Replace('\\', '/');
        var name = PeerLineage.ProgramName(path);

        return client.Programs.Any(program => program.EndsWith('*')
                ? name.StartsWith(program[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, program, StringComparison.OrdinalIgnoreCase))
            || client.ProgramFolders.Any(folder => path.Contains(folder, PathIdentity.Comparison))
            || (client.MacApp is { } bundle && path.Contains("/" + bundle + "/", PathIdentity.Comparison));
    }

    /// <summary>Whether <paramref name="host"/> is Node or Bun running a script from inside one of <paramref name="client"/>'s installed packages.</summary>
    internal static bool RunsPackage(McpClient client, string host, string? script)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(host);

        return script is not null
            && PeerLineage.ScriptHosts.Contains(PeerLineage.ProgramName(host))
            && client.Packages.Any(package => script.Replace('\\', '/').Contains($"/node_modules/{package}/", PathIdentity.Comparison));
    }

    /// <summary>
    /// The app a program runs in: the first of its ancestors inside a macOS app bundle, or else the first above its parent
    /// that is not a shell or another program commands pass through, or null when the walk reaches the system's own.
    /// </summary>
    internal static string? AppOf(IReadOnlyList<string> ancestors)
    {
        ArgumentNullException.ThrowIfNull(ancestors);

        foreach (var ancestor in ancestors)
        {
            var path = ancestor.Replace('\\', '/');

            if (path.IndexOf(".app/", StringComparison.Ordinal) is > 0 and var end)
            {
                return Shown(path[(path.LastIndexOf('/', end) + 1)..end]);
            }
        }

        foreach (var ancestor in ancestors.Skip(1))
        {
            var name = PeerLineage.ProgramName(ancestor.Replace('\\', '/'));

            if (_roots.Contains(name))
            {
                return null;
            }

            if (!_passedThrough.Contains(name))
            {
                return Shown(_appNames.GetValueOrDefault(name, name));
            }
        }

        return null;
    }

    private static string? Shown(string name) =>
        EntryNameSanitizer.Sanitize(name, _maximumAppLength).Text is { Length: > 0 } text ? text : null;
}
