using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A folder whose reading is taken away, so the system cannot open it to flush a rename into it, until disposed.</summary>
internal sealed class UnflushableFolder : IDisposable
{
    private readonly string _folder;

    private UnflushableFolder(string folder) => _folder = folder;

    /// <summary>Stages <paramref name="folder"/>, or skips where the system would flush it anyway.</summary>
    internal static UnflushableFolder Stage(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows renames write through, so no folder is flushed after them.");
            return null!;
        }

        File.SetUnixFileMode(folder, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var staged = new UnflushableFolder(folder);

        try
        {
            _ = Directory.GetFiles(folder);
        }
        catch (UnauthorizedAccessException)
        {
            return staged;
        }

        staged.Dispose();
        Assert.Skip("This user reads every folder whatever its mode.");
        return null!;
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
