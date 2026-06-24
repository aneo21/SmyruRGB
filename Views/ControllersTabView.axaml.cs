using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB.Views;

public partial class ControllersTabView : UserControl
{
    public ControllersTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}