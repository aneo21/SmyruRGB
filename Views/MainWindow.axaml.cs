using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
