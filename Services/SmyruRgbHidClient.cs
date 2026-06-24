using HidSharp;
using SmyruRGB.Effects;

namespace SmyruRGB;

internal sealed class SmyruRgbHidClient : IDisposable
{
    private static readonly int[] ChannelMap32 = [5, 4, 3, 2, 1, 0, 15, 14, 26, 27, 28, 29, 30, 31, 8, 9, 19, 18, 17, 16, 7, 6, 25, 24, 23, 22, 21, 20, 13, 12, 11, 10];
    private static readonly int[] ChannelMap16 = [19, 18, 17, 16, 24, 25, 26, 27, 20, 21, 22, 23, 31, 30, 29, 28, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
    private static readonly int[] ChannelMap16Os2 = [3, 2, 1, 0, 8, 9, 10, 11, 4, 5, 6, 7, 15, 14, 13, 12];

    private static readonly IReadOnlyList<HidDeviceProfile> Profiles =
    [
        new("Nollie 32", 0x3061, 0x4714, 32, 256, true, ChannelMap32, ColorOrder.GreenRedBlue),
        new("Nollie 32 OS2", 0x16D5, 0x4714, 32, 256, true, ChannelMap32, ColorOrder.GreenRedBlue),
        new("Nollie 32 OS2.1", 0x16D5, 0x2A32, 32, 256, true, ChannelMap32, ColorOrder.GreenRedBlue, "mi_00"),
        new("Nollie 16", 0x3061, 0x4716, 16, 256, true, ChannelMap16, ColorOrder.GreenRedBlue),
        new("Nollie 16 OS2", 0x16D5, 0x4716, 16, 256, true, ChannelMap16Os2, ColorOrder.GreenRedBlue),
        new("Nollie 16 OS2.1", 0x16D5, 0x2A16, 16, 256, true, ChannelMap16Os2, ColorOrder.GreenRedBlue, "mi_00"),
        new("Nollie 8", 0x16D2, 0x1F01, 8, 126, false, null, ColorOrder.GreenRedBlue),
        new("Nollie 8 OS2", 0x16D5, 0x1F01, 8, 126, false, null, ColorOrder.GreenRedBlue),
        new("Nollie 8 OS2.1", 0x16D5, 0x2A08, 8, 126, false, null, ColorOrder.GreenRedBlue, "mi_02")
    ];

    private HidDevice? _device;
    private HidStream? _stream;
    private HidDeviceProfile? _profile;

    public string DeviceSummary => _device is null || _profile is null
        ? "No supported device detected"
        : $"{_profile.Name} | VID 0x{_profile.VendorId:X4} PID 0x{_profile.ProductId:X4} | {_device.DevicePath}";

    public string? DeviceIdentifier => _device is null || _profile is null
        ? null
        : BuildDeviceIdentifier(_device, _profile);

    public int ChannelCount => _profile?.ChannelCount ?? 0;
    public int MaxLedsPerChannel => _profile?.MaxLedsPerChannel ?? 126;

    public IReadOnlyList<AvailableDeviceInfo> GetAvailableDevices()
    {
        return DeviceList.Local.GetHidDevices()
            .Select(device =>
            {
                HidDeviceProfile? profile = Profiles.FirstOrDefault(candidate => candidate.Matches(device));
                if (profile is null)
                {
                    return null;
                }

                return new AvailableDeviceInfo(
                    BuildDeviceIdentifier(device, profile),
                    BuildDeviceLabel(device, profile),
                    $"{profile.Name} | VID 0x{profile.VendorId:X4} PID 0x{profile.ProductId:X4} | {device.DevicePath}",
                    profile.ChannelCount,
                    profile.MaxLedsPerChannel);
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
            HidDeviceProfile? profile = Profiles.FirstOrDefault(candidate => candidate.Matches(device));
            if (profile is null)
            {
                continue;
            }

            string deviceIdentifier = BuildDeviceIdentifier(device, profile);
            if (preferredDeviceWasRequested && !string.Equals(deviceIdentifier, preferredDeviceIdentifier, StringComparison.Ordinal))
            {
                continue;
            }

            if (!device.TryOpen(out HidStream stream))
            {
                continue;
            }

            _device = device;
            _stream = stream;
            _profile = profile;
            _stream.WriteTimeout = 500;
            message = $"Connected to {profile.Name}.";
            return true;
        }

        message = preferredDeviceWasRequested
            ? "The selected controller is no longer available. Refresh the device list and choose another controller."
            : "No compatible Nollie HID device found. Check the USB cable and OS2/OS2.1 firmware.";
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
        if (_stream is null || _profile is null)
        {
            message = "No active HID connection.";
            return false;
        }

        if (channelFrames.Count != _profile.ChannelCount)
        {
            message = $"Expected {_profile.ChannelCount} channel frames, got {channelFrames.Count}.";
            return false;
        }

        if (ledCountsPerChannel.Count != _profile.ChannelCount)
        {
            message = $"Expected {_profile.ChannelCount} channel LED settings, got {ledCountsPerChannel.Count}.";
            return false;
        }

        for (int channelIndex = 0; channelIndex < ledCountsPerChannel.Count; channelIndex++)
        {
            int ledCount = ledCountsPerChannel[channelIndex];
            if (ledCount < 1 || ledCount > _profile.MaxLedsPerChannel)
            {
                message = $"Channel {channelIndex + 1} LED count must be between 1 and {_profile.MaxLedsPerChannel}.";
                return false;
            }

            if (channelFrames[channelIndex].Count != ledCount)
            {
                message = $"Channel {channelIndex + 1} frame contains {channelFrames[channelIndex].Count} LEDs, expected {ledCount}.";
                return false;
            }
        }

        try
        {
            if (_profile.IsHighSpeed)
            {
                SendHighSpeedFrames(channelFrames, ledCountsPerChannel);
            }
            else
            {
                SendFullSpeedFrames(channelFrames, ledCountsPerChannel);
            }

            message = $"Sent colors to {_profile.ChannelCount} channels with individual LED counts.";
            return true;
        }
        catch (Exception ex)
        {
            Disconnect();
            message = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _stream = null;
        _device = null;
        _profile = null;
    }

    private void SendHighSpeedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel)
    {
        ArgumentNullException.ThrowIfNull(_profile);
        ArgumentNullException.ThrowIfNull(_stream);

        var channels = (_profile.ChannelMap ?? Enumerable.Range(0, _profile.ChannelCount).ToArray())
            .Take(_profile.ChannelCount)
            .Select((physicalChannel, logicalChannel) => new { physicalChannel, logicalChannel })
            .OrderBy(entry => entry.physicalChannel);

        foreach (var channel in channels)
        {
            int ledCount = ledCountsPerChannel[channel.logicalChannel];
            IReadOnlyList<LedColor> ledFrame = channelFrames[channel.logicalChannel];
            byte[] report = new byte[1025];
            report[1] = (byte)channel.physicalChannel;
            report[2] = 0;
            report[3] = (byte)(ledCount >> 8);
            report[4] = (byte)(ledCount & 0xFF);

            for (int index = 0; index < ledCount; index++)
            {
                int offset = 5 + (index * 3);
                LedColor color = ledFrame[index];
                WriteColor(report, offset, color.Red, color.Green, color.Blue, _profile.ColorOrder);
            }

            _stream.Write(report);
        }
    }

    private void SendFullSpeedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel)
    {
        ArgumentNullException.ThrowIfNull(_profile);
        ArgumentNullException.ThrowIfNull(_stream);

        const int colorsPerPacket = 21;
        int packetInterval = _profile.ChannelCount switch
        {
            8 => 6,
            12 => 2,
            1 => 30,
            _ => 25
        };

        for (int channel = 0; channel < _profile.ChannelCount; channel++)
        {
            int remaining = ledCountsPerChannel[channel];
            IReadOnlyList<LedColor> ledFrame = channelFrames[channel];
            int packetId = 0;
            int ledOffset = 0;

            while (remaining > 0)
            {
                int colorsInPacket = Math.Min(colorsPerPacket, remaining);
                byte[] report = new byte[65];
                report[1] = (byte)(packetId + (channel * packetInterval));

                for (int colorIndex = 0; colorIndex < colorsInPacket; colorIndex++)
                {
                    int offset = 2 + (colorIndex * 3);
                    LedColor color = ledFrame[ledOffset + colorIndex];
                    WriteColor(report, offset, color.Red, color.Green, color.Blue, _profile.ColorOrder);
                }

                _stream.Write(report);
                remaining -= colorsInPacket;
                ledOffset += colorsInPacket;
                packetId++;
            }
        }

        byte[] flush = new byte[65];
        flush[1] = 0xFF;
        _stream.Write(flush);
    }

    private static void WriteColor(byte[] target, int offset, byte red, byte green, byte blue, ColorOrder colorOrder)
    {
        switch (colorOrder)
        {
            case ColorOrder.RedGreenBlue:
                target[offset] = red;
                target[offset + 1] = green;
                target[offset + 2] = blue;
                break;
            case ColorOrder.GreenRedBlue:
                target[offset] = green;
                target[offset + 1] = red;
                target[offset + 2] = blue;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(colorOrder), colorOrder, null);
        }
    }

    private static string BuildDeviceIdentifier(HidDevice device, HidDeviceProfile profile)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{profile.Name}|{profile.VendorId:X4}|{profile.ProductId:X4}|SN:{serialNumber}";
        }

        return $"{profile.Name}|{profile.VendorId:X4}|{profile.ProductId:X4}|PATH:{device.DevicePath}";
    }

    private static string BuildDeviceLabel(HidDevice device, HidDeviceProfile profile)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{profile.Name} ({serialNumber})";
        }

        string[] parts = device.DevicePath.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string suffix = parts.LastOrDefault() ?? device.DevicePath;
        return $"{profile.Name} ({suffix})";
    }

    private static string? TryGetSerialNumber(HidDevice device)
    {
        try
        {
            return device.GetSerialNumber();
        }
        catch
        {
            return null;
        }
    }

    private enum ColorOrder
    {
        RedGreenBlue,
        GreenRedBlue
    }

    internal sealed record AvailableDeviceInfo(
        string DeviceIdentifier,
        string DisplayName,
        string Summary,
        int ChannelCount,
        int MaxLedsPerChannel);

    private sealed record HidDeviceProfile(
        string Name,
        int VendorId,
        int ProductId,
        int ChannelCount,
        int MaxLedsPerChannel,
        bool IsHighSpeed,
        int[]? ChannelMap,
        ColorOrder ColorOrder,
        string? RequiredInterfaceMarker = null)
    {
        public bool Matches(HidDevice device)
        {
            if (device.VendorID != VendorId || device.ProductID != ProductId)
            {
                return false;
            }

            return RequiredInterfaceMarker is null
                || device.DevicePath.Contains(RequiredInterfaceMarker, StringComparison.OrdinalIgnoreCase);
        }
    }
}