using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests;

public sealed class OpenerProcessTests : IDisposable
{
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);
    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-opener-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("cursor://anysphere.cursor-deeplink/mcp/install?name=keypaste&config=eyJjb21tYW5kIjoifi9iaW4ifQ%3D%3D")]
    [InlineData("vscode:mcp/install?%7B%22command%22%3A%22~/bin/keypaste%22%7D")]
    [InlineData("https://example.com/c?k=$HOME&x=1#f")]
    public async Task The_opener_receives_the_address_byte_for_byte(string link)
    {
        var (opener, received) = Stub(exit: 0);
        var address = new Uri(link);

        Assert.True(await new OpenerProcess(opener, _patience).OpenAsync(address));

        Assert.Equal(address.AbsoluteUri + "\n", File.ReadAllText(received));
    }

    [Fact]
    public async Task An_address_nothing_handles_is_not_opened()
    {
        var (opener, _) = Stub(exit: 4);

        Assert.False(await new OpenerProcess(opener, _patience).OpenAsync(new Uri("cursor://anysphere.cursor-deeplink/mcp/install?name=keypaste")));
    }

    [Fact]
    public async Task A_missing_opener_is_not_opened()
    {
        Assert.False(await new OpenerProcess(Path.Combine(_root, "absent"), _patience).OpenAsync(new Uri("https://example.com/")));
    }

    private (string Opener, string Received) Stub(int exit)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows keeps Avalonia's launcher, which starts no shell.");

        var received = Path.Combine(_root, "received");
        var opener = Path.Combine(_root, "opener");
        File.WriteAllText(opener, $"#!/bin/sh\nprintf '%s\\n' \"$1\" > '{received}'\nexit {exit}\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(opener, _executable);
        }

        return (opener, received);
    }
}
