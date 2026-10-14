using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>One agent's settings; everything it reads and does is <see cref="ViewModels.AgentSettingsViewModel"/>'s.</summary>
internal sealed partial class AgentSettingsView : UserControl
{
    public AgentSettingsView() => AvaloniaXamlLoader.Load(this);
}
