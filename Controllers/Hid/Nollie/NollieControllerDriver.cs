using HidSharp;
using SmyruRGB.Effects;

namespace SmyruRGB.Controllers.Hid.Nollie;

internal sealed class NollieControllerDriver : IHidRgbControllerDriver
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

    public bool Matches(HidDevice device)
    {
        return TryGetProfile(device) is not null;
    }

    public string BuildDeviceIdentifier(HidDevice device)
    {
        HidDeviceProfile? profile = TryGetProfile(device);
        return profile is null
            ? device.DevicePath
            : BuildDeviceIdentifier(device, profile);
    }

    public string BuildDeviceLabel(HidDevice device)
    {
        HidDeviceProfile? profile = TryGetProfile(device);
        return profile is null
            ? device.DevicePath
            : BuildDeviceLabel(device, profile);
    }

    public string BuildDeviceSummary(HidDevice device)
    {
        HidDeviceProfile? profile = TryGetProfile(device);
        return profile is null
            ? device.DevicePath
            : $"{profile.Name} | VID 0x{profile.VendorId:X4} PID 0x{profile.ProductId:X4} | {device.DevicePath}";
    }

    public bool TryOpen(HidDevice device, out IHidRgbControllerSession? session)
    {
        session = null;

        HidDeviceProfile? profile = TryGetProfile(device);
        if (profile is null || !device.TryOpen(out HidStream stream))
        {
            return false;
        }

        stream.WriteTimeout = 500;
        session = new NollieControllerSession(device.DevicePath, stream, profile);
        return true;
    }

    private static HidDeviceProfile? TryGetProfile(HidDevice device)
    {
        return Profiles.FirstOrDefault(candidate => candidate.Matches(device));
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

    private sealed class NollieControllerSession(string devicePath, HidStream stream, HidDeviceProfile profile) : IHidRgbControllerSession
    {
        public string DeviceName => profile.Name;
        public int VendorId => profile.VendorId;
        public int ProductId => profile.ProductId;
        public string DevicePath => devicePath;
        public int ChannelCount => profile.ChannelCount;
        public int MaxLedsPerChannel => profile.MaxLedsPerChannel;
        public IReadOnlyList<HidRgbChannelInfo> Channels { get; } = Enumerable.Range(0, profile.ChannelCount)
            .Select(index => new HidRgbChannelInfo(index + 1, $"Channel {index + 1}", 30, profile.MaxLedsPerChannel))
            .ToArray();

        public bool TrySetLedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel, out string message)
        {
            if (channelFrames.Count != profile.ChannelCount)
            {
                message = $"Expected {profile.ChannelCount} channel frames, got {channelFrames.Count}.";
                return false;
            }

            if (ledCountsPerChannel.Count != profile.ChannelCount)
            {
                message = $"Expected {profile.ChannelCount} channel LED settings, got {ledCountsPerChannel.Count}.";
                return false;
            }

            for (int channelIndex = 0; channelIndex < ledCountsPerChannel.Count; channelIndex++)
            {
                int ledCount = ledCountsPerChannel[channelIndex];
                if (ledCount < 1 || ledCount > profile.MaxLedsPerChannel)
                {
                    message = $"Channel {channelIndex + 1} LED count must be between 1 and {profile.MaxLedsPerChannel}.";
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
                if (profile.IsHighSpeed)
                {
                    SendHighSpeedFrames(channelFrames, ledCountsPerChannel);
                }
                else
                {
                    SendFullSpeedFrames(channelFrames, ledCountsPerChannel);
                }

                message = $"Sent colors to {profile.ChannelCount} channels with individual LED counts.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }

        private void SendHighSpeedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel)
        {
            var channels = (profile.ChannelMap ?? Enumerable.Range(0, profile.ChannelCount).ToArray())
                .Take(profile.ChannelCount)
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
                    WriteColor(report, offset, color.Red, color.Green, color.Blue, profile.ColorOrder);
                }

                stream.Write(report);
            }
        }

        private void SendFullSpeedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel)
        {
            const int colorsPerPacket = 21;
            int packetInterval = profile.ChannelCount switch
            {
                8 => 6,
                12 => 2,
                1 => 30,
                _ => 25
            };

            for (int channel = 0; channel < profile.ChannelCount; channel++)
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
                        WriteColor(report, offset, color.Red, color.Green, color.Blue, profile.ColorOrder);
                    }

                    stream.Write(report);
                    remaining -= colorsInPacket;
                    ledOffset += colorsInPacket;
                    packetId++;
                }
            }

            byte[] flush = new byte[65];
            flush[1] = 0xFF;
            stream.Write(flush);
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
    }
}