using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Records the stack of every first-chance null reference in a CLI test process, for a test that
/// reports only the message after the CLI turned the exception into an error.
/// </summary>
/// <remarks>
/// Off unless <c>KEYPASTE_TEST_EXCEPTION_TRACE</c> names a directory; then each test process writes
/// <c>cli-null-reference-&lt;pid&gt;.log</c> there, with exception types and stacks but no messages or
/// argument values. Run the whole suite with it set, so startup and concurrency stay as they were.
/// Writing is best effort: an empty file means no stack was recorded, a missing one that tracing did
/// not start. It is test code and never ships.
/// </remarks>
internal static class ExceptionTrace
{
    private static readonly Lock _gate = new();
    private static string? _path;

    [ThreadStatic]
    private static bool _writing;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Environment.GetEnvironmentVariable("KEYPASTE_TEST_EXCEPTION_TRACE");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"cli-null-reference-{Environment.ProcessId}.log");
            File.WriteAllText(path, string.Empty);
            _path = path;
            AppDomain.CurrentDomain.FirstChanceException += Record;
        }
        catch (Exception)
        {
            // Optional diagnostics must not replace the failure being investigated.
        }
    }

    private static void Record(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not NullReferenceException || _path is null || _writing)
        {
            return;
        }

        _writing = true;
        try
        {
            // Exception messages may contain input values, so retain only the type and stack.
            var trace = args.Exception.GetType().FullName + Environment.NewLine
                + args.Exception.StackTrace + Environment.NewLine + Environment.NewLine;
            lock (_gate)
            {
                File.AppendAllText(_path, trace);
            }
        }
        catch (Exception)
        {
            // A write failure must preserve the original exception's behavior.
        }
        finally
        {
            _writing = false;
        }
    }
}
