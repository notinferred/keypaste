using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Every form makes a value with the same generator, one small sheet over the window: Apply puts it in the field, Cancel leaves the field as it was.</summary>
public sealed class OneGeneratorTests
{
    [Fact]
    public Task A_projects_replace_form_takes_the_generators_value_only_on_Apply() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-one-generator-");
        var project = shell.OpenProject();
        project.BeginReplace(project.Variables[0]);
        RenderedShell.Drain();

        shell.Tap(shell.Named<Button>("ReplacementEnvGenerate"));
        AssertOneSheetOverTheWindow(shell);

        shell.Tap(shell.Named<Button>("CloseGenerator"));
        Assert.Null(project.ReplacementGenerator);
        Assert.False(project.ReplacementValue.HasValue);

        shell.Tap(shell.Named<Button>("ReplacementEnvGenerate"));
        var chosen = project.ReplacementGenerator!.Reveal();
        shell.Tap(shell.Named<Button>("ApplyGenerated"));

        Assert.Null(project.ReplacementGenerator);
        Assert.Equal(chosen, project.ReplacementValue.Compose());
        Assert.True(project.IsReplacing);
    });

    [Fact]
    public Task A_new_keys_generator_starts_from_the_value_the_form_generated() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-one-generator-new-");
        var project = shell.OpenProject();
        project.BeginAddCommand.Execute(null);
        RenderedShell.Drain();
        var generated = project.NewValue.Compose();

        project.OpenGeneratorCommand.Execute(project.NewValue);
        var panel = project.NewValueGenerator!;
        Assert.Equal(generated, panel.Reveal());

        panel.Generator.Length = 40;
        Assert.Equal(generated, project.NewValue.Compose());

        panel.ApplyCommand.Execute(null);
        Assert.Equal(40, project.NewValue.MaskedLength);
        Assert.True(project.GenerateValue);

        project.CancelAddCommand.Execute(null);
        Assert.Null(project.NewValueGenerator);
    });

    [Fact]
    public Task New_item_edit_and_a_field_form_open_the_same_sheet_and_change_their_field_only_on_Apply() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-one-generator-sheet-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        items.BeginAddCommand.Execute(null);
        RenderedShell.Drain();
        var typed = items.NewItem!.Password.Compose();
        shell.Tap(shell.Named<Button>("NewEntryGenerate"));
        AssertOneSheetOverTheWindow(shell);
        items.NewItem.GeneratorPanel!.Generator.Length = 33;
        Assert.Equal(typed, items.NewItem.Password.Compose());
        shell.Tap(shell.Named<Button>("ApplyGenerated"));
        Assert.Equal(33, items.NewItem.Password.MaskedLength);
        Assert.Null(items.NewItem.GeneratorPanel);
        items.CancelAddCommand.Execute(null);
        RenderedShell.Drain();

        items.Selected = items.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        items.Detail!.EditCommand.Execute(null);
        RenderedShell.Drain();
        shell.Tap(shell.Named<Button>("ReplacementPasswordGenerate"));
        AssertOneSheetOverTheWindow(shell);
        Assert.True(shell.Named<Button>("ApplyGenerated").IsEffectivelyVisible);
        shell.Tap(shell.Named<Button>("CloseGenerator"));
        Assert.False(items.Detail.NewPassword.HasValue);
        Assert.Equal("ReplacementPasswordGenerate", (shell.Window.FocusManager?.GetFocusedElement() as Control)?.Name);

        // A choice the generator refuses leaves Apply off.
        shell.Tap(shell.Named<Button>("ReplacementPasswordGenerate"));
        var panel = items.Detail.GeneratorPanel!;
        panel.Generator.Letters = false;
        panel.Generator.Digits = false;
        panel.Generator.Symbols = false;
        RenderedShell.Drain();
        Assert.False(shell.Named<Button>("ApplyGenerated").IsEffectivelyEnabled);
        panel.CancelCommand.Execute(null);
        items.Detail.CancelCommand.Execute(null);
        RenderedShell.Drain();

        shell.Shell.OpenGeneratorCommand.Execute(null);
        RenderedShell.Drain();
        AssertOneSheetOverTheWindow(shell);
        Assert.False(shell.Named<Button>("ApplyGenerated").IsEffectivelyVisible);
        Assert.True(shell.Named<Button>("CopyGenerated").IsEffectivelyVisible);
        Assert.True(shell.Named<Button>("GeneratedNewItem").IsEffectivelyVisible);
        Assert.True(shell.Named<Button>("AddGeneratedField").IsEffectivelyVisible);
    });

    private static void AssertOneSheetOverTheWindow(RenderedShell shell)
    {
        RenderedShell.Drain();
        var sheet = Assert.Single(shell.Window.GetVisualDescendants().OfType<GeneratePasswordView>());
        Assert.True(sheet.IsEffectivelyVisible);
        Assert.Single(shell.Window.GetVisualDescendants().OfType<GeneratorPanel>());
        Assert.Equal(shell.Window.GetVisualDescendants().OfType<ShellView>().Single().Bounds.Size, sheet.Bounds.Size);
        Assert.Equal(400, shell.Named<Border>("GenerateDialog").Bounds.Width);
    }
}
