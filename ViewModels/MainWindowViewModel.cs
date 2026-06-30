using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Media;
using ReactiveUI;
using SmyruRGB.Controllers.Hid;
using SmyruRGB.Effects;
using System.Reactive;

namespace SmyruRGB;

public class MainWindowViewModel : ReactiveObject
{
    private const double CanvasDeviceWidth = 0.28;
    private const double CanvasDevicePadding = 0.018;
    private const double CanvasDeviceHeaderHeight = 0.052;
    private const double CanvasChannelGap = 0.018;
    private const double CanvasBarHeight = 0.12;
    private const double CanvasPizzaHeight = 0.22;
    private const double CanvasKeyboardHeight = 0.18;
    private const int CanvasBackgroundColumns = 40;
    private const int CanvasBackgroundRows = 24;

    private readonly SmyruRgbHidClient _client = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly WindowsStartupRegistration _windowsStartupRegistration = new();
    private readonly Dictionary<string, DeviceDraftState> _deviceDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, AppSettingsStore.ChannelSettings>> _groupedDeviceChannelOverrides = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _canvasPreviewTimer;
    private CancellationTokenSource? _effectCancellationTokenSource;
    private long _canvasPreviewTick;
    private Views.CanvasPreviewSceneSnapshot? _canvasPreviewScene;
    private string? _currentDeviceIdentifier;
    private bool _isUpdatingControllerSelection;
    private bool _isLoadingUiSettings;
    private bool _isControllerDetailsVisible;
    private string _baseColorHex = "FF0000";
    private string _backgroundColorHex = "000000";

    public string AppVersion { get; } = GetAppVersion();

    public MainWindowViewModel()
    {
        RefreshCommand = ReactiveCommand.Create(() => RefreshConnection(false));
        SaveProfileCommand = ReactiveCommand.Create(SaveCurrentProfile, this.WhenAnyValue(
            x => x.IsConnected,
            x => x.ProfileName,
            (connected, profileName) => connected && !string.IsNullOrWhiteSpace(profileName)));
        LoadProfileCommand = ReactiveCommand.Create(LoadSelectedProfile, this.WhenAnyValue(
            x => x.IsConnected,
            x => x.SelectedProfileName,
            (connected, profileName) => connected && !string.IsNullOrWhiteSpace(profileName)));
        OpenControllerDetailsCommand = ReactiveCommand.Create<ControllerOption>(OpenControllerDetails);
        BackToControllerListCommand = ReactiveCommand.Create(CloseControllerDetails);
        ApplyColorCommand = ReactiveCommand.Create(StartSelectedEffect, this.WhenAnyValue(x => x.IsConnected));
        StopEffectCommand = ReactiveCommand.Create(StopActiveEffect, this.WhenAnyValue(x => x.IsEffectRunning));
        SelectAllCanvasDevicesCommand = ReactiveCommand.Create(() => SetCanvasDeviceSelection(true));
        ClearCanvasDeviceSelectionCommand = ReactiveCommand.Create(() => SetCanvasDeviceSelection(false));
        MoveCanvasDeviceCommand = ReactiveCommand.Create<Views.CanvasDeviceMoveRequest>(MoveCanvasDevice);

        _canvasPreviewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _canvasPreviewTimer.Tick += (_, _) =>
        {
            _canvasPreviewTick++;
            RefreshCanvasPreview();
        };

        ResetChannelLedSettings(_client.Channels);
        foreach (ILedEffect effect in EffectCatalog.All)
        {
            AvailableEffects.Add(effect.Name);
        }

        LoadUiSettings();
        ProfileName = "Default";
        SelectedEffectName = EffectCatalog.DefaultEffectName;
        UpdateSelectedEffectState(null);

        RefreshConnection(true);
        RefreshCanvasPreview();
        _canvasPreviewTimer.Start();
    }

    private static string GetAppVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "unknown" : version.ToString(3);
    }

    private int _r = 255;
    public int R
    {
        get => _r;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_r == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _r, normalizedValue);
            SyncBaseColorState();
            this.RaisePropertyChanged(nameof(BaseColorPreviewBrush));
        }
    }

    private int _g;
    public int G
    {
        get => _g;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_g == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _g, normalizedValue);
            SyncBaseColorState();
            this.RaisePropertyChanged(nameof(BaseColorPreviewBrush));
        }
    }

    private int _b;
    public int B
    {
        get => _b;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_b == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _b, normalizedValue);
            SyncBaseColorState();
            this.RaisePropertyChanged(nameof(BaseColorPreviewBrush));
        }
    }

    public Color BaseColor
    {
        get => Color.FromRgb((byte)R, (byte)G, (byte)B);
        set
        {
            if (R == value.R && G == value.G && B == value.B)
            {
                return;
            }

            R = value.R;
            G = value.G;
            B = value.B;
            SyncBaseColorState();
        }
    }

    public string BaseColorHex
    {
        get => _baseColorHex;
        set
        {
            string normalizedValue = NormalizeHexInput(value);
            if (string.Equals(_baseColorHex, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _baseColorHex, normalizedValue);

            if (TryParseHexColor(normalizedValue, out Color parsedColor))
            {
                BaseColor = parsedColor;
            }
        }
    }

    private bool _backgroundColorEnabled;
    public bool BackgroundColorEnabled
    {
        get => _backgroundColorEnabled;
        set
        {
            if (_backgroundColorEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _backgroundColorEnabled, value);
            this.RaisePropertyChanged(nameof(ShowsBackgroundColorControls));
            this.RaisePropertyChanged(nameof(BackgroundColorPreviewBrush));
        }
    }

    private int _backgroundR;
    public int BackgroundR
    {
        get => _backgroundR;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_backgroundR == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _backgroundR, normalizedValue);
            SyncBackgroundColorState();
            this.RaisePropertyChanged(nameof(BackgroundColorPreviewBrush));
        }
    }

    private int _backgroundG;
    public int BackgroundG
    {
        get => _backgroundG;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_backgroundG == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _backgroundG, normalizedValue);
            SyncBackgroundColorState();
            this.RaisePropertyChanged(nameof(BackgroundColorPreviewBrush));
        }
    }

    private int _backgroundB;
    public int BackgroundB
    {
        get => _backgroundB;
        set
        {
            int normalizedValue = Math.Clamp(value, 0, 255);
            if (_backgroundB == normalizedValue)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _backgroundB, normalizedValue);
            SyncBackgroundColorState();
            this.RaisePropertyChanged(nameof(BackgroundColorPreviewBrush));
        }
    }

    public Color BackgroundColor
    {
        get => Color.FromRgb((byte)BackgroundR, (byte)BackgroundG, (byte)BackgroundB);
        set
        {
            if (BackgroundR == value.R && BackgroundG == value.G && BackgroundB == value.B)
            {
                return;
            }

            BackgroundR = value.R;
            BackgroundG = value.G;
            BackgroundB = value.B;
            SyncBackgroundColorState();
        }
    }

    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set
        {
            string normalizedValue = NormalizeHexInput(value);
            if (string.Equals(_backgroundColorHex, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _backgroundColorHex, normalizedValue);

            if (TryParseHexColor(normalizedValue, out Color parsedColor))
            {
                BackgroundColor = parsedColor;
            }
        }
    }

    public ObservableCollection<ChannelLedSetting> ChannelLedSettings { get; } = [];
    public ObservableCollection<ControllerOption> AvailableControllers { get; } = [];
    public ObservableCollection<DeviceChannelGroup> AllDeviceChannelGroups { get; } = [];
    public ObservableCollection<DeviceChannelGroup> CanvasDeviceChannelGroups { get; } = [];
    public ObservableCollection<string> AvailableProfiles { get; } = [];
    public ObservableCollection<string> AvailableEffects { get; } = [];
    public ObservableCollection<EffectParameterSetting> EffectParameters { get; } = [];
    public ObservableCollection<EffectParameterGroup> EffectParameterGroups { get; } = [];

    public Views.CanvasPreviewSceneSnapshot? CanvasPreviewScene
    {
        get => _canvasPreviewScene;
        private set => this.RaiseAndSetIfChanged(ref _canvasPreviewScene, value);
    }

    public bool IsControllerDetailsVisible
    {
        get => _isControllerDetailsVisible;
        private set => this.RaiseAndSetIfChanged(ref _isControllerDetailsVisible, value);
    }

    public string SelectedControllerTitle => SelectedController?.DisplayName ?? "Controller";

    private ControllerOption? _selectedController;
    public ControllerOption? SelectedController
    {
        get => _selectedController;
        set
        {
            if (ReferenceEquals(_selectedController, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedController, value);
            this.RaisePropertyChanged(nameof(SelectedControllerTitle));
            if (!_isUpdatingControllerSelection && value is not null
                && !string.Equals(value.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
            {
                RememberCurrentDeviceDraft();
                RefreshConnection(false);
            }
        }
    }

    private string _deviceSummary = "No device";
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set => this.RaiseAndSetIfChanged(ref _deviceSummary, value);
    }

    private int _maxLedsPerChannel = 126;
    public int MaxLedsPerChannel
    {
        get => _maxLedsPerChannel;
        private set => this.RaiseAndSetIfChanged(ref _maxLedsPerChannel, value);
    }

    private int _channelCount = 8;
    public int ChannelCount
    {
        get => _channelCount;
        private set => this.RaiseAndSetIfChanged(ref _channelCount, value);
    }

    private string _status = "Initializing connection...";
    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => this.RaiseAndSetIfChanged(ref _isConnected, value); }

    private bool _isEffectRunning;
    public bool IsEffectRunning
    {
        get => _isEffectRunning;
        private set => this.RaiseAndSetIfChanged(ref _isEffectRunning, value);
    }

    private string _selectedEffectName = EffectCatalog.DefaultEffectName;
    public string SelectedEffectName
    {
        get => _selectedEffectName;
        set
        {
            string normalizedEffectName = EffectCatalog.NormalizeEffectName(value);
            this.RaiseAndSetIfChanged(ref _selectedEffectName, normalizedEffectName);
            UpdateSelectedEffectState(null);
        }
    }

    private string _selectedEffectDescription = string.Empty;
    public string SelectedEffectDescription
    {
        get => _selectedEffectDescription;
        private set => this.RaiseAndSetIfChanged(ref _selectedEffectDescription, value);
    }

    private int _effectSpeed = 50;
    public int EffectSpeed
    {
        get => _effectSpeed;
        set => this.RaiseAndSetIfChanged(ref _effectSpeed, Math.Clamp(value, 1, 100));
    }

    private bool _showsBaseColorControls = true;
    public bool ShowsBaseColorControls
    {
        get => _showsBaseColorControls;
        private set => this.RaiseAndSetIfChanged(ref _showsBaseColorControls, value);
    }

    private bool _supportsBackgroundColor;
    public bool SupportsBackgroundColor
    {
        get => _supportsBackgroundColor;
        private set
        {
            if (_supportsBackgroundColor == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _supportsBackgroundColor, value);
            this.RaisePropertyChanged(nameof(ShowsBackgroundColorControls));
            this.RaisePropertyChanged(nameof(BackgroundColorPreviewBrush));
        }
    }

    public bool ShowsBackgroundColorControls => SupportsBackgroundColor && BackgroundColorEnabled;

    private bool _minimizeToTrayEnabled;
    public bool MinimizeToTrayEnabled
    {
        get => _minimizeToTrayEnabled;
        set
        {
            if (_minimizeToTrayEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _minimizeToTrayEnabled, value);
            if (_isLoadingUiSettings)
            {
                return;
            }

            SaveUiSettings();
        }
    }

    private bool _closeToTrayEnabled;
    public bool CloseToTrayEnabled
    {
        get => _closeToTrayEnabled;
        set
        {
            if (_closeToTrayEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _closeToTrayEnabled, value);
            if (_isLoadingUiSettings)
            {
                return;
            }

            SaveUiSettings();
        }
    }

    private bool _startMinimizedEnabled;
    public bool StartMinimizedEnabled
    {
        get => _startMinimizedEnabled;
        set
        {
            if (_startMinimizedEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _startMinimizedEnabled, value);
            if (_isLoadingUiSettings)
            {
                return;
            }

            SaveUiSettings();
        }
    }

    private bool _startWithWindowsEnabled;
    public bool StartWithWindowsEnabled
    {
        get => _startWithWindowsEnabled;
        set
        {
            if (_startWithWindowsEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _startWithWindowsEnabled, value);
            if (_isLoadingUiSettings)
            {
                return;
            }

            _windowsStartupRegistration.SetEnabled(value);
            SaveUiSettings();
        }
    }

    public IBrush BaseColorPreviewBrush => new SolidColorBrush(Color.FromRgb((byte)R, (byte)G, (byte)B));

    public IBrush BackgroundColorPreviewBrush => new SolidColorBrush(
        BackgroundColorEnabled
            ? Color.FromRgb((byte)BackgroundR, (byte)BackgroundG, (byte)BackgroundB)
            : Color.FromRgb(18, 18, 18));

    private bool _showsSpeedControl = true;
    public bool ShowsSpeedControl
    {
        get => _showsSpeedControl;
        private set => this.RaiseAndSetIfChanged(ref _showsSpeedControl, value);
    }

    private bool _hasEffectParameters;
    public bool HasEffectParameters
    {
        get => _hasEffectParameters;
        private set => this.RaiseAndSetIfChanged(ref _hasEffectParameters, value);
    }

    private string _profileName = string.Empty;
    public string ProfileName
    {
        get => _profileName;
        set => this.RaiseAndSetIfChanged(ref _profileName, value);
    }

    private string? _selectedProfileName;
    public string? SelectedProfileName
    {
        get => _selectedProfileName;
        set => this.RaiseAndSetIfChanged(ref _selectedProfileName, value);
    }

    public ReactiveCommand<Unit, Unit> ApplyColorCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadProfileCommand { get; }
    public ReactiveCommand<ControllerOption, Unit> OpenControllerDetailsCommand { get; }
    public ReactiveCommand<Unit, Unit> BackToControllerListCommand { get; }
    public ReactiveCommand<Unit, Unit> StopEffectCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCanvasDevicesCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCanvasDeviceSelectionCommand { get; }
    public ReactiveCommand<Views.CanvasDeviceMoveRequest, Unit> MoveCanvasDeviceCommand { get; }

    private void RefreshConnection(bool restoreProfileOnStartup)
    {
        StopActiveEffectCore(updateStatus: false);

        IReadOnlyList<SmyruRgbHidClient.AvailableDeviceInfo> devices = _client.GetAvailableDevices();
        UpdateAvailableControllers(devices);

        if (SelectedController is null)
        {
            ApplyDisconnectedState("No compatible RGB HID controller found. Connect a supported controller and refresh the device list.");
            return;
        }

        if (_client.TryConnect(SelectedController.DeviceIdentifier, out string message))
        {
            _currentDeviceIdentifier = _client.DeviceIdentifier;
            DeviceSummary = _client.DeviceSummary;
            ChannelCount = _client.ChannelCount;
            MaxLedsPerChannel = _client.MaxLedsPerChannel;
            ResetChannelLedSettings(_client.Channels);
            LoadProfilesForCurrentDevice();
            RestoreDeviceDraft();

            bool restored = restoreProfileOnStartup && RestoreLastUsedProfile();
            Status = restored && !string.IsNullOrWhiteSpace(SelectedProfileName)
                ? $"{message} Restored profile '{SelectedProfileName}'."
                : message;
            IsConnected = true;
        }
        else
        {
            UpdateAvailableControllers(_client.GetAvailableDevices());
            ApplyDisconnectedState(message);
        }
    }

    private void OpenControllerDetails(ControllerOption? controller)
    {
        if (controller is null)
        {
            return;
        }

        IsControllerDetailsVisible = true;
        if (!ReferenceEquals(SelectedController, controller))
        {
            SelectedController = controller;
        }
    }

    private void CloseControllerDetails()
    {
        IsControllerDetailsVisible = false;
    }

    private void UpdateAvailableControllers(IReadOnlyList<SmyruRgbHidClient.AvailableDeviceInfo> devices)
    {
        string? preferredIdentifier = SelectedController?.DeviceIdentifier ?? _currentDeviceIdentifier;
        Dictionary<string, ControllerOption> existingControllers = AvailableControllers.ToDictionary(controller => controller.DeviceIdentifier, StringComparer.Ordinal);

        _isUpdatingControllerSelection = true;
        try
        {
            AvailableControllers.Clear();
            foreach (SmyruRgbHidClient.AvailableDeviceInfo device in devices)
            {
                if (existingControllers.TryGetValue(device.DeviceIdentifier, out ControllerOption? existingController))
                {
                    existingController.Update(device);
                    AvailableControllers.Add(existingController);
                }
                else
                {
                    AvailableControllers.Add(new ControllerOption(device));
                }
            }

            RebuildAllDeviceChannelGroups();

            SelectedController = AvailableControllers.FirstOrDefault(controller =>
                                     string.Equals(controller.DeviceIdentifier, preferredIdentifier, StringComparison.Ordinal))
                                 ?? AvailableControllers.FirstOrDefault();
        }
        finally
        {
            _isUpdatingControllerSelection = false;
        }
    }

    private void ApplyDisconnectedState(string message)
    {
        RememberCurrentDeviceDraft();
        _currentDeviceIdentifier = null;
        DeviceSummary = SelectedController?.Summary ?? "No device";
        ChannelCount = 8;
        MaxLedsPerChannel = 126;
        ResetChannelLedSettings(_client.Channels);
        RebuildAllDeviceChannelGroups();
        AvailableProfiles.Clear();
        SelectedProfileName = null;
        ProfileName = "Default";
        SelectedEffectName = EffectCatalog.DefaultEffectName;
        UpdateSelectedEffectState(null);
        Status = message;
        IsConnected = false;
    }

    private void StartSelectedEffect()
    {
        if (!IsConnected)
        {
            Status = "No device is connected.";
            return;
        }

        StopActiveEffectCore(updateStatus: false);

        ILedEffect effect = EffectCatalog.FindByName(SelectedEffectName);
        if (!effect.IsAnimated)
        {
            ApplySingleFrameEffect(effect);
            return;
        }

        StartDynamicEffect(effect);
    }

    private void ApplySingleFrameEffect(ILedEffect effect)
    {
        if (!TryCreateDeviceFrameRequests(effect, 0, out IReadOnlyList<SmyruRgbHidClient.DeviceFrameRequest> deviceRequests, out _, out string errorMessage))
        {
            ReportSendFailure(errorMessage);
            return;
        }

        if (TrySendLedFrames(deviceRequests, out string message))
        {
            IsEffectRunning = false;
            Status = $"Applied effect '{effect.Name}'. {message}";
        }
    }

    private void StartDynamicEffect(ILedEffect effect)
    {
        _effectCancellationTokenSource = new CancellationTokenSource();
        IsEffectRunning = true;
        Status = $"Started effect '{effect.Name}'.";
        _ = RunEffectLoopAsync(effect, _effectCancellationTokenSource.Token);
    }

    private async Task RunEffectLoopAsync(ILedEffect effect, CancellationToken cancellationToken)
    {
        long tick = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryCreateDeviceFrameRequests(effect, tick, out IReadOnlyList<SmyruRgbHidClient.DeviceFrameRequest> deviceRequests, out int delayMilliseconds, out string errorMessage))
                {
                    ReportSendFailure(errorMessage);
                    return;
                }

                if (!TrySendLedFrames(deviceRequests, out _))
                {
                    return;
                }

                tick++;
                await Task.Delay(Math.Max(10, delayMilliseconds), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_effectCancellationTokenSource?.Token == cancellationToken)
                {
                    _effectCancellationTokenSource.Dispose();
                    _effectCancellationTokenSource = null;
                    IsEffectRunning = false;
                    if (IsConnected && string.Equals(SelectedEffectName, effect.Name, StringComparison.Ordinal))
                    {
                        Status = $"Stopped effect '{effect.Name}'.";
                    }
                }
            });
        }
    }

    private void StopActiveEffect()
    {
        StopActiveEffectCore(updateStatus: true);
    }

    private void StopActiveEffectCore(bool updateStatus)
    {
        CancellationTokenSource? cancellationTokenSource = _effectCancellationTokenSource;
        _effectCancellationTokenSource = null;

        if (cancellationTokenSource is null)
        {
            IsEffectRunning = false;
            if (updateStatus)
            {
                Status = "No dynamic effect is running.";
            }

            return;
        }

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
        IsEffectRunning = false;
        if (updateStatus)
        {
            Status = "Stopped active effect.";
        }
    }

    private bool TrySendLedFrames(IReadOnlyList<SmyruRgbHidClient.DeviceFrameRequest> deviceRequests, out string message)
    {
        bool sent = _client.TrySetLedFrames(deviceRequests, out string sendMessage);
        message = sendMessage;

        if (sent)
        {
            return true;
        }

        ReportSendFailure(sendMessage);
        return false;
    }

    private EffectContext CreateEffectContext(long tick)
    {
        return new EffectContext(
            new LedColor((byte)R, (byte)G, (byte)B),
            SupportsBackgroundColor && BackgroundColorEnabled,
            new LedColor((byte)BackgroundR, (byte)BackgroundG, (byte)BackgroundB),
            EffectSpeed,
            ChannelLedSettings.Select(setting => setting.LedCount).ToArray(),
            ChannelLedSettings.Select(setting => CreateEffectLedLocations(setting.PreviewLayout, setting.SelectedDevicePresetOption.KeyboardLayout)).ToArray(),
            ChannelLedSettings.Select(setting => setting.ChannelName).ToArray(),
            ChannelLedSettings.Count,
            tick,
            EffectParameters.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal));
    }

    private EffectContext CreateEffectContext(IReadOnlyList<DeviceChannelEntry> channels, long tick)
    {
        return new EffectContext(
            new LedColor((byte)R, (byte)G, (byte)B),
            SupportsBackgroundColor && BackgroundColorEnabled,
            new LedColor((byte)BackgroundR, (byte)BackgroundG, (byte)BackgroundB),
            EffectSpeed,
            channels.Select(channel => channel.LedCount).ToArray(),
            channels.Select(channel => CreateEffectLedLocations(channel.PreviewLayout, channel.SelectedDevicePresetOption.KeyboardLayout)).ToArray(),
            channels.Select(channel => NormalizeChannelDeviceName(channel.ChannelName, channel.ChannelNumber)).ToArray(),
            channels.Count,
            tick,
            EffectParameters.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal));
    }

    private static IReadOnlyList<EffectLedLocation> CreateEffectLedLocations(ChannelDeviceLayout layout, KeyboardTemplate? keyboardLayout = null)
    {
        if (layout.ShapeKind == DeviceShapeKind.Keyboard && keyboardLayout is not null)
        {
            Dictionary<int, (double X, double Y, double Sequence)> templateLocations = BuildKeyboardTemplateLocations(keyboardLayout);
            return layout.Leds
                .Select(led =>
                {
                    if (templateLocations.TryGetValue(led.LedIndex, out (double X, double Y, double Sequence) mapped))
                    {
                        return new EffectLedLocation(
                            led.LedIndex,
                            mapped.X,
                            mapped.Y,
                            mapped.Sequence,
                            Math.Sqrt(Math.Pow(mapped.X - 0.5, 2) + Math.Pow(mapped.Y - 0.5, 2)),
                            0);
                    }

                    return new EffectLedLocation(
                        led.LedIndex,
                        led.X,
                        led.Y,
                        led.LedIndex,
                        Math.Sqrt(Math.Pow(led.X - 0.5, 2) + Math.Pow(led.Y - 0.5, 2)),
                        0);
                })
                .ToArray();
        }

        return layout.Leds
            .Select(led => new EffectLedLocation(
                led.LedIndex,
                led.X,
                led.Y,
                GetAxisPosition(layout.ShapeKind, led),
                Math.Sqrt(Math.Pow(led.X - 0.5, 2) + Math.Pow(led.Y - 0.5, 2)),
                NormalizeAngle(led.StartAngleDegrees + (led.SweepAngleDegrees / 2.0))))
            .ToArray();
    }

    private static Dictionary<int, (double X, double Y, double Sequence)> BuildKeyboardTemplateLocations(KeyboardTemplate keyboardTemplate)
    {
        var locations = new Dictionary<int, (double X, double Y, double Sequence)>();
        int stripCount = Math.Max(1, keyboardTemplate.Strips.Count);
        int sequenceIndex = 0;

        for (int stripIndex = 0; stripIndex < keyboardTemplate.Strips.Count; stripIndex++)
        {
            KeyboardStrip strip = keyboardTemplate.Strips[stripIndex];
            double totalWidth = Math.Max(0.001, strip.Keys.Sum(key => key.Width));
            double currentX = 0;
            double y = (stripIndex + 0.5) / stripCount;

            foreach (KeyboardKey key in strip.Keys)
            {
                double keyWidth = Math.Max(0.001, key.Width);
                if (key.LedIndex.HasValue)
                {
                    int ledIndex = key.LedIndex.Value - 1;
                    double x = (currentX + (keyWidth / 2.0)) / totalWidth;
                    locations[ledIndex] = (x, y, sequenceIndex);
                    sequenceIndex++;
                }

                currentX += keyWidth;
            }
        }

        return locations;
    }

    private static double GetAxisPosition(DeviceShapeKind shapeKind, ChannelDeviceLedLayout led)
    {
        return shapeKind switch
        {
            DeviceShapeKind.Pizza => NormalizeAngle(led.StartAngleDegrees + (led.SweepAngleDegrees / 2.0)),
            DeviceShapeKind.Keyboard => led.Y,
            _ => led.X
        };
    }

    private static double NormalizeAngle(double angleDegrees)
    {
        double normalized = angleDegrees % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized / 360.0;
    }

    private bool TryCreateDeviceFrameRequests(
        ILedEffect effect,
        long tick,
        out IReadOnlyList<SmyruRgbHidClient.DeviceFrameRequest> deviceRequests,
        out int delayMilliseconds,
        out string message)
    {
        IReadOnlyList<CanvasDeviceRenderState> renderedDevices = CreateCanvasRenderStates(includeSelectedOnly: false, tick, out delayMilliseconds);
        List<SmyruRgbHidClient.DeviceFrameRequest> requests = [];
        message = string.Empty;
        int? roccatDelayOverride = null;

        foreach (IGrouping<string, CanvasDeviceRenderState> controllerGroup in renderedDevices.GroupBy(device => device.ControllerDeviceIdentifier, StringComparer.Ordinal))
        {
            List<CanvasChannelRenderState> channels = controllerGroup
                .SelectMany(device => device.Channels)
                .OrderBy(channel => channel.Channel.ChannelNumber)
                .ToList();

            if (ChannelDevicePresetCatalog.IsRoccatDevice(controllerGroup.Key))
            {
                List<DeviceChannelEntry> rocctChannels = channels.Select(channel => channel.Channel).ToList();
                EffectContext roccatContext = CreateEffectContext(rocctChannels, tick);
                EffectFrame roccatFrame = effect.CreateFrame(roccatContext);
                if (roccatFrame.ChannelLeds.Count != rocctChannels.Count)
                {
                    deviceRequests = [];
                    message = $"Effect '{effect.Name}' returned {roccatFrame.ChannelLeds.Count} channels for Roccat, expected {rocctChannels.Count}.";
                    return false;
                }

                requests.Add(new SmyruRgbHidClient.DeviceFrameRequest(
                    controllerGroup.Key,
                    roccatFrame.ChannelLeds.Select(channel => (IReadOnlyList<LedColor>)channel.ToArray()).ToArray(),
                    rocctChannels.Select(channel => channel.LedCount).ToArray()));

                roccatDelayOverride = roccatDelayOverride.HasValue
                    ? Math.Min(roccatDelayOverride.Value, roccatFrame.DelayMilliseconds)
                    : roccatFrame.DelayMilliseconds;
                continue;
            }

            requests.Add(new SmyruRgbHidClient.DeviceFrameRequest(
                controllerGroup.Key,
                channels.Select(channel => (IReadOnlyList<LedColor>)channel.LedColors).ToArray(),
                channels.Select(channel => channel.Channel.LedCount).ToArray()));
        }

        if (roccatDelayOverride.HasValue)
        {
            delayMilliseconds = Math.Min(delayMilliseconds, roccatDelayOverride.Value);
        }

        if (requests.Count == 0)
        {
            deviceRequests = [];
            message = "No compatible RGB HID controller found. Connect a supported controller and refresh the device list.";
            return false;
        }

        deviceRequests = requests;
        return true;
    }

    private void SetCanvasDeviceSelection(bool isSelected)
    {
        foreach (DeviceChannelGroup group in CanvasDeviceChannelGroups)
        {
            group.IsSelectedForCanvas = isSelected;
        }

        RefreshCanvasPreview();
    }

    private void RefreshCanvasPreview()
    {
        CanvasPreviewScene = CreateCanvasPreviewSnapshot(_canvasPreviewTick);
    }

    public Views.CanvasPreviewSceneSnapshot CreateCanvasPreviewSnapshot(long tick)
    {
        IReadOnlyList<CanvasDeviceRenderState> renderedDevices = CreateCanvasRenderStates(includeSelectedOnly: true, tick, out _);
        IReadOnlyList<Views.CanvasPreviewBackgroundCellSnapshot> background = CreateCanvasBackgroundSnapshot(tick);

        return new Views.CanvasPreviewSceneSnapshot(
            background,
            renderedDevices.Select(device => new Views.CanvasPreviewDeviceSnapshot(
                device.DeviceIdentifier,
                device.DeviceName,
                device.Bounds,
                device.Channels.Select(channel => new Views.CanvasPreviewChannelSnapshot(
                    channel.Channel.ChannelLabel,
                    channel.Channel.ChannelName,
                    GetShapeDisplayName(channel.Channel.DeviceShapeKind),
                    channel.Channel.PreviewLayout,
                    channel.Bounds,
                    channel.LedColors.Select(static color => Color.FromRgb(color.Red, color.Green, color.Blue)).ToArray(),
                    channel.Channel.SelectedDevicePresetOption.KeyboardLayout))
                    .ToArray()))
                .ToArray());
    }

    private IReadOnlyList<Views.CanvasPreviewBackgroundCellSnapshot> CreateCanvasBackgroundSnapshot(long tick)
    {
        List<CanvasBackgroundSampleState> backgroundCells = new(CanvasBackgroundColumns * CanvasBackgroundRows);
        List<Point> samplePoints = new(backgroundCells.Capacity);

        double cellWidth = 1.0 / CanvasBackgroundColumns;
        double cellHeight = 1.0 / CanvasBackgroundRows;

        for (int row = 0; row < CanvasBackgroundRows; row++)
        {
            for (int column = 0; column < CanvasBackgroundColumns; column++)
            {
                Rect bounds = new(column * cellWidth, row * cellHeight, cellWidth + 0.0005, cellHeight + 0.0005);
                Point center = new(bounds.X + (bounds.Width / 2.0), bounds.Y + (bounds.Height / 2.0));
                backgroundCells.Add(new CanvasBackgroundSampleState(bounds, samplePoints.Count));
                samplePoints.Add(center);
            }
        }

        IReadOnlyList<LedColor> colors = SampleCanvasEffectColors(samplePoints, tick, out _);
        return backgroundCells.Select(cell => new Views.CanvasPreviewBackgroundCellSnapshot(
            cell.Bounds,
            ToAvaloniaColor(colors[cell.SampleIndex]))).ToArray();
    }

    private IReadOnlyList<CanvasDeviceRenderState> CreateCanvasRenderStates(bool includeSelectedOnly, long tick, out int delayMilliseconds)
    {
        List<CanvasDeviceLayoutState> devices = CanvasDeviceChannelGroups
            .Where(group => !includeSelectedOnly || group.IsSelectedForCanvas)
            .Select(CreateCanvasDeviceLayoutState)
            .Where(static device => device.Channels.Count > 0)
            .ToList();

        if (devices.Count == 0)
        {
            delayMilliseconds = 33;
            return [];
        }

        List<Point> samplePoints = [];
        Dictionary<(string DeviceIdentifier, int ChannelNumber, int LedIndex), List<int>> ledSamples = new();

        foreach (CanvasDeviceLayoutState device in devices)
        {
            foreach (CanvasChannelLayoutState channel in device.Channels)
            {
                foreach (ChannelDeviceLedLayout led in channel.Channel.PreviewLayout.Leds)
                {
                    List<int> indices = [];
                    foreach (Point samplePoint in CreateLedCaptureSamplePoints(channel.Bounds, led))
                    {
                        indices.Add(samplePoints.Count);
                        samplePoints.Add(samplePoint);
                    }

                    ledSamples[(device.DeviceIdentifier, channel.Channel.ChannelNumber, led.LedIndex)] = indices;
                }
            }
        }

        IReadOnlyList<LedColor> sampledColors = SampleCanvasEffectColors(samplePoints, tick, out delayMilliseconds);
        return devices.Select(device => new CanvasDeviceRenderState(
            device.DeviceIdentifier,
            device.ControllerDeviceIdentifier,
            device.DeviceName,
            device.Bounds,
            device.Channels.Select(channel => new CanvasChannelRenderState(
                channel.Channel,
                channel.Bounds,
                channel.Channel.PreviewLayout.Leds
                    .Select(led => AverageLedColors(
                        sampledColors,
                        ledSamples[(device.DeviceIdentifier, channel.Channel.ChannelNumber, led.LedIndex)]))
                    .ToArray()))
                .ToArray())).ToArray();
    }

    private IReadOnlyList<LedColor> SampleCanvasEffectColors(IReadOnlyList<Point> samplePoints, long tick, out int delayMilliseconds)
    {
        if (samplePoints.Count == 0)
        {
            delayMilliseconds = 33;
            return [];
        }

        EffectContext context = new(
            new LedColor((byte)R, (byte)G, (byte)B),
            SupportsBackgroundColor && BackgroundColorEnabled,
            new LedColor((byte)BackgroundR, (byte)BackgroundG, (byte)BackgroundB),
            EffectSpeed,
            [samplePoints.Count],
            [samplePoints.Select(CreateCanvasEffectLedLocation).ToArray()],
            ["Canvas"],
            1,
            tick,
            EffectParameters.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal));

        EffectFrame frame = EffectCatalog.FindByName(SelectedEffectName).CreateFrame(context);
        delayMilliseconds = frame.DelayMilliseconds;
        return frame.ChannelLeds[0].ToArray();
    }

    private static EffectLedLocation CreateCanvasEffectLedLocation(Point point, int ledIndex)
    {
        double deltaX = point.X - 0.5;
        double deltaY = point.Y - 0.5;
        double angleDegrees = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
        return new EffectLedLocation(
            ledIndex,
            point.X,
            point.Y,
            point.X,
            Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)),
            NormalizeAngle(angleDegrees));
    }

    private static CanvasDeviceLayoutState CreateCanvasDeviceLayoutState(DeviceChannelGroup group)
    {
        IReadOnlyList<DeviceChannelEntry> channels = group.Channels.ToArray();
        double height = GetCanvasDeviceHeight(channels);
        Rect bounds = new(group.CanvasLeft, group.CanvasTop, CanvasDeviceWidth, height);
        double channelWidth = Math.Max(0.08, CanvasDeviceWidth - (CanvasDevicePadding * 2));
        double currentTop = group.CanvasTop + CanvasDevicePadding + CanvasDeviceHeaderHeight;
        List<CanvasChannelLayoutState> channelStates = new(channels.Count);

        foreach (DeviceChannelEntry channel in channels)
        {
            double channelHeight = GetChannelPreviewHeight(channel.DeviceShapeKind);
            Rect channelBounds = new(group.CanvasLeft + CanvasDevicePadding, currentTop, channelWidth, channelHeight);
            channelStates.Add(new CanvasChannelLayoutState(channel, channelBounds));
            currentTop += channelHeight + CanvasChannelGap;
        }

        return new CanvasDeviceLayoutState(group.DeviceIdentifier, group.ControllerDeviceIdentifier, group.DeviceName, bounds, channelStates);
    }

    private static IReadOnlyList<Point> CreateLedCaptureSamplePoints(Rect channelBounds, ChannelDeviceLedLayout led)
    {
        Point center = new(
            channelBounds.X + (channelBounds.Width * led.X),
            channelBounds.Y + (channelBounds.Height * led.Y));
        double radius = Math.Max(0.004, Math.Min(channelBounds.Width, channelBounds.Height) * 0.045);

        return
        [
            ClampToCanvas(center),
            ClampToCanvas(new Point(center.X - radius, center.Y)),
            ClampToCanvas(new Point(center.X + radius, center.Y)),
            ClampToCanvas(new Point(center.X, center.Y - radius)),
            ClampToCanvas(new Point(center.X, center.Y + radius))
        ];
    }

    private static LedColor AverageLedColors(IReadOnlyList<LedColor> source, IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            return LedColor.Black;
        }

        int red = 0;
        int green = 0;
        int blue = 0;
        foreach (int index in indices)
        {
            LedColor color = source[index];
            red += color.Red;
            green += color.Green;
            blue += color.Blue;
        }

        return new LedColor(
            (byte)(red / indices.Count),
            (byte)(green / indices.Count),
            (byte)(blue / indices.Count));
    }

    private static Point ClampToCanvas(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, 1),
            Math.Clamp(point.Y, 0, 1));
    }

    private static double GetCanvasDeviceHeight(IReadOnlyList<DeviceChannelEntry> channels)
    {
        if (channels.Count == 0)
        {
            return CanvasDevicePadding + CanvasDeviceHeaderHeight + CanvasDevicePadding;
        }

        double channelsHeight = channels.Sum(channel => GetChannelPreviewHeight(channel.DeviceShapeKind));
        double gaps = Math.Max(0, channels.Count - 1) * CanvasChannelGap;
        return CanvasDevicePadding + CanvasDeviceHeaderHeight + channelsHeight + gaps + CanvasDevicePadding;
    }

    private static double GetChannelPreviewHeight(DeviceShapeKind shapeKind)
    {
        return shapeKind switch
        {
            DeviceShapeKind.Pizza => CanvasPizzaHeight,
            DeviceShapeKind.Keyboard => CanvasKeyboardHeight,
            _ => CanvasBarHeight
        };
    }

    private static string GetShapeDisplayName(DeviceShapeKind shapeKind)
    {
        return shapeKind switch
        {
            DeviceShapeKind.Pizza => "Pizza",
            DeviceShapeKind.Keyboard => "Klawiatura",
            _ => "Pasek"
        };
    }

    private static Color ToAvaloniaColor(LedColor color)
    {
        return Color.FromRgb(color.Red, color.Green, color.Blue);
    }

    private void ReportSendFailure(string failureMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StopActiveEffectCore(updateStatus: false);
            Status = $"Send error: {failureMessage}";
            IsConnected = false;
        });
    }

    private void ResetChannelLedSettings(IReadOnlyList<HidRgbChannelInfo> channels)
    {
        AppSettingsStore.ChannelSettings[] existingSettings = ChannelLedSettings.Select(static setting => setting.ToChannelSettings()).ToArray();

        ChannelLedSettings.Clear();
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            HidRgbChannelInfo channel = channels[channelIndex];
            AppSettingsStore.ChannelSettings initialSettings = CreateNormalizedChannelSettings(
                channelIndex < existingSettings.Length ? existingSettings[channelIndex] : null,
                channel.ChannelNumber,
                channel.DefaultLedCount,
                channel.MaxLedCount,
                _currentDeviceIdentifier);

            ChannelLedSetting setting = new(channel.ChannelNumber, initialSettings, channel.MaxLedCount, _currentDeviceIdentifier);
            setting.PropertyChanged += OnChannelLedSettingChanged;
            ChannelLedSettings.Add(setting);
        }

        RebuildAllDeviceChannelGroups();
    }

    private void OnChannelLedSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChannelLedSetting.LedCount)
            or nameof(ChannelLedSetting.ChannelName)
            or nameof(ChannelLedSetting.SelectedDevicePresetOption)
            or nameof(ChannelLedSetting.SelectedDeviceShapeOption))
        {
            RebuildAllDeviceChannelGroups();
        }
    }

    private void SyncBaseColorState()
    {
        string normalizedHex = FormatHex(R, G, B);
        if (!string.Equals(_baseColorHex, normalizedHex, StringComparison.Ordinal))
        {
            _baseColorHex = normalizedHex;
            this.RaisePropertyChanged(nameof(BaseColorHex));
        }

        this.RaisePropertyChanged(nameof(BaseColor));
    }

    private void SyncBackgroundColorState()
    {
        string normalizedHex = FormatHex(BackgroundR, BackgroundG, BackgroundB);
        if (!string.Equals(_backgroundColorHex, normalizedHex, StringComparison.Ordinal))
        {
            _backgroundColorHex = normalizedHex;
            this.RaisePropertyChanged(nameof(BackgroundColorHex));
        }

        this.RaisePropertyChanged(nameof(BackgroundColor));
    }

    private static string NormalizeHexInput(string? value)
    {
        return (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
    }

    private static string FormatHex(int red, int green, int blue)
    {
        return string.Create(
            6,
            (red, green, blue),
            static (span, color) =>
            {
                color.red.TryFormat(span[..2], out _, "X2", CultureInfo.InvariantCulture);
                color.green.TryFormat(span.Slice(2, 2), out _, "X2", CultureInfo.InvariantCulture);
                color.blue.TryFormat(span.Slice(4, 2), out _, "X2", CultureInfo.InvariantCulture);
            });
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = Color.FromRgb(0, 0, 0);
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packedColor))
        {
            return false;
        }

        color = Color.FromRgb(
            (byte)((packedColor >> 16) & 0xFF),
            (byte)((packedColor >> 8) & 0xFF),
            (byte)(packedColor & 0xFF));
        return true;
    }

    private void LoadUiSettings()
    {
        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();

        _isLoadingUiSettings = true;
        try
        {
            MinimizeToTrayEnabled = savedSettings.Ui.MinimizeToTrayEnabled;
            CloseToTrayEnabled = savedSettings.Ui.CloseToTrayEnabled;
            StartMinimizedEnabled = savedSettings.Ui.StartMinimizedEnabled;
            StartWithWindowsEnabled = _windowsStartupRegistration.IsEnabled();
        }
        finally
        {
            _isLoadingUiSettings = false;
        }
    }

    private void SaveUiSettings()
    {
        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();
        savedSettings.Ui.MinimizeToTrayEnabled = MinimizeToTrayEnabled;
        savedSettings.Ui.CloseToTrayEnabled = CloseToTrayEnabled;
        savedSettings.Ui.StartMinimizedEnabled = StartMinimizedEnabled;
        savedSettings.Ui.StartWithWindowsEnabled = StartWithWindowsEnabled;
        _settingsStore.Save(savedSettings);
    }

    private void LoadProfilesForCurrentDevice()
    {
        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();
        AppSettingsStore.DeviceSettings? deviceSettings = GetCurrentDeviceSettings(savedSettings);

        AvailableProfiles.Clear();
        foreach (string profileName in deviceSettings?.Profiles.Select(profile => profile.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                     ?? Enumerable.Empty<string>())
        {
            AvailableProfiles.Add(profileName);
        }

        SelectedProfileName = deviceSettings?.LastProfileName;
        ProfileName = deviceSettings?.LastProfileName ?? ProfileName;
    }

    private bool RestoreLastUsedProfile()
    {
        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();
        AppSettingsStore.DeviceSettings? deviceSettings = GetCurrentDeviceSettings(savedSettings);
        if (deviceSettings is null || string.IsNullOrWhiteSpace(deviceSettings.LastProfileName))
        {
            return false;
        }

        AppSettingsStore.ProfileSettings? profile = deviceSettings.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, deviceSettings.LastProfileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            return false;
        }

        ApplyProfile(profile);
        SelectedProfileName = profile.Name;
        ProfileName = profile.Name;
        return true;
    }

    private void SaveCurrentProfile()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_currentDeviceIdentifier))
        {
            Status = "No device is connected.";
            return;
        }

        string profileName = NormalizeProfileName(ProfileName);
        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();
        AppSettingsStore.DeviceSettings deviceSettings = GetOrCreateCurrentDeviceSettings(savedSettings);

        AppSettingsStore.ProfileSettings profile = deviceSettings.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?? new AppSettingsStore.ProfileSettings { Name = profileName };

        profile.Name = profileName;
        profile.EffectName = SelectedEffectName;
        profile.EffectSpeed = EffectSpeed;
        profile.EffectParameters = EffectParameters.Select(parameter => new AppSettingsStore.EffectParameterSetting
        {
            Key = parameter.Key,
            Value = parameter.Value
        }).ToList();
        profile.Red = R;
        profile.Green = G;
        profile.Blue = B;
        profile.BackgroundColorEnabled = BackgroundColorEnabled;
        profile.BackgroundRed = BackgroundR;
        profile.BackgroundGreen = BackgroundG;
        profile.BackgroundBlue = BackgroundB;
        profile.Channels = ChannelLedSettings.Select(static setting => setting.ToChannelSettings()).ToList();

        int existingIndex = deviceSettings.Profiles.FindIndex(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            deviceSettings.Profiles[existingIndex] = profile;
        }
        else
        {
            deviceSettings.Profiles.Add(profile);
        }

        deviceSettings.LastProfileName = profileName;
        PersistGroupedDeviceChannelSettings(savedSettings);
        _settingsStore.Save(savedSettings);
        LoadProfilesForCurrentDevice();
        SelectedProfileName = profileName;
        ProfileName = profileName;
        Status = $"Saved profile '{profileName}' for the connected controller.";
        RebuildAllDeviceChannelGroups();
    }

    private void LoadSelectedProfile()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(_currentDeviceIdentifier))
        {
            Status = "No device is connected.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileName))
        {
            Status = "Choose a saved profile first.";
            return;
        }

        AppSettingsStore.PersistedSettings savedSettings = _settingsStore.Load();
        AppSettingsStore.DeviceSettings? deviceSettings = GetCurrentDeviceSettings(savedSettings);
        AppSettingsStore.ProfileSettings? profile = deviceSettings?.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, SelectedProfileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            Status = $"Profile '{SelectedProfileName}' was not found for this controller.";
            return;
        }

        ApplyProfile(profile);
        deviceSettings!.LastProfileName = profile.Name;
        _settingsStore.Save(savedSettings);
        ProfileName = profile.Name;
        Status = $"Loaded profile '{profile.Name}'.";
        RebuildAllDeviceChannelGroups();
    }

    private void ApplyProfile(AppSettingsStore.ProfileSettings profile)
    {
        SelectedEffectName = profile.EffectName;
        EffectSpeed = profile.EffectSpeed;
        UpdateSelectedEffectState(profile.EffectParameters);
        R = profile.Red;
        G = profile.Green;
        B = profile.Blue;
        BackgroundColorEnabled = profile.BackgroundColorEnabled;
        BackgroundR = profile.BackgroundRed;
        BackgroundG = profile.BackgroundGreen;
        BackgroundB = profile.BackgroundBlue;

        for (int channelIndex = 0; channelIndex < ChannelLedSettings.Count; channelIndex++)
        {
            ChannelLedSetting setting = ChannelLedSettings[channelIndex];
            AppSettingsStore.ChannelSettings? savedChannel = channelIndex < profile.Channels.Count
                ? profile.Channels[channelIndex]
                : null;
            int defaultLedCount = channelIndex < _client.Channels.Count
                ? _client.Channels[channelIndex].DefaultLedCount
                : setting.LedCount;

            AppSettingsStore.ChannelSettings normalizedSettings = CreateNormalizedChannelSettings(
                savedChannel,
                setting.ChannelNumber,
                defaultLedCount,
                setting.MaxLedCount,
                _currentDeviceIdentifier);

            setting.ApplyChannelSettings(normalizedSettings);
        }

        RememberCurrentDeviceDraft();
    }

    private void PersistGroupedDeviceChannelSettings(AppSettingsStore.PersistedSettings savedSettings)
    {
        foreach (ControllerOption controller in AvailableControllers)
        {
            if (string.Equals(controller.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
            {
                continue;
            }

            bool hasDraft = _deviceDrafts.TryGetValue(controller.DeviceIdentifier, out DeviceDraftState? draft);
            bool hasOverrides = _groupedDeviceChannelOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, AppSettingsStore.ChannelSettings>? channelOverrides)
                && channelOverrides.Count > 0;

            if (!hasDraft && !hasOverrides)
            {
                continue;
            }

            string profileName = ResolveGroupedDeviceProfileName(savedSettings, controller.DeviceIdentifier, draft);
            AppSettingsStore.DeviceSettings deviceSettings = GetOrCreateDeviceSettings(savedSettings, controller.DeviceIdentifier);
            AppSettingsStore.ProfileSettings profile = deviceSettings.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase))
                ?? new AppSettingsStore.ProfileSettings { Name = profileName };

            profile.Name = profileName;
            profile.Channels = BuildDeviceChannelEntries(controller).Select(static entry => entry.ToChannelSettings()).ToList();

            int existingIndex = deviceSettings.Profiles.FindIndex(candidate =>
                string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                deviceSettings.Profiles[existingIndex] = profile;
            }
            else
            {
                deviceSettings.Profiles.Add(profile);
            }

            deviceSettings.LastProfileName = profileName;
        }
    }

    private string ResolveGroupedDeviceProfileName(
        AppSettingsStore.PersistedSettings savedSettings,
        string deviceIdentifier,
        DeviceDraftState? draft)
    {
        if (!string.IsNullOrWhiteSpace(draft?.SelectedProfileName))
        {
            return draft.SelectedProfileName!;
        }

        AppSettingsStore.DeviceSettings? deviceSettings = savedSettings.Devices.FirstOrDefault(device =>
            string.Equals(device.DeviceIdentifier, deviceIdentifier, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(deviceSettings?.LastProfileName))
        {
            return deviceSettings.LastProfileName!;
        }

        return NormalizeProfileName(draft?.ProfileName);
    }

    private static string NormalizeProfileName(string? profileName)
    {
        string normalizedName = profileName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedName) ? "Default" : normalizedName;
    }

    private void RememberCurrentDeviceDraft()
    {
        if (string.IsNullOrWhiteSpace(_currentDeviceIdentifier))
        {
            return;
        }

        _deviceDrafts[_currentDeviceIdentifier] = new DeviceDraftState(
            ProfileName,
            SelectedProfileName,
            SelectedEffectName,
            EffectSpeed,
            EffectParameters.Select(parameter => new AppSettingsStore.EffectParameterSetting
            {
                Key = parameter.Key,
                Value = parameter.Value
            }).ToList(),
            R,
            G,
            B,
            BackgroundColorEnabled,
            BackgroundR,
            BackgroundG,
            BackgroundB,
            ChannelLedSettings.Select(static setting => setting.ToChannelSettings()).ToList());

        RebuildAllDeviceChannelGroups();
    }

    private void RestoreDeviceDraft()
    {
        if (string.IsNullOrWhiteSpace(_currentDeviceIdentifier)
            || !_deviceDrafts.TryGetValue(_currentDeviceIdentifier, out DeviceDraftState? draft))
        {
            return;
        }

        ProfileName = draft.ProfileName;
        SelectedProfileName = draft.SelectedProfileName;
        SelectedEffectName = draft.EffectName;
        EffectSpeed = draft.EffectSpeed;
        UpdateSelectedEffectState(draft.EffectParameters);
        R = draft.Red;
        G = draft.Green;
        B = draft.Blue;
        BackgroundColorEnabled = draft.BackgroundColorEnabled;
        BackgroundR = draft.BackgroundRed;
        BackgroundG = draft.BackgroundGreen;
        BackgroundB = draft.BackgroundBlue;

        for (int channelIndex = 0; channelIndex < ChannelLedSettings.Count; channelIndex++)
        {
            AppSettingsStore.ChannelSettings? savedChannel = channelIndex < draft.Channels.Count
                ? draft.Channels[channelIndex]
                : null;

            if (savedChannel is null)
            {
                continue;
            }

            int defaultLedCount = channelIndex < _client.Channels.Count
                ? _client.Channels[channelIndex].DefaultLedCount
                : ChannelLedSettings[channelIndex].LedCount;

            ChannelLedSettings[channelIndex].ApplyChannelSettings(CreateNormalizedChannelSettings(
                savedChannel,
                ChannelLedSettings[channelIndex].ChannelNumber,
                defaultLedCount,
                ChannelLedSettings[channelIndex].MaxLedCount,
                _currentDeviceIdentifier));
        }

        RebuildAllDeviceChannelGroups();
    }

    private void RebuildAllDeviceChannelGroups()
    {
        Dictionary<string, bool> selectionByDevice = CanvasDeviceChannelGroups.ToDictionary(
            group => group.DeviceIdentifier,
            group => group.IsSelectedForCanvas,
            StringComparer.Ordinal);
        Dictionary<string, Point> positionByDevice = CanvasDeviceChannelGroups.ToDictionary(
            group => group.DeviceIdentifier,
            group => new Point(group.CanvasLeft, group.CanvasTop),
            StringComparer.Ordinal);

        AllDeviceChannelGroups.Clear();
        CanvasDeviceChannelGroups.Clear();

        int deviceIndex = 0;
        for (int controllerIndex = 0; controllerIndex < AvailableControllers.Count; controllerIndex++)
        {
            ControllerOption controller = AvailableControllers[controllerIndex];
            IReadOnlyList<DeviceChannelEntry> channels = BuildDeviceChannelEntries(controller);
            AllDeviceChannelGroups.Add(new DeviceChannelGroup(
                controller.DeviceIdentifier,
                controller.DeviceIdentifier,
                controller.ShortDisplayName,
                controller.ShortDisplayName,
                channels));

            foreach (DeviceChannelEntry channel in channels)
            {
                string groupIdentifier = GetCanvasGroupIdentifier(controller.DeviceIdentifier, channel.ChannelNumber);
                bool isSelected = !selectionByDevice.TryGetValue(groupIdentifier, out bool previousSelection) || previousSelection;
                Point position = positionByDevice.TryGetValue(groupIdentifier, out Point previousPosition)
                    ? previousPosition
                    : GetDefaultCanvasPosition(deviceIndex);
                CanvasDeviceChannelGroups.Add(new DeviceChannelGroup(
                    groupIdentifier,
                    controller.DeviceIdentifier,
                    channel.ChannelName,
                    controller.ShortDisplayName,
                    [channel],
                    isSelected,
                    position.X,
                    position.Y));
                deviceIndex++;
            }
        }

        RefreshCanvasPreview();
    }

    private void MoveCanvasDevice(Views.CanvasDeviceMoveRequest request)
    {
        DeviceChannelGroup? device = CanvasDeviceChannelGroups.FirstOrDefault(group =>
            string.Equals(group.DeviceIdentifier, request.DeviceIdentifier, StringComparison.Ordinal));
        if (device is null)
        {
            return;
        }

        double maxLeft = Math.Max(0, 1 - CanvasDeviceWidth);
        double maxTop = Math.Max(0, 1 - GetCanvasDeviceHeight(device.Channels.ToArray()));
        device.CanvasLeft = Math.Clamp(request.Left, 0, maxLeft);
        device.CanvasTop = Math.Clamp(request.Top, 0, maxTop);
        RefreshCanvasPreview();
    }

    private static Point GetDefaultCanvasPosition(int index)
    {
        int columns = 3;
        int column = index % columns;
        int row = index / columns;
        double left = 0.04 + (column * 0.31);
        double top = 0.05 + (row * 0.34);
        return new Point(Math.Min(left, 0.68), Math.Min(top, 0.82));
    }

    private static string GetCanvasGroupIdentifier(string controllerIdentifier, int channelNumber)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{controllerIdentifier}::channel-{channelNumber}");
    }

    private IReadOnlyList<DeviceChannelEntry> BuildDeviceChannelEntries(ControllerOption controller)
    {
        if (!string.IsNullOrWhiteSpace(_currentDeviceIdentifier)
            && string.Equals(controller.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
        {
            return ChannelLedSettings.Select(setting => new DeviceChannelEntry(
                controller.DeviceIdentifier,
                setting.ChannelNumber,
                $"Channel {setting.ChannelNumber}",
                setting.ToChannelSettings(),
                setting.MaxLedCount,
                UpdateGroupedDeviceChannel)).ToArray();
        }

        IReadOnlyList<AppSettingsStore.ChannelSettings>? savedChannels = null;
        if (_deviceDrafts.TryGetValue(controller.DeviceIdentifier, out DeviceDraftState? draft))
        {
            savedChannels = draft.Channels;
        }

        Dictionary<int, AppSettingsStore.ChannelSettings>? overrides = _groupedDeviceChannelOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, AppSettingsStore.ChannelSettings>? values)
            ? values
            : null;

        return controller.Channels.Select((channel, index) =>
        {
            AppSettingsStore.ChannelSettings? baseSettings = savedChannels is not null && index < savedChannels.Count
                ? savedChannels[index]
                : null;

            if (overrides is not null && overrides.TryGetValue(channel.ChannelNumber, out AppSettingsStore.ChannelSettings? overriddenSettings))
            {
                baseSettings = overriddenSettings;
            }

            AppSettingsStore.ChannelSettings normalizedSettings = CreateNormalizedChannelSettings(
                baseSettings,
                channel.ChannelNumber,
                channel.DefaultLedCount,
                channel.MaxLedCount,
                controller.DeviceIdentifier);

            return new DeviceChannelEntry(
                controller.DeviceIdentifier,
                channel.ChannelNumber,
                $"Channel {channel.ChannelNumber}",
                normalizedSettings,
                channel.MaxLedCount,
                UpdateGroupedDeviceChannel);
        }).ToArray();
    }

    private void UpdateGroupedDeviceChannel(DeviceChannelEntry entry)
    {
        AppSettingsStore.ChannelSettings normalizedSettings = CreateNormalizedChannelSettings(
            entry.ToChannelSettings(),
            entry.ChannelNumber,
            entry.LedCount,
            entry.MaxLedCount,
            entry.DeviceIdentifier);
        entry.ApplyChannelSettings(normalizedSettings);

        if (!string.IsNullOrWhiteSpace(_currentDeviceIdentifier)
            && string.Equals(entry.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
        {
            ChannelLedSetting? channelSetting = ChannelLedSettings.FirstOrDefault(setting => setting.ChannelNumber == entry.ChannelNumber);
            if (channelSetting is not null)
            {
                channelSetting.ApplyChannelSettings(normalizedSettings);
            }

            return;
        }

        if (!_groupedDeviceChannelOverrides.TryGetValue(entry.DeviceIdentifier, out Dictionary<int, AppSettingsStore.ChannelSettings>? overrides))
        {
            overrides = new Dictionary<int, AppSettingsStore.ChannelSettings>();
            _groupedDeviceChannelOverrides[entry.DeviceIdentifier] = overrides;
        }

        overrides[entry.ChannelNumber] = CloneChannelSettings(normalizedSettings);
    }

    private static string NormalizeChannelDeviceName(string? deviceName, int channelNumber)
    {
        string normalizedName = deviceName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedName) ? GetDefaultChannelDeviceName(channelNumber) : normalizedName;
    }

    private static string GetDefaultChannelDeviceName(int channelNumber) => $"Device {channelNumber}";

    private static AppSettingsStore.ChannelSettings CreateNormalizedChannelSettings(
        AppSettingsStore.ChannelSettings? source,
        int channelNumber,
        int defaultLedCount,
        int maxLedCount,
        string? deviceIdentifier)
    {
        ChannelDevicePresetOption preset = ChannelDevicePresetCatalog.NormalizeForDevice(source?.DevicePresetId, deviceIdentifier);
        ChannelDeviceShapeOption shape = ChannelDeviceShapeCatalog.FindById(
            string.IsNullOrWhiteSpace(source?.DeviceShapeId)
                ? preset.DefaultShapeId
                : source!.DeviceShapeId);
        bool isRoccatDevice = ChannelDevicePresetCatalog.IsRoccatDevice(deviceIdentifier);
        int ledCount = preset.IsCustom
            ? Math.Clamp(source?.LedCount ?? defaultLedCount, 1, maxLedCount)
            : isRoccatDevice
                ? Math.Clamp(maxLedCount, 1, maxLedCount)
                : Math.Clamp(preset.DefaultLedCount, 1, maxLedCount);

        string channelName = preset.IsCustom
            ? NormalizeChannelDeviceName(source?.Name, channelNumber)
            : preset.DefaultName;

        return new AppSettingsStore.ChannelSettings
        {
            DevicePresetId = preset.Id,
            DeviceShapeId = shape.Id,
            Name = channelName,
            LedCount = ledCount,
            LedAddresses = NormalizeLedAddresses(source?.LedAddresses, ledCount)
        };
    }

    private static AppSettingsStore.ChannelSettings CloneChannelSettings(AppSettingsStore.ChannelSettings source)
    {
        return new AppSettingsStore.ChannelSettings
        {
            DevicePresetId = source.DevicePresetId,
            DeviceShapeId = source.DeviceShapeId,
            Name = source.Name,
            LedCount = source.LedCount,
            LedAddresses = [.. source.LedAddresses]
        };
    }

    private static List<int> NormalizeLedAddresses(IReadOnlyList<int>? addresses, int ledCount)
    {
        List<int> normalizedAddresses = new(ledCount);
        for (int index = 0; index < ledCount; index++)
        {
            int defaultAddress = index;
            int address = addresses is not null && index < addresses.Count
                ? Math.Max(0, addresses[index])
                : defaultAddress;
            normalizedAddresses.Add(address);
        }

        return normalizedAddresses;
    }

    private void UpdateSelectedEffectState(IReadOnlyList<AppSettingsStore.EffectParameterSetting>? savedParameters)
    {
        ILedEffect effect = EffectCatalog.FindByName(SelectedEffectName);
        SelectedEffectDescription = effect.Description;
        ShowsBaseColorControls = effect.UsesBaseColor;
        SupportsBackgroundColor = effect.SupportsBackgroundColor;
        ShowsSpeedControl = effect.UsesSpeed;

        Dictionary<string, int> savedValues = savedParameters?.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal)
            ?? [];

        EffectParameters.Clear();
        EffectParameterGroups.Clear();
        foreach (EffectParameterDefinition definition in effect.Parameters)
        {
            int value = savedValues.TryGetValue(definition.Key, out int savedValue) ? savedValue : definition.DefaultValue;
            EffectParameters.Add(new EffectParameterSetting(definition, value));
        }

        foreach (IGrouping<string, EffectParameterSetting> group in EffectParameters.GroupBy(parameter => parameter.Group, StringComparer.Ordinal))
        {
            EffectParameterGroups.Add(new EffectParameterGroup(group.Key, group));
        }

        HasEffectParameters = EffectParameters.Count > 0;
    }

    private AppSettingsStore.DeviceSettings? GetCurrentDeviceSettings(AppSettingsStore.PersistedSettings savedSettings)
    {
        if (string.IsNullOrWhiteSpace(_currentDeviceIdentifier))
        {
            return null;
        }

        return savedSettings.Devices.FirstOrDefault(device =>
            string.Equals(device.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal));
    }

    private AppSettingsStore.DeviceSettings GetOrCreateCurrentDeviceSettings(AppSettingsStore.PersistedSettings savedSettings)
    {
        AppSettingsStore.DeviceSettings? existingSettings = GetCurrentDeviceSettings(savedSettings);
        if (existingSettings is not null)
        {
            return existingSettings;
        }

        return GetOrCreateDeviceSettings(savedSettings, _currentDeviceIdentifier!);
    }

    private static AppSettingsStore.DeviceSettings GetOrCreateDeviceSettings(AppSettingsStore.PersistedSettings savedSettings, string deviceIdentifier)
    {
        AppSettingsStore.DeviceSettings? existingSettings = savedSettings.Devices.FirstOrDefault(device =>
            string.Equals(device.DeviceIdentifier, deviceIdentifier, StringComparison.Ordinal));
        if (existingSettings is not null)
        {
            return existingSettings;
        }

        var deviceSettings = new AppSettingsStore.DeviceSettings
        {
            DeviceIdentifier = deviceIdentifier
        };

        savedSettings.Devices.Add(deviceSettings);
        return deviceSettings;
    }

    public sealed class ChannelLedSetting : ReactiveObject
    {
        private readonly string? _deviceIdentifier;
        private ChannelDevicePresetOption _selectedDevicePresetOption;
        private ChannelDeviceShapeOption _selectedDeviceShapeOption;
        private string _channelName;
        private int _ledCount;
        private string _ledCountText;
        private int _maxLedCount;

        internal ChannelLedSetting(int channelNumber, AppSettingsStore.ChannelSettings settings, int maxLedCount, string? deviceIdentifier)
        {
            ChannelNumber = channelNumber;
            _deviceIdentifier = deviceIdentifier;
            _maxLedCount = Math.Max(1, maxLedCount);
            _selectedDevicePresetOption = ChannelDevicePresetCatalog.NormalizeForDevice(settings.DevicePresetId, _deviceIdentifier);
            _selectedDeviceShapeOption = ChannelDeviceShapeCatalog.FindById(settings.DeviceShapeId);
            _channelName = NormalizeChannelDeviceName(settings.Name, channelNumber);
            _ledCount = Math.Clamp(settings.LedCount, 1, _maxLedCount);
            _ledCountText = _ledCount.ToString(CultureInfo.InvariantCulture);
            LedAddressSettings = [];
            ApplyChannelSettings(settings);
        }

        public int ChannelNumber { get; }

        public IReadOnlyList<ChannelDevicePresetOption> AvailableDevicePresetOptions => ChannelDevicePresetCatalog.GetAvailableForDevice(_deviceIdentifier);

        public bool IsRoccatDevice => ChannelDevicePresetCatalog.IsRoccatDevice(_deviceIdentifier);

        public IReadOnlyList<ChannelDeviceShapeOption> AvailableDeviceShapeOptions => ChannelDeviceShapeCatalog.All;

        public ObservableCollection<LedAddressSetting> LedAddressSettings { get; }

        public ChannelDevicePresetOption SelectedDevicePresetOption
        {
            get => _selectedDevicePresetOption;
            set
            {
                ChannelDevicePresetOption preset = ChannelDevicePresetCatalog.NormalizeForDevice(value?.Id, _deviceIdentifier);
                if (ReferenceEquals(_selectedDevicePresetOption, preset))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedDevicePresetOption, preset);
                SelectedDeviceShapeOption = preset.DefaultShape;
                if (!preset.IsCustom)
                {
                    _channelName = preset.DefaultName;
                    _ledCount = Math.Clamp(preset.DefaultLedCount, 1, MaxLedCount);
                    _ledCountText = _ledCount.ToString(CultureInfo.InvariantCulture);
                    this.RaisePropertyChanged(nameof(ChannelName));
                    this.RaisePropertyChanged(nameof(LedCount));
                    this.RaisePropertyChanged(nameof(LedCountText));
                    ResetLedAddresses(_ledCount);
                }

                this.RaisePropertyChanged(nameof(IsCustomDevicePreset));
            }
        }

        public ChannelDeviceShapeOption SelectedDeviceShapeOption
        {
            get => _selectedDeviceShapeOption;
            set
            {
                ChannelDeviceShapeOption shape = value ?? ChannelDeviceShapeCatalog.FindById(null);
                if (ReferenceEquals(_selectedDeviceShapeOption, shape))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedDeviceShapeOption, shape);
                this.RaisePropertyChanged(nameof(DeviceShapeKind));
                this.RaisePropertyChanged(nameof(PreviewLayout));
            }
        }

        public bool IsCustomDevicePreset => SelectedDevicePresetOption.IsCustom;

        public DeviceShapeKind DeviceShapeKind => SelectedDeviceShapeOption.ShapeKind;

        public ChannelDeviceLayout PreviewLayout => ChannelDeviceLayoutFactory.Create(DeviceShapeKind, LedCount);

        public string ChannelName
        {
            get => _channelName;
            set => this.RaiseAndSetIfChanged(ref _channelName, NormalizeChannelDeviceName(value, ChannelNumber));
        }

        public int LedCount
        {
            get => _ledCount;
            set
            {
                int normalizedValue = Math.Clamp(value, 1, MaxLedCount);
                if (_ledCount == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _ledCount, normalizedValue);
                string normalizedText = normalizedValue.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(_ledCountText, normalizedText, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _ledCountText, normalizedText);
                }

                ResizeLedAddresses(normalizedValue);
                this.RaisePropertyChanged(nameof(PreviewLayout));
            }
        }

        public string LedCountText
        {
            get => _ledCountText;
            set
            {
                string normalizedText = value ?? string.Empty;
                if (_ledCountText == normalizedText)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _ledCountText, normalizedText);

                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    return;
                }

                if (!int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    return;
                }

                LedCount = parsedValue;
            }
        }

        public int MaxLedCount
        {
            get => _maxLedCount;
            set
            {
                int normalizedValue = Math.Max(1, value);
                this.RaiseAndSetIfChanged(ref _maxLedCount, normalizedValue);
                LedCount = Math.Clamp(LedCount, 1, normalizedValue);
            }
        }

        internal void ApplyChannelSettings(AppSettingsStore.ChannelSettings settings)
        {
            ChannelDevicePresetOption preset = ChannelDevicePresetCatalog.NormalizeForDevice(settings.DevicePresetId, _deviceIdentifier);
            ChannelDeviceShapeOption shape = ChannelDeviceShapeCatalog.FindById(settings.DeviceShapeId);
            this.RaiseAndSetIfChanged(ref _selectedDevicePresetOption, preset);
            this.RaiseAndSetIfChanged(ref _selectedDeviceShapeOption, shape);
            this.RaisePropertyChanged(nameof(IsCustomDevicePreset));
            this.RaisePropertyChanged(nameof(DeviceShapeKind));
            this.RaiseAndSetIfChanged(ref _channelName, NormalizeChannelDeviceName(settings.Name, ChannelNumber));
            int normalizedLedCount = Math.Clamp(settings.LedCount, 1, MaxLedCount);
            this.RaiseAndSetIfChanged(ref _ledCount, normalizedLedCount);
            string normalizedText = normalizedLedCount.ToString(CultureInfo.InvariantCulture);
            this.RaiseAndSetIfChanged(ref _ledCountText, normalizedText);
            ResetLedAddresses(normalizedLedCount, settings.LedAddresses);
            this.RaisePropertyChanged(nameof(PreviewLayout));
        }

        internal AppSettingsStore.ChannelSettings ToChannelSettings()
        {
            return new AppSettingsStore.ChannelSettings
            {
                DevicePresetId = SelectedDevicePresetOption.Id,
                DeviceShapeId = SelectedDeviceShapeOption.Id,
                Name = NormalizeChannelDeviceName(ChannelName, ChannelNumber),
                LedCount = LedCount,
                LedAddresses = LedAddressSettings.Select(static address => address.Address).ToList()
            };
        }

        private void ResetLedAddresses(int ledCount, IReadOnlyList<int>? sourceAddresses = null)
        {
            LedAddressSettings.Clear();
            for (int index = 0; index < ledCount; index++)
            {
                int address = sourceAddresses is not null && index < sourceAddresses.Count
                    ? Math.Max(0, sourceAddresses[index])
                    : index;
                LedAddressSettings.Add(new LedAddressSetting(index, address));
            }
        }

        private void ResizeLedAddresses(int ledCount)
        {
            List<int> existingAddresses = LedAddressSettings.Select(static address => address.Address).ToList();
            ResetLedAddresses(ledCount, existingAddresses);
        }
    }

    public sealed class LedAddressSetting : ReactiveObject
    {
        private int _address;
        private string _addressText;

        public LedAddressSetting(int ledIndex, int address)
        {
            LedIndex = ledIndex;
            _address = Math.Max(0, address);
            _addressText = _address.ToString(CultureInfo.InvariantCulture);
        }

        public int LedIndex { get; }

        public int Address
        {
            get => _address;
            set
            {
                int normalizedValue = Math.Max(0, value);
                if (_address == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _address, normalizedValue);
                string normalizedText = normalizedValue.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(_addressText, normalizedText, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _addressText, normalizedText);
                }
            }
        }

        public string AddressText
        {
            get => _addressText;
            set
            {
                string normalizedText = value ?? string.Empty;
                if (_addressText == normalizedText)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _addressText, normalizedText);

                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    return;
                }

                if (!int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    return;
                }

                Address = parsedValue;
            }
        }
    }

    public sealed class EffectParameterSetting : ReactiveObject
    {
        private int _value;

        internal EffectParameterSetting(EffectParameterDefinition definition, int value)
        {
            Key = definition.Key;
            Label = definition.Label;
            Description = definition.Description;
            Group = definition.Group;
            Minimum = definition.Minimum;
            Maximum = definition.Maximum;
            _value = Math.Clamp(value, Minimum, Maximum);
        }

        public string Key { get; }

        public string Label { get; }

        public string Description { get; }

        public string Group { get; }

        public int Minimum { get; }

        public int Maximum { get; }

        public int Value
        {
            get => _value;
            set => this.RaiseAndSetIfChanged(ref _value, Math.Clamp(value, Minimum, Maximum));
        }
    }

    public sealed class EffectParameterGroup
    {
        public EffectParameterGroup(string title, IEnumerable<EffectParameterSetting> parameters)
        {
            Title = title;
            Parameters = new ObservableCollection<EffectParameterSetting>(parameters);
        }

        public string Title { get; }

        public ObservableCollection<EffectParameterSetting> Parameters { get; }
    }

    public sealed class ControllerOption
    {
        internal ControllerOption(SmyruRgbHidClient.AvailableDeviceInfo device)
        {
            DeviceIdentifier = device.DeviceIdentifier;
            Update(device);
        }

        public string DeviceIdentifier { get; }

        public string DisplayName { get; private set; } = string.Empty;

        public string ShortDisplayName { get; private set; } = string.Empty;

        public string Summary { get; private set; } = string.Empty;

        public int ChannelCount { get; private set; }

        public int MaxLedsPerChannel { get; private set; }

        internal IReadOnlyList<HidRgbChannelInfo> Channels { get; private set; } = [];

        internal void Update(SmyruRgbHidClient.AvailableDeviceInfo device)
        {
            DisplayName = device.DisplayName;
            ShortDisplayName = StripSuffix(DisplayName);
            Summary = device.Summary;
            ChannelCount = device.ChannelCount;
            MaxLedsPerChannel = device.MaxLedsPerChannel;
            Channels = device.Channels;
        }

        private static string StripSuffix(string displayName)
        {
            int suffixStart = displayName.LastIndexOf(" (", StringComparison.Ordinal);
            return suffixStart > 0 ? displayName[..suffixStart] : displayName;
        }
    }

    public sealed class DeviceChannelGroup : ReactiveObject
    {
        private bool _isSelectedForCanvas;
        private double _canvasLeft;
        private double _canvasTop;

        public DeviceChannelGroup(
            string deviceIdentifier,
            string controllerDeviceIdentifier,
            string deviceName,
            string sourceName,
            IReadOnlyList<DeviceChannelEntry> channels,
            bool isSelectedForCanvas = true,
            double canvasLeft = 0.05,
            double canvasTop = 0.05)
        {
            DeviceIdentifier = deviceIdentifier;
            ControllerDeviceIdentifier = controllerDeviceIdentifier;
            DeviceName = deviceName;
            SourceName = sourceName;
            Channels = new ObservableCollection<DeviceChannelEntry>(channels);
            _isSelectedForCanvas = isSelectedForCanvas;
            _canvasLeft = canvasLeft;
            _canvasTop = canvasTop;
        }

        internal string DeviceIdentifier { get; }

        internal string ControllerDeviceIdentifier { get; }

        public string DeviceName { get; }

        public string SourceName { get; }

        public ObservableCollection<DeviceChannelEntry> Channels { get; }

        public bool IsSelectedForCanvas
        {
            get => _isSelectedForCanvas;
            set => this.RaiseAndSetIfChanged(ref _isSelectedForCanvas, value);
        }

        public double CanvasLeft
        {
            get => _canvasLeft;
            set => this.RaiseAndSetIfChanged(ref _canvasLeft, value);
        }

        public double CanvasTop
        {
            get => _canvasTop;
            set => this.RaiseAndSetIfChanged(ref _canvasTop, value);
        }

        public int ChannelCount => Channels.Count;

        public int TotalLedCount => Channels.Sum(channel => channel.LedCount);

        public string Summary => ChannelCount == 1
            ? $"{SourceName}, {Channels[0].ChannelLabel}, {TotalLedCount} LEDs"
            : $"{SourceName}, {ChannelCount} channels, {TotalLedCount} LEDs";
    }

    public sealed class DeviceChannelEntry : ReactiveObject
    {
        private readonly Action<DeviceChannelEntry> _onChannelSettingsChanged;
        private readonly string? _deviceIdentifier;
        private ChannelDevicePresetOption _selectedDevicePresetOption;
        private ChannelDeviceShapeOption _selectedDeviceShapeOption;
        private string _channelName;
        private int _ledCount;
        private string _ledCountText;

        internal DeviceChannelEntry(
            string deviceIdentifier,
            int channelNumber,
            string channelLabel,
            AppSettingsStore.ChannelSettings settings,
            int maxLedCount,
            Action<DeviceChannelEntry> onChannelSettingsChanged)
        {
            DeviceIdentifier = deviceIdentifier;
            _deviceIdentifier = deviceIdentifier;
            ChannelNumber = channelNumber;
            ChannelLabel = channelLabel;
            MaxLedCount = Math.Max(1, maxLedCount);
            _selectedDevicePresetOption = ChannelDevicePresetCatalog.NormalizeForDevice(settings.DevicePresetId, _deviceIdentifier);
            _selectedDeviceShapeOption = ChannelDeviceShapeCatalog.FindById(settings.DeviceShapeId);
            _channelName = NormalizeChannelDeviceName(settings.Name, channelNumber);
            _ledCount = Math.Clamp(settings.LedCount, 1, MaxLedCount);
            _ledCountText = _ledCount.ToString(CultureInfo.InvariantCulture);
            LedAddressSettings = [];
            _onChannelSettingsChanged = onChannelSettingsChanged;
            ApplyChannelSettings(settings);
        }

        internal string DeviceIdentifier { get; }

        internal int ChannelNumber { get; }

        public IReadOnlyList<ChannelDevicePresetOption> AvailableDevicePresetOptions => ChannelDevicePresetCatalog.GetAvailableForDevice(_deviceIdentifier);

        public bool IsRoccatDevice => ChannelDevicePresetCatalog.IsRoccatDevice(_deviceIdentifier);

        public IReadOnlyList<ChannelDeviceShapeOption> AvailableDeviceShapeOptions => ChannelDeviceShapeCatalog.All;

        public ObservableCollection<LedAddressSetting> LedAddressSettings { get; }

        public ChannelDevicePresetOption SelectedDevicePresetOption
        {
            get => _selectedDevicePresetOption;
            set
            {
                ChannelDevicePresetOption preset = ChannelDevicePresetCatalog.NormalizeForDevice(value?.Id, _deviceIdentifier);
                if (ReferenceEquals(_selectedDevicePresetOption, preset))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedDevicePresetOption, preset);
                SelectedDeviceShapeOption = preset.DefaultShape;
                if (!preset.IsCustom)
                {
                    this.RaiseAndSetIfChanged(ref _channelName, preset.DefaultName);
                    int presetLedCount = Math.Clamp(preset.DefaultLedCount, 1, MaxLedCount);
                    this.RaiseAndSetIfChanged(ref _ledCount, presetLedCount);
                    this.RaiseAndSetIfChanged(ref _ledCountText, presetLedCount.ToString(CultureInfo.InvariantCulture));
                    ResetLedAddresses(presetLedCount);
                    this.RaisePropertyChanged(nameof(ChannelName));
                    this.RaisePropertyChanged(nameof(LedCount));
                    this.RaisePropertyChanged(nameof(LedCountText));
                }

                this.RaisePropertyChanged(nameof(IsCustomDevicePreset));
                _onChannelSettingsChanged(this);
            }
        }

        public ChannelDeviceShapeOption SelectedDeviceShapeOption
        {
            get => _selectedDeviceShapeOption;
            set
            {
                ChannelDeviceShapeOption shape = value ?? ChannelDeviceShapeCatalog.FindById(null);
                if (ReferenceEquals(_selectedDeviceShapeOption, shape))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedDeviceShapeOption, shape);
                this.RaisePropertyChanged(nameof(DeviceShapeKind));
                this.RaisePropertyChanged(nameof(PreviewLayout));
                _onChannelSettingsChanged(this);
            }
        }

        public bool IsCustomDevicePreset => SelectedDevicePresetOption.IsCustom;

        public DeviceShapeKind DeviceShapeKind => SelectedDeviceShapeOption.ShapeKind;

        public ChannelDeviceLayout PreviewLayout => ChannelDeviceLayoutFactory.Create(DeviceShapeKind, LedCount);

        public string ChannelName
        {
            get => _channelName;
            set
            {
                string normalizedValue = value ?? string.Empty;
                if (_channelName == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _channelName, normalizedValue);
                _onChannelSettingsChanged(this);
            }
        }

        public string ChannelLabel { get; }

        public int LedCount
        {
            get => _ledCount;
            set
            {
                int normalizedValue = Math.Clamp(value, 1, MaxLedCount);
                if (_ledCount == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _ledCount, normalizedValue);
                string normalizedText = normalizedValue.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(_ledCountText, normalizedText, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _ledCountText, normalizedText);
                }

                ResizeLedAddresses(normalizedValue);
                this.RaisePropertyChanged(nameof(PreviewLayout));
                _onChannelSettingsChanged(this);
            }
        }

        public string LedCountText
        {
            get => _ledCountText;
            set
            {
                string normalizedText = value ?? string.Empty;
                if (_ledCountText == normalizedText)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _ledCountText, normalizedText);

                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    return;
                }

                if (!int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    return;
                }

                int clampedValue = Math.Clamp(parsedValue, 1, MaxLedCount);
                if (_ledCount != clampedValue)
                {
                    LedCount = clampedValue;
                    return;
                }

                string clampedText = clampedValue.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(_ledCountText, clampedText, StringComparison.Ordinal))
                {
                    this.RaiseAndSetIfChanged(ref _ledCountText, clampedText);
                }
            }
        }

        public int MaxLedCount { get; }

        internal void ApplyChannelSettings(AppSettingsStore.ChannelSettings settings)
        {
            ChannelDevicePresetOption preset = ChannelDevicePresetCatalog.NormalizeForDevice(settings.DevicePresetId, _deviceIdentifier);
            ChannelDeviceShapeOption shape = ChannelDeviceShapeCatalog.FindById(settings.DeviceShapeId);
            this.RaiseAndSetIfChanged(ref _selectedDevicePresetOption, preset);
            this.RaiseAndSetIfChanged(ref _selectedDeviceShapeOption, shape);
            this.RaisePropertyChanged(nameof(IsCustomDevicePreset));
            this.RaisePropertyChanged(nameof(DeviceShapeKind));
            this.RaiseAndSetIfChanged(ref _channelName, NormalizeChannelDeviceName(settings.Name, ChannelNumber));
            int normalizedLedCount = Math.Clamp(settings.LedCount, 1, MaxLedCount);
            this.RaiseAndSetIfChanged(ref _ledCount, normalizedLedCount);
            this.RaiseAndSetIfChanged(ref _ledCountText, normalizedLedCount.ToString(CultureInfo.InvariantCulture));
            ResetLedAddresses(normalizedLedCount, settings.LedAddresses);
            this.RaisePropertyChanged(nameof(PreviewLayout));
        }

        internal AppSettingsStore.ChannelSettings ToChannelSettings()
        {
            return new AppSettingsStore.ChannelSettings
            {
                DevicePresetId = SelectedDevicePresetOption.Id,
                DeviceShapeId = SelectedDeviceShapeOption.Id,
                Name = NormalizeChannelDeviceName(ChannelName, ChannelNumber),
                LedCount = LedCount,
                LedAddresses = LedAddressSettings.Select(static address => address.Address).ToList()
            };
        }

        private void ResetLedAddresses(int ledCount, IReadOnlyList<int>? sourceAddresses = null)
        {
            LedAddressSettings.Clear();
            for (int index = 0; index < ledCount; index++)
            {
                int address = sourceAddresses is not null && index < sourceAddresses.Count
                    ? Math.Max(0, sourceAddresses[index])
                    : index;
                LedAddressSettings.Add(new LedAddressSetting(index, address));
            }
        }

        private void ResizeLedAddresses(int ledCount)
        {
            List<int> existingAddresses = LedAddressSettings.Select(static address => address.Address).ToList();
            ResetLedAddresses(ledCount, existingAddresses);
        }
    }

    private sealed record CanvasBackgroundSampleState(Rect Bounds, int SampleIndex);

    private sealed record CanvasDeviceLayoutState(
        string DeviceIdentifier,
        string ControllerDeviceIdentifier,
        string DeviceName,
        Rect Bounds,
        IReadOnlyList<CanvasChannelLayoutState> Channels);

    private sealed record CanvasChannelLayoutState(
        DeviceChannelEntry Channel,
        Rect Bounds);

    private sealed record CanvasDeviceRenderState(
        string DeviceIdentifier,
        string ControllerDeviceIdentifier,
        string DeviceName,
        Rect Bounds,
        IReadOnlyList<CanvasChannelRenderState> Channels);

    private sealed record CanvasChannelRenderState(
        DeviceChannelEntry Channel,
        Rect Bounds,
        IReadOnlyList<LedColor> LedColors);

    private sealed record DeviceDraftState(
        string ProfileName,
        string? SelectedProfileName,
        string EffectName,
        int EffectSpeed,
        IReadOnlyList<AppSettingsStore.EffectParameterSetting> EffectParameters,
        int Red,
        int Green,
        int Blue,
        bool BackgroundColorEnabled,
        int BackgroundRed,
        int BackgroundGreen,
        int BackgroundBlue,
        IReadOnlyList<AppSettingsStore.ChannelSettings> Channels);
}
