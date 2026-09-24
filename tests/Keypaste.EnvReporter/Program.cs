using System.IO.Pipes;

namespace Keypaste.EnvReporter;

/// <summary>
/// Reports its working directory, its own command line and the named variables of its own
/// environment over the named pipe it is given, then exits.
/// </summary>
/// <remarks>
/// The app's Run starts it in a terminal in V-E.1b. It reports over a pipe so the test reads what the
/// child received without the child writing a file, which the same test checks nothing did.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: Keypaste.EnvReporter <pipe> [NAME...]");
            return 2;
        }

        using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.Out);
        pipe.Connect(TimeSpan.FromSeconds(30));

        using var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine($"cwd={Environment.CurrentDirectory}");
        writer.WriteLine($"cmdline={Environment.CommandLine}");

        foreach (var name in args.Skip(1))
        {
            writer.WriteLine(Environment.GetEnvironmentVariable(name) is { } value ? $"{name}={value}" : $"{name} unset");
        }

        writer.WriteLine("end");
        return 0;
    }
}
