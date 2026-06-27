using System.Text;
using HidSharp;
using SmyruRGB.Effects;

namespace SmyruRGB.Controllers.Hid.Asus;

internal sealed class AsusAuraUsbMainboardDriver : IHidRgbControllerDriver
{
    private const string RequiredInterfaceMarker = "mi_02";

    public bool Matches(HidDevice device)
    {
        return device.VendorID == AsusAuraFamilies.VendorId
            && AsusAuraFamilies.AuraUsbMainboardProductIds.Contains(device.ProductID)
            && device.DevicePath.Contains(RequiredInterfaceMarker, StringComparison.OrdinalIgnoreCase);
    }

    public string BuildDeviceIdentifier(HidDevice device)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"ASUS-AURA-USB|{device.ProductID:X4}|SN:{serialNumber}";
        }

        return $"ASUS-AURA-USB|{device.ProductID:X4}|PATH:{device.DevicePath}";
    }

    public string BuildDeviceLabel(HidDevice device)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"ASUS Aura USB ({serialNumber})";
        }

        string[] parts = device.DevicePath.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string suffix = parts.LastOrDefault() ?? device.DevicePath;
        return $"ASUS Aura USB ({suffix})";
    }

    public string BuildDeviceSummary(HidDevice device)
    {
        return $"ASUS {AsusAuraFamilies.TufMainboardControllerFamily} | VID 0x{device.VendorID:X4} PID 0x{device.ProductID:X4} | {device.DevicePath}";
    }

    public bool TryOpen(HidDevice device, out IHidRgbControllerSession? session)
    {
        session = null;

        if (!device.TryOpen(out HidStream stream))
        {
            return false;
        }

        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;

        try
        {
            session = new AsusAuraUsbMainboardSession(device, stream);
            return true;
        }
        catch
        {
            stream.Dispose();
            return false;
        }
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

    private sealed class AsusAuraUsbMainboardSession : IHidRgbControllerSession
    {
        private const byte PacketHeader = 0xEC;
        private const byte FirmwareVersionCommand = 0x82;
        private const byte ConfigTableCommand = 0xB0;
        private const byte ConfigTableResponseId = 0x30;
        private const byte DirectModeCommand = 0x40;
        private const byte MainboardEffectCommand = 0x35;
        private const byte MainboardEffectColorCommand = 0x36;
        private const byte MainboardCommitCommand = 0x3F;
        private const byte DirectModeValue = 0xFF;
        private const int ConfigTableLength = 60;
        private const int LedsPerDirectPacket = 0x14;
        private const int DefaultArgbLedLimit = 120;

        private readonly HidDevice _device;
        private readonly HidStream _stream;
        private readonly byte[] _configTable;
        private readonly ChannelInfo[] _channels;
        private readonly bool[] _directModeInitialized;
        private readonly string _firmwareVersion;

        public AsusAuraUsbMainboardSession(HidDevice device, HidStream stream)
        {
            _device = device;
            _stream = stream;
            _firmwareVersion = ReadFirmwareVersion();
            _configTable = ReadConfigTable();
            _channels = BuildChannels(_configTable);
            _directModeInitialized = new bool[_channels.Length];

            if (_channels.Length == 0)
            {
                throw new InvalidOperationException("The ASUS Aura USB controller reported no usable channels.");
            }

            SendGen1Handshake();
        }

        public string DeviceName => string.IsNullOrWhiteSpace(_firmwareVersion)
            ? "ASUS Aura USB Motherboard"
            : $"ASUS Aura USB Motherboard ({_firmwareVersion})";

        public int VendorId => _device.VendorID;
        public int ProductId => _device.ProductID;
        public string DevicePath => _device.DevicePath;
        public int ChannelCount => _channels.Length;
        public int MaxLedsPerChannel => _channels.Max(channel => channel.MaxLeds);
        public IReadOnlyList<HidRgbChannelInfo> Channels => _channels
            .Select((channel, index) => new HidRgbChannelInfo(index + 1, channel.Name, channel.DefaultLedCount, channel.MaxLeds))
            .ToArray();

        public bool TrySetLedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel, out string message)
        {
            if (channelFrames.Count != _channels.Length)
            {
                message = $"Expected {_channels.Length} channel frames, got {channelFrames.Count}.";
                return false;
            }

            if (ledCountsPerChannel.Count != _channels.Length)
            {
                message = $"Expected {_channels.Length} channel LED settings, got {ledCountsPerChannel.Count}.";
                return false;
            }

            for (int channelIndex = 0; channelIndex < _channels.Length; channelIndex++)
            {
                ChannelInfo channel = _channels[channelIndex];
                int ledCount = ledCountsPerChannel[channelIndex];
                if (ledCount < 1 || ledCount > channel.MaxLeds)
                {
                    message = $"Channel {channelIndex + 1} LED count must be between 1 and {channel.MaxLeds}.";
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
                for (int channelIndex = 0; channelIndex < _channels.Length; channelIndex++)
                {
                    EnsureDirectMode(channelIndex);

                    ChannelInfo channel = _channels[channelIndex];
                    IReadOnlyList<LedColor> frame = channelFrames[channelIndex];

                    if (channel.Kind == ChannelKind.Fixed)
                    {
                        SendMainboardColor(frame);
                    }
                    else
                    {
                        SendAddressableDirect(channel.DirectChannel, frame);
                    }
                }

                message = $"Sent colors to {_channels.Length} ASUS Aura USB channels.";
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
            _stream.Dispose();
        }

        private static ChannelInfo[] BuildChannels(byte[] configTable)
        {
            byte totalMainboardLeds = configTable[0x1B];
            byte rgbHeaderCount = configTable[0x1D];
            byte addressableHeaderCount = configTable[0x02];
            byte effectChannel = 0;
            var channels = new List<ChannelInfo>();

            if (totalMainboardLeds < rgbHeaderCount)
            {
                rgbHeaderCount = 0;
            }

            if (totalMainboardLeds > 0)
            {
                channels.Add(new ChannelInfo(
                    ChannelKind.Fixed,
                    "Mainboard",
                    effectChannel,
                    0x04,
                    totalMainboardLeds,
                    totalMainboardLeds,
                    totalMainboardLeds,
                    rgbHeaderCount));
                effectChannel++;
            }

            for (byte headerIndex = 0; headerIndex < addressableHeaderCount; headerIndex++)
            {
                channels.Add(new ChannelInfo(
                    ChannelKind.Addressable,
                    $"Addressable Header {headerIndex + 1}",
                    effectChannel,
                    headerIndex,
                    DefaultArgbLedLimit,
                    30,
                    0,
                    0));
                effectChannel++;
            }

            return channels.ToArray();
        }

        private void EnsureDirectMode(int channelIndex)
        {
            if (_directModeInitialized[channelIndex])
            {
                return;
            }

            ChannelInfo channel = _channels[channelIndex];
            byte[] packet = new byte[65];
            packet[0] = PacketHeader;
            packet[1] = MainboardEffectCommand;
            packet[2] = channel.EffectChannel;
            packet[3] = 0x00;
            packet[4] = 0x00;
            packet[5] = DirectModeValue;

            Write(packet);
            _directModeInitialized[channelIndex] = true;
        }

        private string ReadFirmwareVersion()
        {
            byte[] request = new byte[65];
            request[0] = PacketHeader;
            request[1] = FirmwareVersionCommand;

            Write(request);

            byte[] response = ReadResponse();
            string version = Encoding.ASCII.GetString(response, 2, response.Length - 2).TrimEnd('\0', ' ');
            return version;
        }

        private byte[] ReadConfigTable()
        {
            byte[] request = new byte[65];
            request[0] = PacketHeader;
            request[1] = ConfigTableCommand;

            Write(request);

            byte[] response = ReadResponse();
            if (response.Length < 4 + ConfigTableLength || response[1] != ConfigTableResponseId)
            {
                throw new InvalidOperationException("The ASUS Aura USB controller returned an unexpected configuration response.");
            }

            byte[] configTable = new byte[ConfigTableLength];
            Array.Copy(response, 4, configTable, 0, ConfigTableLength);
            return configTable;
        }

        private void SendGen1Handshake()
        {
            byte[] packet = new byte[65];
            packet[0] = PacketHeader;
            packet[1] = 0x52;
            packet[2] = 0x53;
            packet[3] = 0x00;
            packet[4] = 0x01;
            Write(packet);
        }

        private void SendAddressableDirect(byte directChannel, IReadOnlyList<LedColor> frame)
        {
            int offset = 0;
            while (offset < frame.Count)
            {
                int ledsInPacket = Math.Min(LedsPerDirectPacket, frame.Count - offset);
                bool apply = offset + ledsInPacket >= frame.Count;

                byte[] packet = new byte[65];
                packet[0] = PacketHeader;
                packet[1] = DirectModeCommand;
                packet[2] = (byte)((apply ? 0x80 : 0x00) | directChannel);
                packet[3] = (byte)offset;
                packet[4] = (byte)ledsInPacket;

                for (int ledIndex = 0; ledIndex < ledsInPacket; ledIndex++)
                {
                    int packetOffset = 5 + (ledIndex * 3);
                    LedColor color = frame[offset + ledIndex];
                    packet[packetOffset] = color.Red;
                    packet[packetOffset + 1] = color.Green;
                    packet[packetOffset + 2] = color.Blue;
                }

                Write(packet);
                offset += ledsInPacket;
            }
        }

        private void SendMainboardColor(IReadOnlyList<LedColor> frame)
        {
            ushort mask = GetMask(0, frame.Count);
            byte[] packet = new byte[65];
            packet[0] = PacketHeader;
            packet[1] = MainboardEffectColorCommand;
            packet[2] = (byte)(mask >> 8);
            packet[3] = (byte)(mask & 0xFF);
            packet[4] = 0x00;

            for (int ledIndex = 0; ledIndex < frame.Count; ledIndex++)
            {
                int packetOffset = 5 + (ledIndex * 3);
                LedColor color = frame[ledIndex];
                packet[packetOffset] = color.Red;
                packet[packetOffset + 1] = color.Green;
                packet[packetOffset + 2] = color.Blue;
            }

            Write(packet);
            SendCommit();
        }

        private void SendCommit()
        {
            byte[] packet = new byte[65];
            packet[0] = PacketHeader;
            packet[1] = MainboardCommitCommand;
            packet[2] = 0x55;
            Write(packet);
        }

        private static ushort GetMask(int start, int size)
        {
            ushort mask = 0;
            for (int index = 0; index < size; index++)
            {
                mask |= (ushort)(1 << (start + index));
            }

            return mask;
        }

        private void Write(byte[] packet)
        {
            _stream.Write(packet);
        }

        private byte[] ReadResponse()
        {
            byte[] buffer = new byte[65];
            int offset = 0;

            while (offset < buffer.Length)
            {
                int bytesRead = _stream.Read(buffer, offset, buffer.Length - offset);
                if (bytesRead <= 0)
                {
                    break;
                }

                offset += bytesRead;
            }

            if (offset == 0)
            {
                throw new InvalidOperationException("The ASUS Aura USB controller did not respond.");
            }

            return offset == buffer.Length ? buffer : buffer[..offset];
        }

        private enum ChannelKind
        {
            Fixed,
            Addressable
        }

        private sealed record ChannelInfo(
            ChannelKind Kind,
            string Name,
            byte EffectChannel,
            byte DirectChannel,
            int MaxLeds,
            int DefaultLedCount,
            int NativeLedCount,
            int HeaderCount);
    }
}