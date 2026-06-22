using Avalonia;
using Avalonia.ReactiveUI;

namespace SmyruRGB;

internal class Program
{
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SmyruRGB.App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
