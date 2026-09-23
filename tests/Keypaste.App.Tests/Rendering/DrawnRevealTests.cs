using Keypaste.App.Controls;
using Keypaste.App.Tests.Controls;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Each surface that reveals a secret, read from the frames Skia drew for it (4.6).
/// </summary>
/// <remarks>
/// <para>
/// The hold is a mouse press on the window that the platform hit-tests to the drawn cell, and every
/// claim that a value is or is not on screen is read from a captured frame by
/// <see cref="DrawnFrame"/>, never from <see cref="RevealedValue.Rendered"/>. A negative is always
/// made in a frame beside the positive it would have had to miss.
/// </para>
/// <para>
/// The automation half is D-0232's window differential, now made for the env value too: the
/// window's surface while a value is drawn equals the surface at rest, and neither carries it.
/// </para>
/// </remarks>
public sealed class DrawnRevealTests
{
    public static TheoryData<string> Surfaces => ["current", "revision", "env"];

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Holding_draws_the_value_and_releasing_takes_it_off_the_frame(string surface) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var (cell, value) = shell.Open(surface);
        var style = TextStyle.Of(cell);
        var dots = new string('•', cell.MaskedLength);

        // At rest with the pointer already over the cell, so the only difference the press can make
        // is the press: hovering changes IsPointerOver on the cell and every ancestor.
        shell.Hover(cell);
        var atRest = shell.Frame();
        var surfaceAtRest = AutomationSurface.Of(shell.Window);
        Assert.True(atRest.Shows(dots, style, atRest.Of(cell, shell.Window)), "the cell's dots are not drawn at rest");
        AssertNoSecretIn(atRest, style);

        shell.Press(cell);
        var held = shell.Frame();
        var surfaceWhileHeld = AutomationSurface.Of(shell.Window);
        Assert.True(held.Shows(value, style, held.Of(cell, shell.Window)), "the held value is not drawn in its cell");
        Assert.False(held.Shows(dots, style, held.Of(cell, shell.Window)), "the dots are still drawn while the value is held");
        foreach (var other in RenderedShell.Secrets.Where(secret => secret != value))
        {
            Assert.False(held.Shows(other, style, held.Everywhere), "a secret nobody is holding is drawn");
        }

        Assert.NotEmpty(surfaceWhileHeld);
        Assert.Equal(surfaceAtRest, surfaceWhileHeld);
        AssertNoSecretExposed(shell);

        shell.Release(cell);
        var released = shell.Frame();
        Assert.True(released.Shows(dots, style, released.Of(cell, shell.Window)), "the dots are not drawn after release");
        AssertNoSecretIn(released, style);
        AssertNoSecretExposed(shell);
    });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Locking_mid_hold_takes_the_value_off_the_next_frame(string surface) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var (cell, value) = shell.Open(surface);
        var style = TextStyle.Of(cell);

        shell.Press(cell);
        var held = shell.Frame();
        Assert.True(held.Shows(value, style, held.Everywhere), "the held value is not drawn before the lock");

        shell.PressLock();

        Assert.True(shell.IsLocked);
        Assert.Null(shell.Session.Unlocked);

        var locked = shell.Frame();
        var password = shell.Named<MaskedInput>("Password");
        var placeholder = DrawnText.Showing(password, password.Placeholder);
        Assert.True(
            locked.Shows(password.Placeholder, TextStyle.Of(placeholder), locked.Of(placeholder, shell.Window)),
            "the unlock screen is not what the next frame draws");
        AssertNoSecretIn(locked, style);
        AssertNoSecretExposed(shell);
    });

    private static void AssertNoSecretIn(DrawnFrame frame, TextStyle style)
    {
        foreach (var secret in RenderedShell.Secrets)
        {
            Assert.False(frame.Shows(secret, style, frame.Everywhere), $"a {secret.Length}-character secret is drawn");
        }
    }

    private static void AssertNoSecretExposed(RenderedShell shell)
    {
        foreach (var secret in RenderedShell.Secrets)
        {
            AutomationSurface.AssertNothingExposes(shell.Window, secret);
        }
    }
}
