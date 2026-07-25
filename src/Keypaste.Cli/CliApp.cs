using Keypaste.Core;

namespace Keypaste.Cli;

/// <summary>
/// The CLI's real entry point. Takes its output streams as parameters so the whole
/// surface is testable in-process, without spawning a child process or mutating the
/// process-global <see cref="Console"/> writers.
/// </summary>
/// <remarks>
/// Output contract, fixed here so every later command inherits it: data goes to stdout,
/// everything else to stderr. Exit codes are 0 success, 1 usage error, 2 internal error.
/// </remarks>
internal static class CliApp
{
    internal const int ExitSuccess = 0;
    internal const int ExitUsageError = 1;
    internal const int ExitInternalError = 2;

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var command = args.Length > 0 ? args[0] : "hello";

        switch (command)
        {
            case "hello":
                stdout.WriteLine(CoreInfo.Hello());
                return ExitSuccess;

            case "version":
            case "--version":
                stdout.WriteLine(CoreInfo.Version);
                return ExitSuccess;

            default:
                stderr.WriteLine($"keypaste: unknown command '{command}'");
                return ExitUsageError;
        }
    }
}
