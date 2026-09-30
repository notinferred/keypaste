namespace Keypaste.App.Tests;

/// <summary>The instant the app's tests and rendered screens were written against, which is not the core clock's default.</summary>
internal static class AppClock
{
    internal static readonly DateTimeOffset Start = new(2026, 7, 28, 9, 12, 44, TimeSpan.Zero);
}
