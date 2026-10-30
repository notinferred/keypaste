using Keypaste.Core.Browser;
using Keypaste.FakeBrowser;
using Xunit;

namespace Keypaste.Browser.Tests;

/// <summary>
/// Which arguments are a browser starting its native messaging host, which callers keypaste answers, and that any other
/// exits before standard input is read (D-0624).
/// </summary>
public sealed class BrowserCallerTests : IDisposable
{
    private const string _chrome = "chrome-extension://oboonakemofpalcgghocfoadofidjkkk/";

    private static readonly string _manifest = Path.Combine(Path.GetTempPath(), "org.keepassxc.keepassxc_browser.json");

    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-caller-tests-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Theory]
    [InlineData(false, _chrome)]
    [InlineData(true, _chrome)]
    [InlineData(true, _chrome, "--parent-window=0")]
    [InlineData(true, "chrome-extension://pdffhmdngciaglkoonimfcmckehcpafo/", "--parent-window=1180410")]
    [InlineData(false, "chrome-extension://abcdefghijklmnopabcdefghijklmnop/")]
    public void A_chrome_family_caller_s_origin_is_a_browser_start(bool windows, params string[] args) =>
        Assert.Equal(args[0], BrowserCaller.Of(args, windows));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Firefox_s_manifest_path_and_add_on_ID_are_a_browser_start(bool windows) =>
        Assert.Equal("keepassxc-browser@keepassxc.org", BrowserCaller.Of([_manifest, "keepassxc-browser@keepassxc.org"], windows));

    [Theory]
    [InlineData(false)]
    [InlineData(false, "mcp")]
    [InlineData(false, "--selftest")]
    [InlineData(false, "approve")]
    [InlineData(false, "get", "github")]
    [InlineData(false, _chrome, "--parent-window=0")]
    [InlineData(true, _chrome, "--parent-window=")]
    [InlineData(true, _chrome, "--parent-window=12a")]
    [InlineData(true, _chrome, "--parent-window=0", "--extra")]
    [InlineData(false, "chrome-extension:/oboonakemofpalcgghocfoadofidjkkk/")]
    [InlineData(false, "org.keepassxc.keepassxc_browser.json", "keepassxc-browser@keepassxc.org")]
    public void Anything_else_is_not(bool windows, params string[] args) => Assert.Null(BrowserCaller.Of(args, windows));

    [Fact]
    public void A_manifest_path_followed_by_an_option_is_not()
    {
        Assert.Null(BrowserCaller.Of([_manifest, "--help"], windows: false));
        Assert.Null(BrowserCaller.Of([Path.ChangeExtension(_manifest, ".txt"), "keepassxc-browser@keepassxc.org"], windows: false));
    }

    [Fact]
    public void Keypaste_answers_the_three_KeePassXC_Browser_builds_and_no_other()
    {
        Assert.True(BrowserCaller.TryRead([_chrome], windows: false, out var chrome, out _));
        Assert.True(BrowserCaller.TryRead(["chrome-extension://pdffhmdngciaglkoonimfcmckehcpafo/", "--parent-window=7"], windows: true, out var edge, out _));
        Assert.True(BrowserCaller.TryRead([_manifest, "keepassxc-browser@keepassxc.org"], windows: false, out var firefox, out _));

        Assert.Equal(BrowserIdentity.ChromeWebStore, chrome);
        Assert.Equal(BrowserIdentity.EdgeAddOns, edge);
        Assert.Equal(BrowserIdentity.FirefoxAddOns, firefox);

        Assert.False(BrowserCaller.TryRead(["chrome-extension://abcdefghijklmnopabcdefghijklmnop/"], windows: false, out _, out var problem));
        Assert.Equal($"{BrowserIdentity.Only}, and the browser named chrome-extension://abcdefghijklmnopabcdefghijklmnop/", problem);
        Assert.False(BrowserCaller.TryRead([_manifest, "someone-else@example.com"], windows: false, out _, out _));
        Assert.False(BrowserCaller.TryRead(["chrome-extension://OBOONAKEMOFPALCGGHOCFOADOFIDJKKK/"], windows: false, out _, out _));
    }

    [Theory]
    [InlineData("chrome", false)]
    [InlineData("chrome", true)]
    [InlineData("edge", false)]
    [InlineData("edge", true)]
    [InlineData("firefox", false)]
    [InlineData("firefox", true)]
    public void The_fake_browser_starts_its_host_as_its_browser_does(string browser, bool windows)
    {
        var arguments = BrowserLaunch.Arguments(browser, windows);

        Assert.NotNull(arguments);
        Assert.True(BrowserCaller.TryRead(arguments, windows, out _, out var problem), problem);
    }

    [Fact]
    public async Task A_caller_keypaste_does_not_answer_exits_before_standard_input_is_opened()
    {
        var opened = false;
        using var output = new MemoryStream();
        await using var error = new StringWriter();

        var code = await BrowserEntry.RunAsync(
            ["chrome-extension://abcdefghijklmnopabcdefghijklmnop/"],
            () =>
            {
                opened = true;
                return Stream.Null;
            },
            output,
            error,
            _ => null,
            windows: false,
            Token);

        Assert.Equal(1, code);
        Assert.False(opened);
        Assert.Equal(0, output.Length);
        Assert.StartsWith($"keypaste: {BrowserIdentity.Only}", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_approver_exits_before_standard_input_is_opened()
    {
        var opened = false;
        using var output = new MemoryStream();
        await using var error = new StringWriter();

        var code = await BrowserEntry.RunAsync(
            [_chrome],
            () =>
            {
                opened = true;
                return Stream.Null;
            },
            output,
            error,
            name => name switch
            {
                "KEYPASTE_HOME" => _home,
                "KEYPASTE_APPROVER" => "not a pipe name/",
                _ => null,
            },
            windows: false,
            Token);

        Assert.Equal(1, code);
        Assert.False(opened);
        Assert.Equal(0, output.Length);
        Assert.StartsWith("keypaste: KEYPASTE_APPROVER: ", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_caller_keypaste_answers_reads_standard_input_until_it_ends()
    {
        var opened = false;
        using var output = new MemoryStream();
        await using var error = new StringWriter();

        var code = await BrowserEntry.RunAsync(
            [_chrome],
            () =>
            {
                opened = true;
                return Stream.Null;
            },
            output,
            error,
            name => name == "KEYPASTE_HOME" ? _home : null,
            windows: false,
            Token);

        Assert.Equal(0, code);
        Assert.True(opened);
        Assert.Equal(0, output.Length);
    }
}
