using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.Core;

namespace Keypaste.App.Views;

/// <summary>About keypaste: its version and copyright, the same on every platform.</summary>
internal sealed partial class AboutWindow : PromptWindow
{
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("VersionText")!.Text = Version;
        this.FindControl<TextBlock>("CopyrightText")!.Text = Copyright;

        var dismiss = this.FindControl<Button>("Dismiss")!;
        dismiss.Click += (_, _) => Close();
        Opened += (_, _) => dismiss.Focus();
    }

    /// <summary>What the window says this copy is.</summary>
    internal static string Version => $"Version {CoreInfo.Version}";

    /// <summary>The copyright the build carries, from Directory.Build.props.</summary>
    internal static string Copyright =>
        typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright keypaste";
}
