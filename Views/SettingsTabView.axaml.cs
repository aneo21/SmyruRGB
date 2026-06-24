using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB.Views;

public partial class SettingsTabView : UserControl
{
    public SettingsTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
