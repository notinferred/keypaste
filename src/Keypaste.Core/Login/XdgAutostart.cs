namespace Keypaste.Core.Login;

/// <summary>Opens at login on Linux through an XDG autostart entry, which GNOME, KDE and most other desktops read.</summary>
public sealed class XdgAutostart : ILoginItem
{
    /// <summary>The entry's file name.</summary>
    public const string FileName = "keypaste.desktop";

    private readonly bool _quotable;

    /// <summary>Initializes the entry for one program.</summary>
    /// <param name="directory">The autostart directory, from <see cref="DirectoryFor"/>.</param>
    /// <param name="program">The executable a login starts.</param>
    public XdgAutostart(string directory, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(program);

        FilePath = Path.Combine(directory, FileName);
        _quotable = program.IndexOfAny(['\n', '\r']) < 0;
        Document = string.Join(
            '\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=keypaste",
            "Comment=Starts keypaste locked in the tray",
            $"Exec={Quoted(program)} {LoginItems.BackgroundFlag}",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            string.Empty);
    }

    /// <summary>Gets where the entry is written.</summary>
    public string FilePath { get; }

    /// <summary>Gets the desktop entry the file holds.</summary>
    public string Document { get; }

    /// <inheritdoc/>
    public bool IsEnabled => string.Equals(LoginItems.ReadFile(FilePath), Document, StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool Enable() => _quotable && LoginItems.WriteFile(FilePath, Document);

    /// <inheritdoc/>
    public bool Disable() => LoginItems.DeleteFile(FilePath);

    /// <summary>The autostart directory: <c>$XDG_CONFIG_HOME/autostart</c>, or <c>~/.config/autostart</c> when that is unset or relative.</summary>
    /// <param name="configHome">The <c>XDG_CONFIG_HOME</c> environment variable.</param>
    /// <param name="home">The person's home directory.</param>
    /// <returns>The directory.</returns>
    public static string DirectoryFor(string? configHome, string home) =>
        configHome is { Length: > 0 } && Path.IsPathRooted(configHome)
            ? Path.Combine(configHome, "autostart")
            : Path.Combine(home, ".config", "autostart");

    // The Desktop Entry spec's quoting for one argument, then its string escape, and a literal percent doubled.
    private static string Quoted(string argument)
    {
        var quoted = new StringBuilder("\"");

        foreach (var c in argument)
        {
            if (c is '"' or '`' or '$')
            {
                quoted.Append(@"\\").Append(c);
            }
            else if (c == '\\')
            {
                quoted.Append(@"\\\\");
            }
            else if (c == '%')
            {
                quoted.Append("%%");
            }
            else
            {
                quoted.Append(c);
            }
        }

        return quoted.Append('"').ToString();
    }
}
