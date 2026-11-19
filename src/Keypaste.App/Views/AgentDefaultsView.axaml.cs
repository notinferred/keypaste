using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Settings › Agents: the defaults every agent gets; everything it reads and does is <see cref="ViewModels.AgentRulesViewModel"/>'s.</summary>
internal sealed partial class AgentDefaultsView : UserControl
{
    public AgentDefaultsView() => AvaloniaXamlLoader.Load(this);
}
