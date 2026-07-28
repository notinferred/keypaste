using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the activity view promises: it says what <c>keypaste log</c> says, it needs no vault, and
/// an empty machine is told so calmly.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first test here is D-0032 as a gate rather than as a comment.</b> The rendering lives in
/// <see cref="AuditText"/> because CORE.md law 4.3 does not allow the CLI and the GUI to write the
/// same sentence twice, and a doc-comment saying so is an assertion about the world rather than a
/// check on it. This fails the day somebody re-implements the table in XAML.
/// </para>
/// <para>
/// <b>Not one of these unlocks a vault, and none of them can.</b> The audit log is machine state;
/// every record below was written by a fixture with no vault anywhere near it.
/// </para>
/// <para>
/// Not one of these starts an Avalonia application either. <see cref="LogViewModel"/> names no
/// Avalonia type, so none of them needs the assembly's one headless session.
/// </para>
/// </remarks>
public sealed class LogViewModelTests : IDisposable
{
    private readonly TempAuditHome _home = new();

    public void Dispose() => _home.Dispose();

    /// <summary>
    /// The lines are the core's heading, table and notes, and nothing else.
    /// </summary>
    /// <remarks>
    /// Asserted against <see cref="AuditText"/> itself rather than against a fixed string, because
    /// the claim is not "the table looks like this" — it is "the GUI shows what the CLI shows". A
    /// literal here would go on passing while the two front ends drifted apart.
    /// </remarks>
    [Fact]
    public void The_lines_are_exactly_what_the_core_renders()
    {
        _home.Append("env/dev/STRIPE_KEY", "env/prod/GITHUB_TOKEN");

        var model = new LogViewModel(_home.Home);

        Assert.True(AuditReader.TryRead(_home.LogPath, out var entries, out var unreadable, out var error), error);
        var report = AuditChainVerifier.Verify(_home.LogPath);

        List<string> expected =
        [
            AuditText.Heading(_home.LogPath, entries.Count, entries.Count, []),
            .. AuditText.Table(entries, report.Unverified),
            .. AuditText.Notes(entries, unreadable, report.Unverified),
        ];

        Assert.Equal(expected, model.Lines);

        // So that an equality between two empty lists can never be what passed.
        Assert.Contains(model.Lines, line => line.Contains("env/prod/GITHUB_TOKEN", StringComparison.Ordinal));
        Assert.Contains(model.Lines, line => line.Contains("decision", StringComparison.Ordinal));
    }

    /// <summary>
    /// A machine no agent has asked anything of is normal, and is told so in one sentence.
    /// </summary>
    [Fact]
    public void An_absent_log_is_a_sentence_rather_than_an_error()
    {
        var model = new LogViewModel(_home.Home);

        Assert.False(File.Exists(_home.LogPath));
        Assert.Empty(model.Lines);
        Assert.False(model.HasLines);
        Assert.True(model.HasMessage);
        Assert.Equal(LogViewModel.NothingYet, model.Message);
        Assert.Empty(model.VerdictLines);
        Assert.False(model.VerifyCommand.CanExecute(null));
    }

    /// <summary>A log with records produces the table, and says nothing alarming about an intact one.</summary>
    [Fact]
    public void A_log_with_records_produces_lines()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);

        Assert.True(model.HasLines);
        Assert.False(model.HasMessage);
        Assert.Contains("env/dev/STRIPE_KEY", model.Text, StringComparison.Ordinal);
        Assert.Contains(_home.LogPath, model.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record appended while the window was open shows up on <see cref="LogViewModel.Refresh"/>.
    /// </summary>
    /// <remarks>
    /// The case this stands for is the ordinary one: keypaste-mcp is a different process, and it
    /// writes to this file while somebody is looking at it.
    /// </remarks>
    [Fact]
    public void Refresh_picks_up_an_appended_record()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);
        var before = model.Lines.Count;

        _home.Append("env/prod/GITHUB_TOKEN");
        Assert.DoesNotContain("env/prod/GITHUB_TOKEN", model.Text, StringComparison.Ordinal);

        model.Refresh();

        Assert.Equal(before + 1, model.Lines.Count);
        Assert.Contains("env/prod/GITHUB_TOKEN", model.Text, StringComparison.Ordinal);
    }

    /// <summary>The same, when a person presses the button rather than the code calling the method.</summary>
    [Fact]
    public void The_refresh_command_re_reads_the_file()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);
        _home.Append("env/prod/GITHUB_TOKEN");

        Assert.True(model.RefreshCommand.CanExecute(null));
        model.RefreshCommand.Execute(null);

        Assert.Contains("env/prod/GITHUB_TOKEN", model.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verdict is on hand from the load, and on screen only when it was asked for.
    /// </summary>
    /// <remarks>
    /// Its last paragraphs are what a passing check does <em>not</em> prove, and that is worth
    /// reading — but it is five paragraphs, and putting them above the table on every load is how a
    /// screen teaches people to stop reading it.
    /// </remarks>
    [Fact]
    public void The_verdict_waits_to_be_asked_for()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);

        Assert.False(model.VerdictShown);
        Assert.NotEmpty(model.VerdictLines);
        Assert.True(model.VerifyCommand.CanExecute(null));

        model.VerifyCommand.Execute(null);

        Assert.True(model.VerdictShown);
        Assert.Contains("verified in", model.VerdictText, StringComparison.Ordinal);

        // It described the file as it was read a moment ago, so a fresh read folds it away.
        model.Refresh();
        Assert.False(model.VerdictShown);
    }

    /// <summary>
    /// An edited file says so on load, and still shows its records.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Saying nothing until somebody pressed a button would let a tampered log
    /// look exactly like an untouched one, and refusing to show the records would hand an attacker
    /// a way to make the log unreadable by editing one byte of it.
    /// </remarks>
    [Fact]
    public void A_broken_chain_is_said_without_hiding_the_records()
    {
        _home.Append("env/dev/STRIPE_KEY", "env/prod/GITHUB_TOKEN");
        _home.Alter();

        var model = new LogViewModel(_home.Home);

        Assert.True(model.HasMessage);
        Assert.Contains("edited", model.Message, StringComparison.Ordinal);
        Assert.True(model.HasLines);
        Assert.Contains("env/prod/GITHUB_TOKEN", model.Text, StringComparison.Ordinal);
        Assert.Contains(AuditText.UnverifiedMark, model.Text, StringComparison.Ordinal);
    }
}
