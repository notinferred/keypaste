using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Login;

/// <summary>Opens at login on macOS through a LaunchAgent in the person's own Library.</summary>
/// <remarks>SMAppService would need a signed bundle and Objective-C interop; a LaunchAgent works for an unsigned app and is a plain file.</remarks>
public sealed class MacLaunchAgent : ILoginItem
{
    /// <summary>The agent's label, which is also the app bundle's identifier.</summary>
    public const string Label = "com.keypaste.app";

    private readonly IStartupApproval _approval;

    /// <summary>Initializes the agent for one program.</summary>
    /// <param name="home">The person's home directory.</param>
    /// <param name="program">The executable a login starts.</param>
    /// <param name="approval">Where launchd keeps its own switch for the agent.</param>
    public MacLaunchAgent(string home, string program, IStartupApproval approval)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(approval);

        _approval = approval;
        FilePath = Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
        Document = string.Join(
            '\n',
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
            "<plist version=\"1.0\">",
            "<dict>",
            "  <key>Label</key>",
            $"  <string>{Label}</string>",
            "  <key>ProgramArguments</key>",
            "  <array>",
            $"    <string>{SecurityElement.Escape(program)}</string>",
            $"    <string>{LoginItems.BackgroundFlag}</string>",
            "  </array>",
            "  <key>RunAtLoad</key>",
            "  <true/>",
            "  <key>LimitLoadToSessionType</key>",
            "  <string>Aqua</string>",
            "  <key>ProcessType</key>",
            "  <string>Interactive</string>",
            "</dict>",
            "</plist>",
            string.Empty);
    }

    /// <summary>Gets where the agent is written.</summary>
    public string FilePath { get; }

    /// <summary>Gets the property list the agent holds.</summary>
    public string Document { get; }

    /// <inheritdoc/>
    public bool IsEnabled =>
        string.Equals(LoginItems.ReadFile(FilePath), Document, StringComparison.Ordinal) && !_approval.IsDisabled(Label);

    /// <inheritdoc/>
    /// <remarks>launchd is asked to forget its switch only when it holds one, so a Mac whose launchctl cannot answer still writes the agent.</remarks>
    public bool Enable() =>
        LoginItems.WriteFile(FilePath, Document) && (!_approval.IsDisabled(Label) || _approval.Clear(Label));

    /// <inheritdoc/>
    public bool Disable() => LoginItems.DeleteFile(FilePath);
}

/// <summary>launchd's own switch for an agent in the person's login session, which turns it off without removing the agent's file.</summary>
public sealed class LaunchdApproval : IStartupApproval
{
    private const string _launchctl = "/bin/launchctl";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    // Nothing is written to launchctl's input, not even a preamble.
    private static readonly Encoding _noPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IProcessRunner _runner;
    private readonly string _domain;

    /// <summary>Initializes access to one user's login session.</summary>
    /// <param name="runner">Runs <c>launchctl</c>.</param>
    /// <param name="uid">The user's numeric id.</param>
    public LaunchdApproval(IProcessRunner runner, uint uid)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _domain = string.Create(CultureInfo.InvariantCulture, $"gui/{uid}");
    }

    /// <summary>The switches of this process's user.</summary>
    /// <returns>Access to the current user's login session through the system's <c>launchctl</c>.</returns>
    public static LaunchdApproval ForThisUser() => new(new SystemProcessRunner(), getuid());

    /// <inheritdoc/>
    /// <remarks>Read from <c>launchctl print-disabled</c>; a listing it cannot give reads as not turned off.</remarks>
    public bool IsDisabled(string name)
    {
        var listing = _runner.Run(_launchctl, ["print-disabled", _domain], null, _noPreamble, _timeout);

        return listing.Succeeded && Disabled(listing.StandardOutput, name);
    }

    /// <inheritdoc/>
    public bool Clear(string name) =>
        _runner.Run(_launchctl, ["enable", $"{_domain}/{name}"], null, _noPreamble, _timeout).Succeeded;

    /// <summary>Whether <paramref name="listing"/> holds <paramref name="label"/> as <c>disabled</c>, or as <c>true</c>, which earlier macOS printed.</summary>
    internal static bool Disabled(string listing, string label)
    {
        var quoted = $"\"{label}\"";

        foreach (var line in listing.Split('\n'))
        {
            var text = line.Trim();

            if (!text.StartsWith(quoted, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = text[quoted.Length..].TrimStart();

            if (rest.StartsWith("=>", StringComparison.Ordinal))
            {
                return rest[2..].Trim() is "disabled" or "true";
            }
        }

        return false;
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint getuid();
}
