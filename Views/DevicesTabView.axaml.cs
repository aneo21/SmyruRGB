using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB.Views;

public partial class DevicesTabView : UserControl
{
    public DevicesTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}