using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Keypaste.EnvReporter;

/// <summary>
/// Reports its working directory, its own command line and the named variables of its own
/// environment over the named pipe it is given, then exits. Or, started with flags, it is the child
/// an agent's run starts in the capture tests: it prints what it is told to, in the forms it is
/// told to, and exits as it is told to.
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
            Console.Error.WriteLine("usage: Keypaste.EnvReporter <pipe> [NAME...] | --<step> ...");
            return 2;
        }

        return args[0].StartsWith("--", StringComparison.Ordinal) ? Steps(args) : Report(args);
    }

    private static int Report(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.Out);
        pipe.Connect(TimeSpan.FromSeconds(30));

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine($"cwd={Environment.CurrentDirectory}");
        writer.WriteLine($"cmdline={Environment.CommandLine}");

        foreach (var name in args.Skip(1))
        {
            writer.WriteLine(Environment.GetEnvironmentVariable(name) is { } value ? $"{name}={value}" : $"{name} unset");
        }

        writer.WriteLine("end");
        return 0;
    }

    /// <summary>Runs each step in the order given.</summary>
    /// <remarks>
    /// <c>--stdout NAME…</c> prints <c>NAME=value</c> lines; <c>--forms NAME…</c> prints each value in the
    /// escaped forms common dumps use; <c>--utf16 NAME</c> writes a value as UTF-16LE bytes; <c>--env</c>
    /// prints the whole environment; <c>--print TEXT</c> and <c>--stderr TEXT</c> print text;
    /// <c>--read-stdin</c> prints how many bytes standard input held; <c>--bytes N</c> prints N bytes;
    /// <c>--has-line PATH TEXT</c> says whether a file holds TEXT; <c>--touch PATH</c> creates a file;
    /// <c>--pid-file PATH</c> writes its process id;
    /// <c>--sleep S</c> waits; <c>--spawn-sleeper</c> leaves a child holding the output pipe and prints
    /// its id; <c>--exit N</c> exits with N.
    /// </remarks>
    private static int Steps(string[] args)
    {
        using var stdout = Console.OpenStandardOutput();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stdout":
                    foreach (var name in Operands(args, ref i))
                    {
                        Console.Out.WriteLine($"{name}={Environment.GetEnvironmentVariable(name)}");
                    }

                    break;

                case "--forms":
                    foreach (var name in Operands(args, ref i))
                    {
                        Forms(name, Environment.GetEnvironmentVariable(name) ?? string.Empty);
                    }

                    break;

                case "--utf16":
                    Console.Out.Flush();
                    var utf16 = Encoding.Unicode.GetBytes(Environment.GetEnvironmentVariable(args[++i]) ?? string.Empty);
                    stdout.Write(utf16);
                    stdout.Flush();
                    break;

                case "--env":
                    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                    {
                        Console.Out.WriteLine($"{entry.Key}={entry.Value}");
                    }

                    break;

                case "--print":
                    Console.Out.WriteLine(args[++i]);
                    break;

                case "--stderr":
                    Console.Error.WriteLine(args[++i]);
                    break;

                case "--read-stdin":
                    using (var input = Console.OpenStandardInput())
                    {
                        var buffer = new byte[4096];
                        long total = 0;
                        int read;
                        while ((read = input.Read(buffer)) > 0)
                        {
                            total += read;
                        }

                        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"stdin={total}"));
                    }

                    break;

                case "--bytes":
                    Console.Out.Flush();
                    var count = long.Parse(args[++i], CultureInfo.InvariantCulture);
                    var line = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde\n");

                    for (long written = 0; written < count; written += line.Length)
                    {
                        stdout.Write(line, 0, (int)Math.Min(line.Length, count - written));
                    }

                    stdout.Flush();
                    break;

                case "--has-line":
                    var path = args[++i];
                    var text = args[++i];
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        Console.Out.WriteLine(reader.ReadToEnd().Contains(text, StringComparison.Ordinal) ? "audit=found" : "audit=missing");
                    }

                    break;

                case "--touch":
                    File.WriteAllText(args[++i], "ran");
                    break;

                case "--pid-file":
                    File.WriteAllText(args[++i], Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                    break;

                case "--sleep":
                    Thread.Sleep(TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture)));
                    break;

                case "--spawn-sleeper":
                    var sleeper = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                    sleeper.ArgumentList.Add("--sleep");
                    sleeper.ArgumentList.Add("120");
                    using (var started = Process.Start(sleeper)!)
                    {
                        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sleeper={started.Id}"));
                    }

                    break;

                case "--exit":
                    Console.Out.Flush();
                    return int.Parse(args[++i], CultureInfo.InvariantCulture);

                default:
                    Console.Error.WriteLine($"unknown step {args[i]}");
                    return 2;
            }
        }

        Console.Out.Flush();
        return 0;
    }

    private static List<string> Operands(string[] args, ref int i)
    {
        List<string> operands = [];

        while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            operands.Add(args[++i]);
        }

        return operands;
    }

    /// <summary>A value as <c>env</c>, JSON encoders, <c>repr</c>, shells and URL encoders print it.</summary>
    private static void Forms(string name, string value)
    {
        Console.Out.WriteLine($"literal {name}={value}");
        Console.Out.WriteLine($"json \"{JsonEncodedText.Encode(value)}\"");
        Console.Out.WriteLine($"json-relaxed \"{JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"");
        Console.Out.WriteLine($"json-ascii-lower \"{JsonAsciiLower(value)}\"");
        Console.Out.WriteLine($"repr '{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}'");
        Console.Out.WriteLine($"set {name}='{value.Replace("'", "'\\''", StringComparison.Ordinal)}'");
        Console.Out.WriteLine($"declare -x {name}=\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal)}\"");
        Console.Out.WriteLine($"percent {Uri.EscapeDataString(value)}");
        Console.Out.WriteLine($"percent-lower {LowerPercent(Uri.EscapeDataString(value))}");
        Console.Out.WriteLine($"form {Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal)}");
    }

    private static string JsonAsciiLower(string value)
    {
        var json = new StringBuilder();

        foreach (var c in value)
        {
            json.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                _ when c < 0x20 || c > 0x7E => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return json.ToString();
    }

    private static string LowerPercent(string escaped)
    {
        var chars = escaped.ToCharArray();

        for (var i = 0; i + 2 < chars.Length; i++)
        {
            if (chars[i] == '%')
            {
                chars[i + 1] = char.ToLowerInvariant(chars[i + 1]);
                chars[i + 2] = char.ToLowerInvariant(chars[i + 2]);
                i += 2;
            }
        }

        return new string(chars);
    }
}
