using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>The real <c>keepassxc-cli</c>, which reads every vault these tests have keypaste write (PRODUCT law 4.6).</summary>
/// <remarks>
/// Found at <c>KPXC_CLI</c>, then on <c>PATH</c>, then in the macOS app. Where none is found the tests skip, unless
/// <c>KEYPASTE_KEEPASSXC</c> is <c>required</c>, as CI's compatibility jobs set it, where they fail: a check that passes
/// without KeePassXC proves nothing.
/// </remarks>
internal static class KeePassXcCli
{
    /// <summary>Every vault's master password. ASCII, because KeePassXC's Windows console can transcode anything else.</summary>
    internal const string Password = "ci-master-pw";

    private static readonly Lazy<string?> _path = new(Find);

    /// <summary>The CLI's path, skipping or failing the test as the class remarks say when there is none.</summary>
    internal static string Path_
    {
        get
        {
            if (_path.Value is { } path)
            {
                return path;
            }

            if (Environment.GetEnvironmentVariable("KEYPASTE_KEEPASSXC") == "required")
            {
                throw new InvalidOperationException("keepassxc-cli was not found, and KEYPASTE_KEEPASSXC requires it; set KPXC_CLI.");
            }

            Assert.Skip("keepassxc-cli is not installed; set KPXC_CLI to run the KeePassXC compatibility tests.");
            return "";
        }
    }

    /// <summary>Runs one command on <paramref name="vault"/> with <see cref="Password"/>, failing the test unless it exits 0.</summary>
    /// <returns>What it printed, without Qt's carriage returns.</returns>
    internal static string Run(string command, string vault, params string[] arguments) =>
        Run(new KeePassXcKey(Password, null), command, vault, arguments);

    /// <summary>Runs one command on <paramref name="vault"/> opened with <paramref name="key"/>, failing the test unless it exits 0.</summary>
    internal static string Run(KeePassXcKey key, string command, string vault, params string[] arguments)
    {
        var (exit, output, error) = Try(key.Password ?? "", [command, "-q", .. key.Arguments, vault, .. arguments]);
        Assert.True(exit == 0, $"keepassxc-cli {command} exited {exit}: {error}");
        return output;
    }

    /// <summary>Runs keepassxc-cli with <paramref name="stdin"/> as its input, whatever it exits with.</summary>
    internal static (int Exit, string Output, string Error) Try(string stdin, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Path_)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.WriteLine(stdin);
        process.StandardInput.Close();

        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"keepassxc-cli {string.Join(' ', start.ArgumentList)} did not finish within a minute");
        }

        return (process.ExitCode, output.Result.Replace("\r", "", StringComparison.Ordinal), error.Result);
    }

    /// <summary>The values of <paramref name="attributes"/> on <paramref name="entry"/>, one per attribute.</summary>
    internal static string[] Show(string vault, string entry, params string[] attributes) =>
        Run("show", vault, [entry, .. attributes.SelectMany(attribute => new[] { "-a", attribute })])
            .TrimEnd('\n')
            .Split('\n');

    /// <summary>The vault as KeePassXC exports it, the clearest reader it offers for history, CustomData and protection.</summary>
    internal static XDocument Export(string vault) => XDocument.Parse(Run("export", vault, "-f", "xml"));

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("KPXC_CLI") is { Length: > 0 } configured)
        {
            return configured;
        }

        var name = OperatingSystem.IsWindows() ? "keepassxc-cli.exe" : "keepassxc-cli";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            if (folder.Length > 0 && File.Exists(System.IO.Path.Combine(folder, name)))
            {
                return System.IO.Path.Combine(folder, name);
            }
        }

        const string macApp = "/Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli";
        return OperatingSystem.IsMacOS() && File.Exists(macApp) ? macApp : null;
    }
}

/// <summary>What opens a vault in keepassxc-cli: a password, a keyfile, or both.</summary>
internal sealed record KeePassXcKey(string? Password, string? Keyfile)
{
    internal string[] Arguments =>
        [.. Keyfile is null ? [] : new[] { "-k", Keyfile }, .. Password is null ? new[] { "--no-password" } : []];
}
