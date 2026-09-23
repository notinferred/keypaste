using Avalonia;
using Avalonia.Headless;

namespace Keypaste.App.Tests;

/// <summary>
/// The one headless Avalonia session this assembly is allowed to have.
/// </summary>
/// <remarks>
/// <para>
/// <b>One per process, and that is not a style choice.</b> A second
/// <see cref="HeadlessUnitTestSession"/> throws <c>InvalidOperationException: A URI scheme name
/// 'avares' already has a registered custom parser</c> — Avalonia registers that parser globally
/// during setup and nothing unregisters it. Two test classes each starting their own session fail
/// in a way that reads like a threading bug rather than what it is, so every UI test in this
/// assembly goes through this one.
/// </para>
/// <para>
/// <b><c>Avalonia.Headless.XUnit</c> is deliberately not referenced.</b> It supplies an
/// <c>[AvaloniaFact]</c> attribute over this same mechanism, and a spike showed the session drives
/// xunit.v3 directly under this repository's Microsoft.Testing.Platform runner. One fewer package
/// on a repository that counts them (docs/PRODUCT.md law 3.9).
/// </para>
/// <para>
/// Most of this stage's tests need none of this. <c>AppVaultSession</c> and the settings and
/// recent-vault stores name no Avalonia type, so they are ordinary facts against a
/// <see cref="TimeProvider"/> — which is faster, deterministic, and where the security assertions
/// live. This session is for the handful of claims that are only true of a real visual tree.
/// </para>
/// </remarks>
internal static class HeadlessSession
{
    internal static HeadlessUnitTestSession Instance { get; } =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessSession));

    /// <summary>The app drawn by Skia, so a test can read what a frame actually shows (4.6).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>Runs <paramref name="body"/> on the session's UI thread.</summary>
    internal static Task On(Action body) => Instance.Dispatch(body, CancellationToken.None);

    /// <summary>Runs an asynchronous <paramref name="body"/> on the session's UI thread.</summary>
    /// <remarks>
    /// <para>
    /// <b>Without this overload an <c>async</c> lambda binds to <see cref="On(Action)"/> as
    /// <c>async void</c>.</b> The returned task then completes at the body's first <c>await</c>, so
    /// every assertion after it runs outside the lifetime xunit observes and a failure surfaces as
    /// an unobserved exception rather than a red test. Four tests in
    /// <c>MaskedInputAutomationTests</c> were written that way, two of them D-0099 differentials.
    /// </para>
    /// <para>
    /// <see cref="HeadlessUnitTestSession"/> offers <c>Dispatch(Action)</c>,
    /// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;)</c> and <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;)</c>
    /// and no non-generic <c>Func&lt;Task&gt;</c>, which is why this returns a value nobody wants.
    /// </para>
    /// </remarks>
    internal static Task On(Func<Task> body) =>
        Instance.Dispatch(
            async () =>
            {
                ArgumentNullException.ThrowIfNull(body);
                await body().ConfigureAwait(true);
                return true;
            },
            CancellationToken.None);
}
