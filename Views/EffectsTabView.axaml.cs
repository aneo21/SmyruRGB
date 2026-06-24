using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmyruRGB.Views;

public partial class EffectsTabView : UserControl
{
    public EffectsTabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}