using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>The Agent Activity screen; everything it reads and does is <see cref="ViewModels.AgentActivityViewModel"/>'s.</summary>
internal sealed partial class AgentActivityView : UserControl
{
    public AgentActivityView() => AvaloniaXamlLoader.Load(this);
}
