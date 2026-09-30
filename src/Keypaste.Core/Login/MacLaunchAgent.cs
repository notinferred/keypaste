using System.Security;

namespace Keypaste.Core.Login;

/// <summary>Opens at login on macOS through a LaunchAgent in the person's own Library.</summary>
/// <remarks>SMAppService would need a signed bundle and Objective-C interop; a LaunchAgent works for an unsigned app and is a plain file.</remarks>
public sealed class MacLaunchAgent : ILoginItem
{
    /// <summary>The agent's label, which is also the app bundle's identifier.</summary>
    public const string Label = "com.keypaste.app";

    /// <summary>Initializes the agent for one program.</summary>
    /// <param name="home">The person's home directory.</param>
    /// <param name="program">The executable a login starts.</param>
    public MacLaunchAgent(string home, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentException.ThrowIfNullOrEmpty(program);

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
    public bool IsEnabled => string.Equals(LoginItems.ReadFile(FilePath), Document, StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool Enable() => LoginItems.WriteFile(FilePath, Document);

    /// <inheritdoc/>
    public bool Disable() => LoginItems.DeleteFile(FilePath);
}
