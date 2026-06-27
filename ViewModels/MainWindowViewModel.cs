using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using ReactiveUI;
using SmyruRGB.Controllers.Hid;
using SmyruRGB.Effects;
using System.Reactive;

namespace SmyruRGB;

public class MainWindowViewModel : ReactiveObject
{
    private readonly SmyruRgbHidClient _client = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly WindowsStartupRegistration _windowsStartupRegistration = new();
    private readonly Dictionary<string, DeviceDraftState> _deviceDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, int>> _groupedDeviceLedCountOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> _groupedDeviceChannelNameOverrides = new(StringComparer.Ordinal);
    private CancellationTokenSource? _effectCancellationTokenSource;
    private string? _currentDeviceIdentifier;
    private bool _isUpdatingControllerSelection;
    private bool _isLoadingUiSettings;
    private bool _isControllerDetailsVisible;
    private string _baseColorHex = "FF0000";
    private string _backgroundColorHex = "000000";

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
    public ObservableCollection<string> AvailableProfiles { get; } = [];
    public ObservableCollection<string> AvailableEffects { get; } = [];
    public ObservableCollection<EffectParameterSetting> EffectParameters { get; } = [];
    public ObservableCollection<EffectParameterGroup> EffectParameterGroups { get; } = [];

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
            channels.Select(channel => NormalizeChannelDeviceName(channel.ChannelName, channel.ChannelNumber)).ToArray(),
            channels.Count,
            tick,
            EffectParameters.ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal));
    }

    private bool TryCreateDeviceFrameRequests(
        ILedEffect effect,
        long tick,
        out IReadOnlyList<SmyruRgbHidClient.DeviceFrameRequest> deviceRequests,
        out int delayMilliseconds,
        out string message)
    {
        List<SmyruRgbHidClient.DeviceFrameRequest> requests = [];
        delayMilliseconds = 33;
        message = string.Empty;

        foreach (ControllerOption controller in AvailableControllers)
        {
            IReadOnlyList<DeviceChannelEntry> channels = BuildDeviceChannelEntries(controller);
            if (channels.Count == 0)
            {
                continue;
            }

            EffectFrame frame = effect.CreateFrame(CreateEffectContext(channels, tick));
            requests.Add(new SmyruRgbHidClient.DeviceFrameRequest(
                controller.DeviceIdentifier,
                frame.ChannelLeds,
                channels.Select(channel => channel.LedCount).ToArray()));

            if (requests.Count == 1
                || string.Equals(controller.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
            {
                delayMilliseconds = frame.DelayMilliseconds;
            }
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
        ChannelLedSetting[] existingSettings = ChannelLedSettings.ToArray();

        ChannelLedSettings.Clear();
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            HidRgbChannelInfo channel = channels[channelIndex];
            int initialCount = channelIndex < existingSettings.Length
                ? existingSettings[channelIndex].LedCount
                : channel.DefaultLedCount;
            string initialName = channelIndex < existingSettings.Length
                ? existingSettings[channelIndex].ChannelName
                : GetDefaultChannelDeviceName(channel.ChannelNumber);

            ChannelLedSetting setting = new(channel.ChannelNumber, initialName, Math.Min(initialCount, channel.MaxLedCount), channel.MaxLedCount);
            setting.PropertyChanged += OnChannelLedSettingChanged;
            ChannelLedSettings.Add(setting);
        }

        RebuildAllDeviceChannelGroups();
    }

    private void OnChannelLedSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChannelLedSetting.LedCount))
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
        profile.Channels = ChannelLedSettings.Select(setting => new AppSettingsStore.ChannelSettings
        {
            Name = setting.ChannelName,
            LedCount = setting.LedCount
        }).ToList();

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

            setting.ChannelName = !string.IsNullOrWhiteSpace(savedChannel?.Name)
                ? savedChannel.Name
                : GetDefaultChannelDeviceName(setting.ChannelNumber);
            setting.LedCount = savedChannel?.LedCount ?? setting.LedCount;
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
            bool hasLedOverrides = _groupedDeviceLedCountOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, int>? ledOverrides)
                && ledOverrides.Count > 0;
            bool hasNameOverrides = _groupedDeviceChannelNameOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, string>? nameOverrides)
                && nameOverrides.Count > 0;

            if (!hasDraft && !hasLedOverrides && !hasNameOverrides)
            {
                continue;
            }

            string profileName = ResolveGroupedDeviceProfileName(savedSettings, controller.DeviceIdentifier, draft);
            AppSettingsStore.DeviceSettings deviceSettings = GetOrCreateDeviceSettings(savedSettings, controller.DeviceIdentifier);
            AppSettingsStore.ProfileSettings profile = deviceSettings.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase))
                ?? new AppSettingsStore.ProfileSettings { Name = profileName };

            profile.Name = profileName;
            profile.Channels = BuildDeviceChannelEntries(controller).Select(entry => new AppSettingsStore.ChannelSettings
            {
                Name = NormalizeChannelDeviceName(entry.ChannelName, entry.ChannelNumber),
                LedCount = entry.LedCount
            }).ToList();

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
            ChannelLedSettings.Select(setting => new AppSettingsStore.ChannelSettings
            {
                Name = setting.ChannelName,
                LedCount = setting.LedCount
            }).ToList());

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

            ChannelLedSettings[channelIndex].ChannelName = savedChannel.Name;
            ChannelLedSettings[channelIndex].LedCount = savedChannel.LedCount;
        }

        RebuildAllDeviceChannelGroups();
    }

    private void RebuildAllDeviceChannelGroups()
    {
        AllDeviceChannelGroups.Clear();

        foreach (ControllerOption controller in AvailableControllers)
        {
            IReadOnlyList<DeviceChannelEntry> channels = BuildDeviceChannelEntries(controller);
            AllDeviceChannelGroups.Add(new DeviceChannelGroup(controller.DeviceIdentifier, controller.ShortDisplayName, channels));
        }
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
                setting.ChannelName,
                setting.LedCount,
                setting.MaxLedCount,
                UpdateGroupedDeviceChannelName,
                UpdateGroupedDeviceChannelLedCount)).ToArray();
        }

        IReadOnlyList<AppSettingsStore.ChannelSettings>? savedChannels = null;
        if (_deviceDrafts.TryGetValue(controller.DeviceIdentifier, out DeviceDraftState? draft))
        {
            savedChannels = draft.Channels;
        }

        Dictionary<int, int>? overrides = _groupedDeviceLedCountOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, int>? values)
            ? values
            : null;
        Dictionary<int, string>? nameOverrides = _groupedDeviceChannelNameOverrides.TryGetValue(controller.DeviceIdentifier, out Dictionary<int, string>? names)
            ? names
            : null;

        return controller.Channels.Select((channel, index) =>
        {
            AppSettingsStore.ChannelSettings? savedChannel = savedChannels is not null && index < savedChannels.Count
                ? savedChannels[index]
                : null;

            int ledCount = savedChannel?.LedCount ?? channel.DefaultLedCount;
            if (overrides is not null && overrides.TryGetValue(channel.ChannelNumber, out int overriddenCount))
            {
                ledCount = overriddenCount;
            }

            string channelName = !string.IsNullOrWhiteSpace(savedChannel?.Name)
                ? savedChannel.Name
                : GetDefaultChannelDeviceName(channel.ChannelNumber);

            if (nameOverrides is not null && nameOverrides.TryGetValue(channel.ChannelNumber, out string? overriddenName))
            {
                channelName = overriddenName;
            }

            return new DeviceChannelEntry(
                controller.DeviceIdentifier,
                channel.ChannelNumber,
                $"Channel {channel.ChannelNumber}",
                channelName,
                ledCount,
                channel.MaxLedCount,
                UpdateGroupedDeviceChannelName,
                UpdateGroupedDeviceChannelLedCount);
        }).ToArray();
    }

    private void UpdateGroupedDeviceChannelName(DeviceChannelEntry entry)
    {
        string normalizedName = NormalizeChannelDeviceName(entry.ChannelName, entry.ChannelNumber);
        if (!string.Equals(entry.ChannelName, normalizedName, StringComparison.Ordinal))
        {
            entry.SetChannelName(normalizedName);
        }

        if (!string.IsNullOrWhiteSpace(_currentDeviceIdentifier)
            && string.Equals(entry.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
        {
            ChannelLedSetting? channelSetting = ChannelLedSettings.FirstOrDefault(setting => setting.ChannelNumber == entry.ChannelNumber);
            if (channelSetting is not null && !string.Equals(channelSetting.ChannelName, normalizedName, StringComparison.Ordinal))
            {
                channelSetting.ChannelName = normalizedName;
            }

            return;
        }

        if (!_groupedDeviceChannelNameOverrides.TryGetValue(entry.DeviceIdentifier, out Dictionary<int, string>? overrides))
        {
            overrides = new Dictionary<int, string>();
            _groupedDeviceChannelNameOverrides[entry.DeviceIdentifier] = overrides;
        }

        overrides[entry.ChannelNumber] = normalizedName;
    }

    private void UpdateGroupedDeviceChannelLedCount(DeviceChannelEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_currentDeviceIdentifier)
            && string.Equals(entry.DeviceIdentifier, _currentDeviceIdentifier, StringComparison.Ordinal))
        {
            ChannelLedSetting? channelSetting = ChannelLedSettings.FirstOrDefault(setting => setting.ChannelNumber == entry.ChannelNumber);
            if (channelSetting is not null && channelSetting.LedCount != entry.LedCount)
            {
                channelSetting.LedCount = entry.LedCount;
            }

            return;
        }

        if (!_groupedDeviceLedCountOverrides.TryGetValue(entry.DeviceIdentifier, out Dictionary<int, int>? overrides))
        {
            overrides = new Dictionary<int, int>();
            _groupedDeviceLedCountOverrides[entry.DeviceIdentifier] = overrides;
        }

        overrides[entry.ChannelNumber] = entry.LedCount;
    }

    private static string NormalizeChannelDeviceName(string? deviceName, int channelNumber)
    {
        string normalizedName = deviceName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedName) ? GetDefaultChannelDeviceName(channelNumber) : normalizedName;
    }

    private static string GetDefaultChannelDeviceName(int channelNumber) => $"Device {channelNumber}";

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
        private int _ledCount;
        private string _channelName;
        private int _maxLedCount;

        public ChannelLedSetting(int channelNumber, string channelName, int ledCount, int maxLedCount)
        {
            ChannelNumber = channelNumber;
            _channelName = string.IsNullOrWhiteSpace(channelName) ? $"Channel {channelNumber}" : channelName;
            _maxLedCount = Math.Max(1, maxLedCount);
            _ledCount = Math.Clamp(ledCount, 1, _maxLedCount);
        }

        public int ChannelNumber { get; }

        public string ChannelName
        {
            get => _channelName;
            set => this.RaiseAndSetIfChanged(ref _channelName, string.IsNullOrWhiteSpace(value) ? $"Channel {ChannelNumber}" : value.Trim());
        }

        public int LedCount
        {
            get => _ledCount;
            set => this.RaiseAndSetIfChanged(ref _ledCount, Math.Clamp(value, 1, MaxLedCount));
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

    public sealed class DeviceChannelGroup
    {
        public DeviceChannelGroup(string deviceIdentifier, string deviceName, IReadOnlyList<DeviceChannelEntry> channels)
        {
            DeviceIdentifier = deviceIdentifier;
            DeviceName = deviceName;
            Channels = new ObservableCollection<DeviceChannelEntry>(channels);
        }

        internal string DeviceIdentifier { get; }

        public string DeviceName { get; }

        public ObservableCollection<DeviceChannelEntry> Channels { get; }
    }

    public sealed class DeviceChannelEntry : ReactiveObject
    {
        private readonly Action<DeviceChannelEntry> _onChannelNameChanged;
        private readonly Action<DeviceChannelEntry> _onLedCountChanged;
        private string _channelName;
        private int _ledCount;
        private string _ledCountText;

        public DeviceChannelEntry(
            string deviceIdentifier,
            int channelNumber,
            string channelLabel,
            string channelName,
            int ledCount,
            int maxLedCount,
            Action<DeviceChannelEntry> onChannelNameChanged,
            Action<DeviceChannelEntry> onLedCountChanged)
        {
            DeviceIdentifier = deviceIdentifier;
            ChannelNumber = channelNumber;
            ChannelLabel = channelLabel;
            _channelName = NormalizeChannelDeviceName(channelName, channelNumber);
            MaxLedCount = Math.Max(1, maxLedCount);
            _ledCount = Math.Clamp(ledCount, 1, MaxLedCount);
            _ledCountText = _ledCount.ToString(CultureInfo.InvariantCulture);
            _onChannelNameChanged = onChannelNameChanged;
            _onLedCountChanged = onLedCountChanged;
        }

        internal string DeviceIdentifier { get; }

        internal int ChannelNumber { get; }

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
                _onChannelNameChanged(this);
            }
        }

        internal void SetChannelName(string channelName)
        {
            this.RaiseAndSetIfChanged(ref _channelName, channelName);
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

                _onLedCountChanged(this);
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
    }

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
