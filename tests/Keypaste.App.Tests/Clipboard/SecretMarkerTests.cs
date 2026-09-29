using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Keypaste.App.Clipboard;
using Xunit;

namespace Keypaste.App.Tests.Clipboard;

/// <summary>
/// V-N.12's first half: a secret reaches the platform as one item holding the text and every marker,
/// and a plain copy as the text alone.
/// </summary>
/// <remarks>
/// The headless clipboard keeps the data exactly as it was handed over, so this holds what the app
/// asks for, not what a real clipboard offers afterwards; <c>scripts/verify-clipboard-markers.sh</c>
/// reads that from the macOS pasteboard and an X11 clipboard.
/// </remarks>
public sealed class SecretMarkerTests
{
    private const string _secret = "marker-test-secret";
    private const string _command = "keypaste run billing -- npm start";

    [Fact]
    public Task A_secret_is_one_item_holding_the_text_and_every_marker() => HeadlessSession.On(async () =>
    {
        var window = new Window();
        window.Show();

        Assert.True(await new AvaloniaClipboard(window).TrySetSecretAsync(_secret));

        var data = await window.Clipboard!.TryGetDataAsync();
        var item = Assert.Single(Assert.IsAssignableFrom<IAsyncDataTransfer>(data).Items);
        Assert.Equal(_secret, await item.TryGetTextAsync());
        Assert.Equal(1 + AvaloniaClipboard.SecretMarkers.Count, item.Formats.Count);

        foreach (var (name, content) in AvaloniaClipboard.SecretMarkers)
        {
            var format = DataFormat.CreateBytesPlatformFormat(name);
            Assert.Contains(format, item.Formats);
            Assert.Equal(content, Assert.IsType<byte[]>(await item.TryGetRawAsync(format)));
            Assert.DoesNotContain(_secret, Encoding.UTF8.GetString(content), StringComparison.Ordinal);
        }

        var hint = await item.TryGetRawAsync(DataFormat.CreateBytesPlatformFormat("x-kde-passwordManagerHint"));
        Assert.Equal("secret", Encoding.ASCII.GetString(Assert.IsType<byte[]>(hint)));
        window.Close();
    });

    [Fact]
    public Task A_plain_copy_carries_no_marker() => HeadlessSession.On(async () =>
    {
        var window = new Window();
        window.Show();

        Assert.True(await new AvaloniaClipboard(window).TrySetPlainAsync(_command));

        var data = await window.Clipboard!.TryGetDataAsync();
        var item = Assert.Single(Assert.IsAssignableFrom<IAsyncDataTransfer>(data).Items);
        Assert.Equal(_command, await item.TryGetTextAsync());
        Assert.Equal([DataFormat.Text], item.Formats);
        window.Close();
    });
}
