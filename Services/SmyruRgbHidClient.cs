using HidSharp;

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

    public int MaxLedsPerChannel => _profile?.MaxLedsPerChannel ?? 126;

    public bool TryConnect(out string message)
    {
        Disconnect();

        foreach (HidDevice device in DeviceList.Local.GetHidDevices())
        {
            HidDeviceProfile? profile = Profiles.FirstOrDefault(candidate => candidate.Matches(device));
            if (profile is null)
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

        message = "No compatible Nollie HID device found. Check the USB cable and OS2/OS2.1 firmware.";
        return false;
    }

    public bool TrySetSolidColor(byte red, byte green, byte blue, int ledCountPerChannel, out string message)
    {
        if (_stream is null || _profile is null)
        {
            message = "No active HID connection.";
            return false;
        }

        if (ledCountPerChannel < 1 || ledCountPerChannel > _profile.MaxLedsPerChannel)
        {
            message = $"LED count must be between 1 and {_profile.MaxLedsPerChannel}.";
            return false;
        }

        try
        {
            if (_profile.IsHighSpeed)
            {
                SendHighSpeedColor(red, green, blue, ledCountPerChannel);
            }
            else
            {
                SendFullSpeedColor(red, green, blue, ledCountPerChannel);
            }

            message = $"Sent RGB({red}, {green}, {blue}) to {_profile.ChannelCount} channels with {ledCountPerChannel} LEDs each.";
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

    private void SendHighSpeedColor(byte red, byte green, byte blue, int ledCountPerChannel)
    {
        ArgumentNullException.ThrowIfNull(_profile);
        ArgumentNullException.ThrowIfNull(_stream);

        IEnumerable<int> channels = (_profile.ChannelMap ?? Enumerable.Range(0, _profile.ChannelCount).ToArray())
            .Take(_profile.ChannelCount)
            .OrderBy(channel => channel);

        foreach (int channel in channels)
        {
            byte[] report = new byte[1025];
            report[1] = (byte)channel;
            report[2] = 0;
            report[3] = (byte)(ledCountPerChannel >> 8);
            report[4] = (byte)(ledCountPerChannel & 0xFF);

            for (int index = 0; index < ledCountPerChannel; index++)
            {
                int offset = 5 + (index * 3);
                WriteColor(report, offset, red, green, blue, _profile.ColorOrder);
            }

            _stream.Write(report);
        }
    }

    private void SendFullSpeedColor(byte red, byte green, byte blue, int ledCountPerChannel)
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
            int remaining = ledCountPerChannel;
            int packetId = 0;

            while (remaining > 0)
            {
                int colorsInPacket = Math.Min(colorsPerPacket, remaining);
                byte[] report = new byte[65];
                report[1] = (byte)(packetId + (channel * packetInterval));

                for (int colorIndex = 0; colorIndex < colorsInPacket; colorIndex++)
                {
                    int offset = 2 + (colorIndex * 3);
                    WriteColor(report, offset, red, green, blue, _profile.ColorOrder);
                }

                _stream.Write(report);
                remaining -= colorsInPacket;
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

    private enum ColorOrder
    {
        RedGreenBlue,
        GreenRedBlue
    }

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