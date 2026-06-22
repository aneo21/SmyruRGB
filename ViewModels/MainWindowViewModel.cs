using ReactiveUI;
using System.Reactive;

namespace SmyruRGB;

public class MainWindowViewModel : ReactiveObject
{
    private readonly SmyruRgbHidClient _client = new();

    public MainWindowViewModel()
    {
        RefreshCommand = ReactiveCommand.Create(RefreshConnection);

        ApplyColorCommand = ReactiveCommand.Create(() =>
        {
            if (!IsConnected)
            {
                Status = "No device is connected.";
                return;
            }

            if (_client.TrySetSolidColor((byte)R, (byte)G, (byte)B, LedCountPerChannel, out string message))
            {
                Status = message;
            }
            else
            {
                Status = $"Send error: {message}";
                IsConnected = false;
            }
        }, this.WhenAnyValue(x => x.IsConnected, x => x.LedCountPerChannel, (connected, ledCount) => connected && ledCount > 0));

        RefreshConnection();
    }

    private int _r = 255;
    public int R
    {
        get => _r;
        set => this.RaiseAndSetIfChanged(ref _r, Math.Clamp(value, 0, 255));
    }

    private int _g;
    public int G
    {
        get => _g;
        set => this.RaiseAndSetIfChanged(ref _g, Math.Clamp(value, 0, 255));
    }

    private int _b;
    public int B
    {
        get => _b;
        set => this.RaiseAndSetIfChanged(ref _b, Math.Clamp(value, 0, 255));
    }

    private int _ledCountPerChannel = 30;
    public int LedCountPerChannel
    {
        get => _ledCountPerChannel;
        set => this.RaiseAndSetIfChanged(ref _ledCountPerChannel, Math.Max(1, value));
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

    private string _status = "Initializing connection...";
    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => this.RaiseAndSetIfChanged(ref _isConnected, value); }

    public ReactiveCommand<Unit, Unit> ApplyColorCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    private void RefreshConnection()
    {
        if (_client.TryConnect(out string message))
        {
            DeviceSummary = _client.DeviceSummary;
            MaxLedsPerChannel = _client.MaxLedsPerChannel;
            LedCountPerChannel = Math.Min(LedCountPerChannel, MaxLedsPerChannel);
            Status = message;
            IsConnected = true;
        }
        else
        {
            DeviceSummary = "No device";
            MaxLedsPerChannel = 126;
            Status = message;
            IsConnected = false;
        }
    }
}
