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
    private readonly AppSettingsStore _settingsStore = new();
    private TrayIcon? _trayIcon;
    private bool _isExitRequested;
    private bool _isApplyingSavedWindowPlacement;
    private bool _startupWindowStateApplied;
    private bool _startupVisibilityApplied;
    private bool _initialPlacementApplied;
    private PixelPoint? _pendingWindowPosition;
    private WindowState? _pendingWindowState;
    private WindowState _lastVisibleWindowState = WindowState.Normal;

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

            ApplySavedWindowPlacement();

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
        _mainWindow.Opened += OnMainWindowOpenedApplyPlacement;
        _mainWindow.Opened += OnMainWindowOpenedEnsureVisible;
        _mainWindow.PositionChanged += (_, _) => SaveWindowPlacement();
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.Closed += (_, _) =>
        {
            SaveWindowPlacement();
            _trayIcon?.Dispose();
        };
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

    private void OnMainWindowOpenedApplyPlacement(object? sender, EventArgs e)
    {
        if (_initialPlacementApplied || _mainWindow is null)
        {
            return;
        }

        _initialPlacementApplied = true;
        PixelPoint? position = _pendingWindowPosition;
        WindowState? state = _pendingWindowState;
        _pendingWindowPosition = null;
        _pendingWindowState = null;

        // On some platforms initial startup placement is resolved after Opened,
        // so apply saved coordinates on the next UI cycle.
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _isApplyingSavedWindowPlacement = true;
            try
            {
                if (position.HasValue)
                {
                    _mainWindow.Position = position.Value;
                }

                if (state.HasValue && state.Value != WindowState.Normal)
                {
                    _mainWindow.WindowState = state.Value;
                }
            }
            finally
            {
                _isApplyingSavedWindowPlacement = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnMainWindowOpenedEnsureVisible(object? sender, EventArgs e)
    {
        if (_startupVisibilityApplied || _mainWindow is null || _mainWindowViewModel?.StartMinimizedEnabled == true)
        {
            return;
        }

        _startupVisibilityApplied = true;

        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Show();
            _mainWindow.Activate();

            if (_trayIcon is not null)
            {
                _trayIcon.IsVisible = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (change.Property == Window.WindowStateProperty)
        {
            if (_mainWindow.WindowState != WindowState.Minimized)
            {
                _lastVisibleWindowState = _mainWindow.WindowState;
                SaveWindowPlacement();
            }

            if (_mainWindowViewModel?.MinimizeToTrayEnabled == true
                && _mainWindow.WindowState == WindowState.Minimized)
            {
                MinimizeMainWindowToTray();
            }

            return;
        }

        if (change.Property == Window.WidthProperty || change.Property == Window.HeightProperty)
        {
            SaveWindowPlacement();
        }
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
        _mainWindow.WindowState = _lastVisibleWindowState == WindowState.Minimized
            ? WindowState.Normal
            : _lastVisibleWindowState;
        _mainWindow.Activate();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        SaveWindowPlacement();
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

    private void ApplySavedWindowPlacement()
    {
        if (_mainWindow is null)
        {
            return;
        }

        AppSettingsStore.PersistedSettings settings = _settingsStore.Load();
        AppSettingsStore.UiSettings ui = settings.Ui;

        _isApplyingSavedWindowPlacement = true;
        try
        {
            if (ui.WindowWidth.HasValue && ui.WindowWidth.Value >= 320)
            {
                _mainWindow.Width = ui.WindowWidth.Value;
            }

            if (ui.WindowHeight.HasValue && ui.WindowHeight.Value >= 240)
            {
                _mainWindow.Height = ui.WindowHeight.Value;
            }

            if (ui.WindowX.HasValue && ui.WindowY.HasValue)
            {
                _pendingWindowPosition = new PixelPoint((int)Math.Round(ui.WindowX.Value), (int)Math.Round(ui.WindowY.Value));
            }

            if (Enum.TryParse(ui.WindowState, ignoreCase: true, out WindowState savedState)
                && savedState != WindowState.Minimized)
            {
                _pendingWindowState = savedState;
                _lastVisibleWindowState = savedState;
            }
        }
        finally
        {
            _isApplyingSavedWindowPlacement = false;
        }
    }

    private void SaveWindowPlacement()
    {
        if (_mainWindow is null || _isApplyingSavedWindowPlacement)
        {
            return;
        }

        AppSettingsStore.PersistedSettings settings = _settingsStore.Load();
        AppSettingsStore.UiSettings ui = settings.Ui;

        WindowState stateToSave = _mainWindow.WindowState == WindowState.Minimized
            ? _lastVisibleWindowState
            : _mainWindow.WindowState;

        ui.WindowState = stateToSave.ToString();

        if (stateToSave == WindowState.Normal)
        {
            ui.WindowWidth = _mainWindow.Width;
            ui.WindowHeight = _mainWindow.Height;
            ui.WindowX = _mainWindow.Position.X;
            ui.WindowY = _mainWindow.Position.Y;
        }

        _settingsStore.Save(settings);
    }
}
