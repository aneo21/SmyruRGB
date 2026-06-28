using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB.Views;

public partial class CanvasTabView : UserControl
{
    public CanvasTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}