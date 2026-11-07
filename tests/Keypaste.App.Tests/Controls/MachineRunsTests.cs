using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>What a machine reads, set inside a sentence, is drawn in Fragment Mono and the rest stays the sentence's own face (DESIGN Rule 5).</summary>
public sealed class MachineRunsTests
{
    [Fact]
    public Task A_flag_is_not_split_at_its_hyphens_when_the_line_wraps() => HeadlessSession.On(() =>
    {
        foreach (var width in Enumerable.Range(14, 40).Select(step => step * 10d))
        {
            var block = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            MachineRuns.SetInText(block, true);
            block.Text = "If you kept a hash, run it with --client-label and --expect on the command line.";
            block.Measure(new Avalonia.Size(width, double.PositiveInfinity));
            var drawn = string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text));

            foreach (var line in block.TextLayout.TextLines)
            {
                var text = drawn.Substring(line.FirstTextSourceIndex, Math.Min(line.Length, drawn.Length - line.FirstTextSourceIndex));
                Assert.False(text.TrimEnd().TrimEnd('\u2060').EndsWith('-'), $"a flag split at {width}: {text}");
            }
        }
    });

    [Theory]
    [InlineData("Connect changes only /Users/maya/.cursor/mcp.json, and keeps it.", "/Users/maya/.cursor/mcp.json")]
    [InlineData("Keeps it as ~/.cursor/mcp.json.", "~/.cursor/mcp.json")]
    [InlineData("C:\\Users\\maya\\mcp.json is not valid JSON at line 1.", "C:\\Users\\maya\\mcp.json")]
    [InlineData("read:project/environment/KEY, or * for the whole set.", "read:project/environment/KEY")]
    [InlineData("Run it with --client-label work.", "--client-label")]
    [InlineData("Connect changes only /Users/maya/Library/Application Support/Claude/claude_desktop_config.json, and keeps it.", "/Users/maya/Library/Application Support/Claude/claude_desktop_config.json")]
    [InlineData("C:\\Users\\Maya Ochoa\\AppData\\Roaming\\Code\\User\\mcp.json is not valid JSON.", "C:\\Users\\Maya Ochoa\\AppData\\Roaming\\Code\\User\\mcp.json")]
    [InlineData("Open ~/My Vaults/work vault.kdbx now.", "~/My Vaults/work vault.kdbx")]
    [InlineData("Saved to /tmp/a.kdbx. Work/linear is next.", "/tmp/a.kdbx")]
    [InlineData("Saved to /tmp/a then closed.", "/tmp/a")]
    [InlineData("such as env:billing-api, with a value per environment.", "env:billing-api")]
    [InlineData("tag:env:billing is every environment.", "tag:env:billing")]
    [InlineData("It only feeds keypaste run and never prints a value.", "keypaste run")]
    [InlineData("Claude Code starts keypaste mcp for this vault.", "keypaste mcp")]
    public Task A_machine_word_is_its_own_mono_run_and_the_sentence_stays_whole(string sentence, string word) => HeadlessSession.On(() =>
    {
        var block = new TextBlock();
        MachineRuns.SetInText(block, true);
        block.Text = sentence;

        Assert.Equal(sentence, block.Text);
        Assert.Equal(sentence, string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text)).Replace("\u2060", string.Empty, StringComparison.Ordinal));
        Assert.Equal(word, Assert.Single(block.Inlines!.OfType<Run>(), run => Equals(run.FontFamily, Avalonia.Application.Current!.FindResource("KpMono"))).Text!.Replace("\u2060", string.Empty, StringComparison.Ordinal));
    });

    [Theory]
    [InlineData("Saved on this machine, and in force as soon as you change them.")]
    [InlineData("and/or either way")]
    [InlineData("12:30")]
    [InlineData("keypaste runs here")]
    public Task A_sentence_without_one_is_left_alone(string sentence) => HeadlessSession.On(() =>
    {
        var block = new TextBlock();
        MachineRuns.SetInText(block, true);
        block.Text = sentence;

        Assert.Empty(block.Inlines ?? []);
        Assert.Equal(sentence, block.Text);
    });

    [Fact]
    public Task A_named_word_is_set_where_it_first_appears_as_a_whole_word() => HeadlessSession.On(() =>
    {
        var block = new TextBlock();
        MachineRuns.SetWords(block, "KEY|Work/linear");
        block.Text = "New KEYS for KEY on Work/linear, KEY again.";

        Assert.Equal(block.Text, string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text)));
        var mono = block.Inlines!.OfType<Run>().Where(run => Equals(run.FontFamily, Avalonia.Application.Current!.FindResource("KpMono"))).Select(run => run.Text);
        Assert.Equal(["KEY", "Work/linear"], mono);
    });

    [Fact]
    public Task A_later_sentence_replaces_the_runs() => HeadlessSession.On(() =>
    {
        var block = new TextBlock();
        MachineRuns.SetInText(block, true);
        block.Text = "Open /tmp/a.kdbx now";
        block.Text = "Nothing to open";

        Assert.Empty(block.Inlines ?? []);
    });
}
