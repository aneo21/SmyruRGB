using HidSharp;
using SmyruRGB.Controllers.Hid;
using SmyruRGB.Effects;

namespace SmyruRGB;

internal sealed class SmyruRgbHidClient : IDisposable
{
    private static readonly IReadOnlyList<IHidRgbControllerDriver> Drivers = HidRgbControllerRegistry.All;

    private HidDevice? _device;
    private IHidRgbControllerDriver? _driver;
    private IHidRgbControllerSession? _session;

    public string DeviceSummary => _device is null || _session is null
        ? "No supported device detected"
        : $"{_session.DeviceName} | VID 0x{_session.VendorId:X4} PID 0x{_session.ProductId:X4} | {_session.DevicePath}";

    public string? DeviceIdentifier => _device is null || _driver is null
        ? null
        : _driver.BuildDeviceIdentifier(_device);

    public int ChannelCount => _session?.ChannelCount ?? 0;
    public int MaxLedsPerChannel => _session?.MaxLedsPerChannel ?? 126;
    public IReadOnlyList<HidRgbChannelInfo> Channels => _session?.Channels ?? _defaultChannels;

    private static readonly IReadOnlyList<HidRgbChannelInfo> _defaultChannels = Enumerable.Range(0, 8)
        .Select(index => new HidRgbChannelInfo(index + 1, $"Channel {index + 1}", 30, 126))
        .ToArray();

    public IReadOnlyList<AvailableDeviceInfo> GetAvailableDevices()
    {
        return DeviceList.Local.GetHidDevices()
            .Select(device =>
            {
                IHidRgbControllerDriver? driver = GetMatchingDriver(device);
                if (driver is null)
                {
                    return null;
                }

                int channelCount = 0;
                int maxLedsPerChannel = 0;
                IReadOnlyList<HidRgbChannelInfo> channels = [];
                if (driver.TryOpen(device, out IHidRgbControllerSession? previewSession) && previewSession is not null)
                {
                    try
                    {
                        channelCount = previewSession.ChannelCount;
                        maxLedsPerChannel = previewSession.MaxLedsPerChannel;
                        channels = previewSession.Channels.ToArray();
                    }
                    finally
                    {
                        previewSession.Dispose();
                    }
                }

                return new AvailableDeviceInfo(
                    driver.BuildDeviceIdentifier(device),
                    driver.BuildDeviceLabel(device),
                    driver.BuildDeviceSummary(device),
                    channelCount,
                    maxLedsPerChannel,
                    channels);
            })
            .Where(info => info is not null)
            .Cast<AvailableDeviceInfo>()
            .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.DeviceIdentifier, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryConnect(string? preferredDeviceIdentifier, out string message)
    {
        Disconnect();

        bool preferredDeviceWasRequested = !string.IsNullOrWhiteSpace(preferredDeviceIdentifier);

        foreach (HidDevice device in DeviceList.Local.GetHidDevices())
        {
            IHidRgbControllerDriver? driver = GetMatchingDriver(device);
            if (driver is null)
            {
                continue;
            }

            string deviceIdentifier = driver.BuildDeviceIdentifier(device);
            if (preferredDeviceWasRequested && !string.Equals(deviceIdentifier, preferredDeviceIdentifier, StringComparison.Ordinal))
            {
                continue;
            }

            if (!driver.TryOpen(device, out IHidRgbControllerSession? session) || session is null)
            {
                continue;
            }

            _device = device;
            _driver = driver;
            _session = session;
            message = $"Connected to {session.DeviceName}.";
            return true;
        }

        message = preferredDeviceWasRequested
            ? "The selected controller is no longer available. Refresh the device list and choose another controller."
            : "No compatible RGB HID device found. Connect a supported controller and refresh the device list.";
        return false;
    }

    public bool TrySetSolidColor(byte red, byte green, byte blue, IReadOnlyList<int> ledCountsPerChannel, out string message)
    {
        IReadOnlyList<LedColor> channelColors = Enumerable.Range(0, ledCountsPerChannel.Count)
            .Select(_ => new LedColor(red, green, blue))
            .ToArray();

        return TrySetChannelColors(channelColors, ledCountsPerChannel, out message);
    }

    public bool TrySetChannelColors(IReadOnlyList<LedColor> channelColors, IReadOnlyList<int> ledCountsPerChannel, out string message)
    {
        IReadOnlyList<IReadOnlyList<LedColor>> channelFrames = channelColors
            .Select((color, index) => (IReadOnlyList<LedColor>)Enumerable.Repeat(color, ledCountsPerChannel[index]).ToArray())
            .ToArray();

        return TrySetLedFrames(channelFrames, ledCountsPerChannel, out message);
    }

    public bool TrySetLedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel, out string message)
    {
        if (_session is null)
        {
            message = "No active HID connection.";
            return false;
        }

        try
        {
            return _session.TrySetLedFrames(channelFrames, ledCountsPerChannel, out message);
        }
        catch (Exception ex)
        {
            Disconnect();
            message = ex.Message;
            return false;
        }
    }

    public bool TrySetLedFrames(IReadOnlyList<DeviceFrameRequest> deviceRequests, out string message)
    {
        if (deviceRequests.Count == 0)
        {
            message = "No compatible RGB HID device found. Connect a supported controller and refresh the device list.";
            return false;
        }

        Dictionary<string, AvailableHidTarget> availableTargets = DeviceList.Local.GetHidDevices()
            .Select(device =>
            {
                IHidRgbControllerDriver? driver = GetMatchingDriver(device);
                return driver is null
                    ? null
                    : new AvailableHidTarget(driver.BuildDeviceIdentifier(device), device, driver);
            })
            .Where(target => target is not null)
            .Cast<AvailableHidTarget>()
            .ToDictionary(target => target.DeviceIdentifier, StringComparer.Ordinal);

        int updatedDeviceCount = 0;
        foreach (DeviceFrameRequest request in deviceRequests)
        {
            if (string.Equals(request.DeviceIdentifier, DeviceIdentifier, StringComparison.Ordinal))
            {
                if (!TrySetLedFrames(request.ChannelFrames, request.LedCountsPerChannel, out message))
                {
                    return false;
                }

                updatedDeviceCount++;
                continue;
            }

            if (!availableTargets.TryGetValue(request.DeviceIdentifier, out AvailableHidTarget? target))
            {
                message = $"The controller '{request.DeviceIdentifier}' is no longer available. Refresh the device list and try again.";
                return false;
            }

            if (!target.Driver.TryOpen(target.Device, out IHidRgbControllerSession? session) || session is null)
            {
                message = $"Could not open controller '{request.DeviceIdentifier}'.";
                return false;
            }

            using (session)
            {
                try
                {
                    if (!session.TrySetLedFrames(request.ChannelFrames, request.LedCountsPerChannel, out message))
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    return false;
                }
            }

            updatedDeviceCount++;
        }

        message = updatedDeviceCount == 1
            ? "Updated 1 device."
            : $"Updated {updatedDeviceCount} devices.";
        return true;
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void Disconnect()
    {
        _session?.Dispose();
        _session = null;
        _driver = null;
        _device = null;
    }

    internal sealed record AvailableDeviceInfo(
        string DeviceIdentifier,
        string DisplayName,
        string Summary,
        int ChannelCount,
        int MaxLedsPerChannel,
        IReadOnlyList<HidRgbChannelInfo> Channels);

    internal sealed record DeviceFrameRequest(
        string DeviceIdentifier,
        IReadOnlyList<IReadOnlyList<LedColor>> ChannelFrames,
        IReadOnlyList<int> LedCountsPerChannel);

    private sealed record AvailableHidTarget(
        string DeviceIdentifier,
        HidDevice Device,
        IHidRgbControllerDriver Driver);

    private static IHidRgbControllerDriver? GetMatchingDriver(HidDevice device)
    {
        return Drivers.FirstOrDefault(driver => driver.Matches(device));
    }
}