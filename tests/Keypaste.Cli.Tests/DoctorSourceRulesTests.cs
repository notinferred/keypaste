using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// D-0467's read-only rule: <c>keypaste doctor</c>, <c>keypaste</c> alone on a terminal and the checks they run open no
/// vault, read no password, take no claim, ask the vault's holder nothing but to connect and attach, never reach the locked
/// app's notice, write no file and set up no AI tool.
/// </summary>
/// <remarks>
/// A text scan of <c>DoctorCommand.cs</c>, <c>StatusCommand.cs</c> and every file of the core's <c>Health</c> folder, in
/// which a name in a comment counts too. The holder's requests are read from <c>ApproverClient</c> itself, so a request added there is forbidden here
/// without this list changing.
/// </remarks>
public sealed partial class DoctorSourceRulesTests
{
    private static readonly string[] _forbidden =
    [
        "VaultSession.", "Vault.Open(", "ReadNewMasterPassword(", "ReadSecret(", ".Prompt.", "TryAcquire(", "AskToShow(",
        "AuditLog.TryOpen(", "LockedAppEndpoint", "McpConnectionCheck", "McpToolSurvey", "McpClientSetup.Detect(", ".Apply(",
        ".TryConnect(", "AttachClient",
        ".Save(", "AtomicFile.", "File.Write", "File.Create", "File.Delete", "File.Move", "File.Copy", "Directory.Create",
        "Directory.Delete", "FileMode.Create", "FileMode.OpenOrCreate", "FileMode.Append", "FileAccess.Write",
        "FileAccess.ReadWrite", "Choose(", "KeepCurrent(",
    ];

    [GeneratedRegex(@"public async ValueTask<\w+\?> (\w+Async)\(")]
    private static partial Regex Request();

    [Fact]
    public void Doctor_and_its_checks_only_read()
    {
        var offenders = Sources().SelectMany(source => Offenders(source.Name, source.Text)).ToList();

        Assert.True(offenders.Count == 0, $"doctor reaches what it must not: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void The_scan_reads_the_verb_and_every_check_and_knows_every_request_but_attach()
    {
        var names = Sources().Select(source => source.Name).ToList();

        Assert.Contains("DoctorCommand.cs", names);
        Assert.Contains("StatusCommand.cs", names);
        Assert.Contains("HealthCheck.cs", names);
        Assert.True(names.Count >= 8, $"only {names.Count} files were scanned");
        Assert.True(Requests().Count >= 11, $"only {Requests().Count} of the holder's requests were found");
        Assert.DoesNotContain(".AttachAsync(", Requests());
    }

    [Fact]
    public void Each_forbidden_name_added_to_doctor_is_found()
    {
        var doctor = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Cli", "Commands", "DoctorCommand.cs"));
        Assert.Empty(Offenders("DoctorCommand.cs", doctor));

        foreach (var marker in _forbidden.Concat(Requests()))
        {
            Assert.NotEmpty(Offenders("DoctorCommand.cs", doctor + Environment.NewLine + marker));
        }
    }

    private static IEnumerable<string> Offenders(string name, string text) =>
        _forbidden.Concat(Requests()).Where(marker => text.Contains(marker, StringComparison.Ordinal)).Select(marker => $"{name}: {marker}");

    /// <summary>Each request <c>ApproverClient</c> sends but attach, as a call names it.</summary>
    private static List<string> Requests() =>
        [.. Request()
            .Matches(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Keypaste.Core", "Ipc", "ApproverClient.cs")))
            .Select(match => match.Groups[1].Value)
            .Where(method => method != "AttachAsync")
            .Select(method => $".{method}(")];

    private static IEnumerable<(string Name, string Text)> Sources()
    {
        var root = RepoRoot();
        var health = Directory.GetFiles(Path.Combine(root, "src", "Keypaste.Core", "Health"), "*.cs", SearchOption.AllDirectories);

        return health
            .Append(Path.Combine(root, "src", "Keypaste.Cli", "Commands", "DoctorCommand.cs"))
            .Append(Path.Combine(root, "src", "Keypaste.Cli", "Commands", "StatusCommand.cs"))
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}
