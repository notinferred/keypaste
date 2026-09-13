using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Keypaste.Cli.Tests;

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
