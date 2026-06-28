using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SmyruRGB;

public partial class App : Application
{
    private const string AppIconAssetUri = "avares://SmyruRGB/Assets/Images/elephant-256x256.png";

    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainWindowViewModel;
    private TrayIcon? _trayIcon;
    private bool _isExitRequested;
    private bool _startupWindowStateApplied;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindowViewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel,
                Icon = CreateAppWindowIcon()
            };

            desktop.MainWindow = _mainWindow;
            InitializeTrayIcon();
            AttachWindowBehavior();
            ApplyStartupWindowBehavior();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            Icon = CreateAppWindowIcon(),
            ToolTipText = "SmyruRGB",
            IsVisible = false
        };

        _trayIcon.Clicked += (_, _) => RestoreMainWindow();

        var menu = new NativeMenu();

        var openMenuItem = new NativeMenuItem("Open SmyruRGB");
        openMenuItem.Click += (_, _) => RestoreMainWindow();
        menu.Add(openMenuItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitMenuItem = new NativeMenuItem("Exit");
        exitMenuItem.Click += (_, _) => ExitApplication();
        menu.Add(exitMenuItem);

        _trayIcon.Menu = menu;
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void AttachWindowBehavior()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.Closed += (_, _) => _trayIcon?.Dispose();
    }

    private void ApplyStartupWindowBehavior()
    {
        if (_mainWindow is null || _mainWindowViewModel?.StartMinimizedEnabled != true)
        {
            return;
        }

        _mainWindow.Opened += OnMainWindowOpened;
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (_startupWindowStateApplied || _mainWindow is null || _mainWindowViewModel?.StartMinimizedEnabled != true)
        {
            return;
        }

        _startupWindowStateApplied = true;
        _mainWindow.Opened -= OnMainWindowOpened;

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.WindowState = WindowState.Minimized;
            if (_mainWindowViewModel?.MinimizeToTrayEnabled == true)
            {
                MinimizeMainWindowToTray();
            }
        }, DispatcherPriority.Background);
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != Window.WindowStateProperty
            || _mainWindow is null
            || _mainWindowViewModel?.MinimizeToTrayEnabled != true
            || _mainWindow.WindowState != WindowState.Minimized)
        {
            return;
        }

        MinimizeMainWindowToTray();
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExitRequested || _mainWindowViewModel?.CloseToTrayEnabled != true)
        {
            return;
        }

        e.Cancel = true;
        MinimizeMainWindowToTray();
    }

    private void MinimizeMainWindowToTray()
    {
        if (_mainWindow is null || _trayIcon is null)
        {
            return;
        }

        _trayIcon.IsVisible = true;
        _mainWindow.Hide();
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Maximized;
        _mainWindow.Activate();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        _trayIcon?.Dispose();

        _mainWindow?.Close();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static WindowIcon CreateAppWindowIcon()
    {
        using var stream = AssetLoader.Open(new Uri(AppIconAssetUri));
        return new WindowIcon(new Bitmap(stream));
    }
}
