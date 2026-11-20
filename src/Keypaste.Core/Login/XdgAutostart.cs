namespace Keypaste.Core.Login;

/// <summary>Opens at login on Linux through an XDG autostart entry, which GNOME, KDE and most other desktops read.</summary>
public sealed class XdgAutostart : ILoginItem
{
    /// <summary>The entry's file name.</summary>
    public const string FileName = "keypaste.desktop";

    private const string _execPrefix = "Exec=";

    private readonly bool _quotable;
    private readonly string _directory;
    private readonly string _program;

    /// <summary>Initializes the entry for one program.</summary>
    /// <param name="directory">The autostart directory, from <see cref="DirectoryFor"/>.</param>
    /// <param name="program">The executable a login starts.</param>
    public XdgAutostart(string directory, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(program);

        _directory = directory;
        _program = program;
        FilePath = Path.Combine(directory, FileName);
        _quotable = program.IndexOfAny(['\n', '\r']) < 0;
        Document = string.Join(
            '\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=keypaste",
            "Comment=Starts keypaste locked in the tray",
            $"{_execPrefix}{Quoted(program)} {LoginItems.BackgroundFlag}",
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

    /// <summary>Re-points an entry keypaste wrote for a program that no longer exists, such as an AppImage since moved, to this one.</summary>
    /// <returns>Whether the entry was rewritten.</returns>
    /// <remarks>An entry for another copy that still exists is that copy's, and a file keypaste did not write is the person's; both are left alone.</remarks>
    public bool KeepCurrent()
    {
        if (LoginItems.ReadFile(FilePath) is not { } written
            || ProgramOf(written) is not { } named
            || string.Equals(named, _program, StringComparison.Ordinal)
            || !string.Equals(written, new XdgAutostart(_directory, named).Document, StringComparison.Ordinal)
            || File.Exists(named))
        {
            return false;
        }

        return Enable();
    }

    /// <summary>The autostart directory: <c>$XDG_CONFIG_HOME/autostart</c>, or <c>~/.config/autostart</c> when that is unset or relative.</summary>
    /// <param name="configHome">The <c>XDG_CONFIG_HOME</c> environment variable.</param>
    /// <param name="home">The person's home directory.</param>
    /// <returns>The directory.</returns>
    public static string DirectoryFor(string? configHome, string home) =>
        configHome is { Length: > 0 } && Path.IsPathRooted(configHome)
            ? Path.Combine(configHome, "autostart")
            : Path.Combine(home, ".config", "autostart");

    // The program an entry's Exec line names, undoing Quoted; null when the line is not one Quoted could have written.
    private static string? ProgramOf(string document)
    {
        var exec = document.Split('\n').FirstOrDefault(line => line.StartsWith(_execPrefix, StringComparison.Ordinal));
        var start = _execPrefix + '"';
        var end = "\" " + LoginItems.BackgroundFlag;

        if (exec is null || exec.Length <= start.Length + end.Length || !exec.StartsWith(start, StringComparison.Ordinal) || !exec.EndsWith(end, StringComparison.Ordinal))
        {
            return null;
        }

        var quoted = exec[start.Length..^end.Length];
        var program = new StringBuilder();

        for (var i = 0; i < quoted.Length; i++)
        {
            if (quoted.AsSpan(i).StartsWith(@"\\\\", StringComparison.Ordinal))
            {
                program.Append('\\');
                i += 3;
            }
            else if (quoted.AsSpan(i).StartsWith(@"\\", StringComparison.Ordinal) && i + 2 < quoted.Length)
            {
                program.Append(quoted[i + 2]);
                i += 2;
            }
            else if (quoted.AsSpan(i).StartsWith("%%", StringComparison.Ordinal))
            {
                program.Append('%');
                i += 1;
            }
            else
            {
                program.Append(quoted[i]);
            }
        }

        return program.ToString();
    }

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
