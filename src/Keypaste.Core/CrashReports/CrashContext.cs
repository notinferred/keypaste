namespace Keypaste.Core.CrashReports;

/// <summary>What the app knows when something fails, beyond the exception: nothing here comes from a vault.</summary>
/// <param name="At">When it failed; a report keeps only the UTC date.</param>
/// <param name="Uptime">How long the app had run, which a report keeps only as a coarse bucket, or null when unknown.</param>
/// <param name="Screen">The app's name for the screen open, such as <c>agents</c>, or null when none was.</param>
public sealed record CrashContext(DateTimeOffset At, TimeSpan? Uptime, string? Screen);
