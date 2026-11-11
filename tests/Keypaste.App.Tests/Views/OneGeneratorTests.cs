using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>A project's forms make a value with the same generator as every other form: Apply puts it in the field, Cancel leaves the field as it was.</summary>
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
        Assert.True(shell.Named<Border>("ReplacementEnvGeneratorPanel").IsEffectivelyVisible);
        Assert.Single(shell.Window.GetVisualDescendants().OfType<GeneratorPanel>(), panel => panel.IsEffectivelyVisible);

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
}
