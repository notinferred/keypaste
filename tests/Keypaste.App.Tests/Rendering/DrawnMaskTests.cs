using Avalonia.Controls;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Controls;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Every masked field, typed into through the window and read from the frames Skia drew (4.6).
/// </summary>
/// <remarks>
/// <para>
/// A field is focused by a click the platform hit-tests and typed into one keystroke at a time.
/// At each length the frame is searched for what was typed, in the typeface the field draws in,
/// within the field and, where the value is long enough not to occur by chance, across the whole
/// window. The mask found where the value is not is the positive beside each negative, and the
/// last step of each field makes it draw the characters to show the search would have seen them.
/// </para>
/// <para>
/// The automation half is D-0099 made over the whole window rather than the field: two values of
/// one length sharing no character leave the same surface, and neither is on it.
/// </para>
/// </remarks>
public sealed class DrawnMaskTests
{
    // The first character is the whole one-character value, so no path or label may hold it (F.17).
    private const string _pool = "¤ZXJ#%&@WVKq";
    private const string _alpha = "QZXJ#%&@WVKq";
    private const string _beta = "0123456789ab";

    // The fixture directory whose Q the vault-name tooltip showed in probe run 36033202756 (F.17).
    private const string _recordedFixture = "keypaste-drawn-v7uQny";

    public static TheoryData<string> UnlockFields => ["Password", "BackupPassword", "NewPassword", "ConfirmPassword"];

    public static TheoryData<string> ShellFields =>
    [
        "CurrentPassword", "AccessNewPassword", "AccessConfirmPassword",
        "NewEntryPassword", "ReplacementPassword", "NewEnvValue", "ReplacementEnvValue",
    ];

    [Theory]
    [MemberData(nameof(UnlockFields))]
    public Task An_unlock_field_never_draws_what_is_typed(string name) => HeadlessSession.On(async () =>
    {
        using var screen = new RenderedUnlock();
        NeverDrawn(screen.Window, await screen.OpenField(name));
    });

    [Theory]
    [MemberData(nameof(ShellFields))]
    public Task A_shell_field_never_draws_what_is_typed(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        NeverDrawn(shell.Window, shell.OpenField(name));
    });

    [Fact]
    public Task The_one_character_value_is_on_no_surface_before_typing() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(_recordedFixture);
        var field = shell.OpenField("ReplacementEnvValue");

        Assert.Contains(AutomationSurface.Of(shell.Window), found => found.Text.Contains(_recordedFixture, StringComparison.Ordinal));
        NeverDrawn(shell.Window, field);
    });

    [Theory]
    [MemberData(nameof(ShellFields))]
    public Task Locking_mid_entry_leaves_neither_value_nor_mask_in_the_next_frame(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var field = shell.OpenField(name);
        var value = Secret(24);

        WindowInput.Click(shell.Window, field);
        WindowInput.Type(shell.Window, value);

        var typed = shell.Frame();
        var style = TextStyle.Of(Display(field));
        Assert.True(typed.Shows(field.Display, style, typed.Of(field, shell.Window)), "the mask is not drawn before the lock");

        shell.PressLock();
        Assert.True(shell.IsLocked);

        var locked = shell.Frame();
        Assert.False(locked.Shows(value, style, locked.Everywhere), "the typed value is drawn after the lock");
        Assert.False(locked.Shows(new string('•', value.Length), style, locked.Everywhere), "the mask is drawn after the lock");
        AutomationSurface.AssertNothingExposes(shell.Window, value);
    });

    [Theory]
    [MemberData(nameof(UnlockFields))]
    public Task An_unlock_field_leaves_the_window_surface_the_same_for_any_value(string name) => HeadlessSession.On(async () =>
    {
        using var screen = new RenderedUnlock();
        Differential(screen.Window, await screen.OpenField(name));
    });

    [Theory]
    [MemberData(nameof(ShellFields))]
    public Task A_shell_field_leaves_the_window_surface_the_same_for_any_value(string name) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        Differential(shell.Window, shell.OpenField(name));
    });

    private static void NeverDrawn(Window window, MaskedInput field)
    {
        NotOnScreenBeforeTyping(window);
        WindowInput.Click(window, field);
        var value = string.Empty;

        foreach (var length in new[] { 1, 5, 24, 64 })
        {
            WindowInput.Escape(window);
            Assert.Equal(0, field.MaskedLength);

            value = Secret(length);
            WindowInput.Type(window, value);
            Assert.Equal(length, field.MaskedLength);

            var frame = DrawnFrame.Capture(window);
            var within = frame.Of(field, window);
            var style = TextStyle.Of(Display(field));

            Assert.False(frame.Shows(value, style, within), $"{field.Name} draws a {length}-character value");

            if (length >= 5)
            {
                Assert.False(frame.Shows(value, style, frame.Everywhere), $"the window draws a {length}-character value typed into {field.Name}");
            }

            // The positive beside the negative: what the field does draw is found where it is.
            if (length <= 24)
            {
                Assert.True(frame.Shows(field.Display, style, within), $"{field.Name}'s {length}-dot mask is not found");
            }

            AutomationSurface.AssertNothingExposes(window, value);
        }

        // Had the field drawn the characters, the search above would have found them.
        var leaking = Display(field);
        leaking.Text = value[..24];
        WindowInput.Drain();
        var leaked = DrawnFrame.Capture(window);
        Assert.True(leaked.Shows(value[..24], TextStyle.Of(leaking), leaked.Of(field, window)), $"a value drawn by {field.Name} is not found");
    }

    private static void Differential(Window window, MaskedInput field)
    {
        Assert.Equal(_alpha.Length, _beta.Length);
        Assert.Empty(_alpha.Intersect(_beta));

        WindowInput.Click(window, field);
        WindowInput.Type(window, _alpha);
        var first = AutomationSurface.Of(window);

        WindowInput.Escape(window);
        WindowInput.Type(window, _beta);
        var second = AutomationSurface.Of(window);

        Assert.Equal(_beta.Length, field.MaskedLength);
        Assert.NotEmpty(second);
        Assert.Equal(first, second);
        AutomationSurface.AssertNothingExposes(window, _alpha);
        AutomationSurface.AssertNothingExposes(window, _beta);
    }

    /// <summary>A sweep that finds the one-character value before it is typed would read the window's own text as an exposure.</summary>
    private static void NotOnScreenBeforeTyping(Window window)
    {
        var value = Secret(1);

        foreach (var (source, text) in AutomationSurface.Of(window))
        {
            Assert.False(text.Contains(value, StringComparison.Ordinal), $"{source} holds the one-character value before anything is typed: {text}");
        }
    }

    /// <summary>The template's text block that draws the mask.</summary>
    private static TextBlock Display(MaskedInput field) => DrawnText.Showing(field, field.Display);

    private static string Secret(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => _pool[i % _pool.Length]));
}
