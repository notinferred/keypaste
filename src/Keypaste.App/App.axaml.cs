using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Keypaste.App;

/// <summary>
/// The application object, and the one place the app's objects are constructed.
/// </summary>
/// <remarks>
/// There is no dependency-injection container. Composition is a handful of objects built by hand in
/// one method, which is what <c>CliContext.CreateDefault</c> already does on the other side of this
/// repository, and it is easier to follow than a container for something this size.
/// </remarks>
internal sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
