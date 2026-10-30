using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 4.11a's rules about where crash-report code may be: only the scrubber reads an exception or builds a report's text,
/// nothing in the crash code reaches a message, a path, a name or the environment, and only the sender names the route
/// (D-0558, THREATS.md T-9).
/// </summary>
/// <remarks>
/// The CLI's and the bridge's <c>BannedSymbols</c> lists keep them from naming crash code. These rules are about single files
/// inside one folder, and a ban list covers a whole project, so they are read from the source, in which a name in a comment
/// counts too. The last test is the control: each rule finds its mutation.
/// </remarks>
public sealed class CrashSourceRulesTests
{
    private const string _scrubber = "CrashScrubber.cs";
    private const string _sender = "CrashSender.cs";
    private const string _route = "/api/crash";

    // What could carry a message, a path, a name, a value, the environment or an identifier into a report.
    private static readonly string[] _unscrubbed =
    [
        ".Message", ".Data", ".StackTrace", ".Source", ".TargetSite", ".HelpLink", ".FileName", "ToString()", "GetMethod(", "GetFileName(",
        "GetFileLineNumber", "GetEnvironmentVariable", "MachineName", "UserName", "UserDomainName", "CommandLine", "CurrentDirectory",
        "ProcessPath", "GetFolderPath", "OSDescription", "VersionString", "Vault", "Audit", "Clipboard",
    ];

    // Reading an exception, and writing or making a report.
    private static readonly string[] _scrubbing =
        ["new CrashReport(", "Utf8JsonWriter", "StackTrace", "StackFrame", "DiagnosticMethodInfo", "InnerException", "HResult"];

    [Fact]
    public void Nothing_in_the_crash_code_reaches_a_message_a_path_a_name_or_the_environment() =>
        Assert.Empty(CrashCode().SelectMany(source => Found(source, _unscrubbed)));

    [Fact]
    public void Only_the_scrubber_reads_an_exception_or_builds_a_reports_text()
    {
        Assert.Empty(CrashCode().Where(source => source.Name != _scrubber).SelectMany(source => Found(source, _scrubbing)));
        Assert.Equal([_scrubber], Containing(Sources("src"), "new CrashReport("));
    }

    [Fact]
    public void Only_the_sender_names_the_route() => Assert.Equal([_sender], Containing(Sources("src"), _route));

    [Fact]
    public void Each_rule_finds_its_mutation()
    {
        var code = CrashCode();
        Assert.Equal(
            ["CrashContext.cs", "CrashOutbox.cs", "CrashReport.cs", "CrashScrubber.cs", "CrashSender.cs"],
            code.Select(source => source.Name).Order(StringComparer.Ordinal));

        var scrubber = code.Single(source => source.Name == _scrubber);
        var outbox = code.Single(source => source.Name == "CrashOutbox.cs");

        Assert.All(_unscrubbed, needle => Assert.NotEmpty(Found(scrubber with { Text = scrubber.Text + needle }, _unscrubbed)));
        Assert.All(_scrubbing, needle => Assert.NotEmpty(Found(outbox with { Text = outbox.Text + needle }, _scrubbing)));
        Assert.NotEmpty(Found(outbox with { Text = outbox.Text + "var message = exception.Message;" }, _unscrubbed));
        Assert.Equal(["CrashOutbox.cs", _sender], Containing([outbox with { Text = outbox.Text + _route }, .. Sources("src")], _route).Order(StringComparer.Ordinal));
    }

    private static List<Source> CrashCode() => Sources("src", "Keypaste.Core", "CrashReports");

    private static IEnumerable<string> Found(Source source, string[] needles) =>
        needles.Where(needle => source.Text.Contains(needle, StringComparison.Ordinal)).Select(needle => $"{source.Name}: {needle}");

    private static List<string> Containing(IEnumerable<Source> sources, string needle) =>
        [.. sources.Where(source => source.Text.Contains(needle, StringComparison.Ordinal)).Select(source => source.Name)];

    private static List<Source> Sources(params string[] folder) =>
        [.. Directory.GetFiles(Path.Combine([VendoredWordList.RepoRoot(), .. folder]), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => new Source(Path.GetFileName(path), File.ReadAllText(path)))];

    private sealed record Source(string Name, string Text);
}
