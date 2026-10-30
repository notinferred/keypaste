using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The architecture rules the compiler holds through each project's <c>BannedSymbols.txt</c> are switched off only where
/// this list says, and KeePassLib is reachable only from <c>Internal/KeePassInterop.cs</c> (D-0675).
/// </summary>
/// <remarks>
/// A <c>#pragma</c>, a <c>SuppressMessage</c>, a <c>NoWarn</c> or an <c>.editorconfig</c> severity would turn a ban off
/// without the compiler saying so, so every mention of RS0030 is read from the source. A new exemption is a change to this list.
/// </remarks>
public sealed class BanExemptionsTests
{
    private const string _disable = "#pragma warning disable RS0030";

    private const string _restore = "#pragma warning restore RS0030";

    private const string _alias = "extern alias KeePass;";

    private static readonly Dictionary<string, int> _exemptions = new(StringComparer.Ordinal)
    {
        ["src/Keypaste.App/Clipboard/AvaloniaClipboard.cs"] = 2,
        ["src/Keypaste.App/CrashReporting.cs"] = 2,
        ["src/Keypaste.App/ViewModels/ConnectClientViewModel.cs"] = 1,
        ["src/Keypaste.App/ViewModels/EntryAttachmentRow.cs"] = 1,
        ["src/Keypaste.App/ViewModels/EntryAttachmentsViewModel.cs"] = 1,
        ["src/Keypaste.App/ViewModels/EntryDetailViewModel.cs"] = 1,
        ["src/Keypaste.App/ViewModels/ShellViewModel.cs"] = 3,
        ["src/Keypaste.App/Views/OpenerProcess.cs"] = 1,
        ["src/Keypaste.App/Views/PlatformWebLauncher.cs"] = 1,
        ["src/Keypaste.Cli/Program.cs"] = 1,
        ["src/Keypaste.Core/CrashReports/CrashSender.cs"] = 1,
        ["src/Keypaste.Core/Internal/TestKeyDerivation.cs"] = 1,
        ["src/Keypaste.Core/Sharing/ShareClient.cs"] = 1,
        ["src/Keypaste.Core/Sharing/ShareService.cs"] = 1,
    };

    [Fact]
    public void Bans_are_switched_off_only_where_listed() => Assert.Equal(_exemptions, Exemptions(Sources()));

    [Fact]
    public void Nothing_else_mentions_the_ban() => Assert.Empty(OtherMentions(Sources()));

    [Fact]
    public void Only_KeePassInterop_declares_the_KeePassLib_alias() =>
        Assert.Equal(["src/Keypaste.Core/Internal/KeePassInterop.cs"], Sources().Where(source => source.Text.Contains(_alias, StringComparison.Ordinal)).Select(source => source.Path));

    [Fact]
    public void An_added_exemption_or_silencing_is_found()
    {
        var sources = Sources();
        Assert.True(sources.Count > 300, $"only {sources.Count} files were read");

        Assert.NotEqual(_exemptions, Exemptions([.. sources, ("src/Keypaste.App/ViewModels/Probe.cs", _disable + "\n")]));
        Assert.NotEmpty(OtherMentions([("src/Keypaste.App/Keypaste.App.csproj", "<NoWarn>$(NoWarn);RS0030</NoWarn>")]));
        Assert.NotEmpty(OtherMentions([(".editorconfig", "dotnet_diagnostic.RS0030.severity = none")]));
        Assert.NotEmpty(OtherMentions([("src/Keypaste.App/Probe.cs", "[SuppressMessage(\"ApiDesign\", \"RS0030\")]")]));
    }

    private static Dictionary<string, int> Exemptions(IEnumerable<(string Path, string Text)> sources) =>
        sources
            .Select(source => (source.Path, Count: source.Text.Split('\n').Count(line => line.TrimStart().StartsWith(_disable, StringComparison.Ordinal))))
            .Where(source => source.Count > 0)
            .ToDictionary(source => source.Path, source => source.Count, StringComparer.Ordinal);

    private static IEnumerable<string> OtherMentions(IEnumerable<(string Path, string Text)> sources) =>
        sources.SelectMany(source => source.Text.Split('\n')
            .Where(line => line.Contains("RS0030", StringComparison.Ordinal))
            .Where(line => !line.TrimStart().StartsWith(_disable, StringComparison.Ordinal) && !line.TrimStart().StartsWith(_restore, StringComparison.Ordinal))
            .Select(line => $"{source.Path}: {line.Trim()}"));

    /// <summary>Every source, project and analyzer setting in <c>src</c>, and the repository's own build and style files.</summary>
    private static List<(string Path, string Text)> Sources()
    {
        var root = VendoredWordList.RepoRoot();
        string[] patterns = ["*.cs", "*.csproj", "*.props", "*.targets", "*.txt", "*.editorconfig", "*.globalconfig"];

        return [.. patterns
            .SelectMany(pattern => Directory.EnumerateFiles(Path.Combine(root, "src"), pattern, SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Concat(new[] { ".editorconfig", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }.Select(file => Path.Combine(root, file)))
            .Select(path => (Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path)))];
    }
}
