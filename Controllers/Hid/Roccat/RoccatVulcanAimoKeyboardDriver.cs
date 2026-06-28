using HidSharp;
using SmyruRGB.Effects;

namespace SmyruRGB.Controllers.Hid.Roccat;

internal sealed class RoccatVulcanAimoKeyboardDriver : IHidRgbControllerDriver
{
    private const int RoccatVendorId = 0x1E7D;
    private const int RoccatVulcan120AimoProductId = 0x3098;
    private const string ControlInterfaceMarker = "mi_01";
    private const string LedInterfaceMarker = "mi_03";
    private const int RoccatLayoutUs = 0;
    private const int ExpectedLedReportLength = 65;
    private const int MissingLed = -1;
    private const int FullLedCount = 132;

    private static readonly int[,] UsLayoutMatrix =
    {
        { 0, MissingLed, 8, 14, 19, 24, MissingLed, 34, 39, 44, 49, 55, 61, 66, 70, MissingLed, 74, 78, 83, MissingLed, MissingLed, MissingLed, MissingLed, MissingLed },
        { 1, 6, 9, 15, 20, 25, 29, 35, 40, 45, 50, 56, 62, 67, MissingLed, MissingLed, 75, 79, 84, MissingLed, 87, 92, 96, 101 },
        { 2, MissingLed, 10, 16, 21, 26, 30, 36, 41, 46, 51, 57, 63, 68, 71, MissingLed, 76, 80, 85, MissingLed, 88, 93, 97, 102 },
        { 3, MissingLed, 11, 17, 22, 27, 31, 37, 42, 47, 52, 58, 64, MissingLed, 72, MissingLed, MissingLed, MissingLed, MissingLed, MissingLed, 89, 94, 98, MissingLed },
        { 4, MissingLed, 12, 18, 23, 28, 32, 38, 43, 48, 53, 59, MissingLed, 69, MissingLed, MissingLed, MissingLed, 81, MissingLed, MissingLed, 90, 95, 99, 103 },
        { 5, 7, 13, MissingLed, MissingLed, MissingLed, 33, MissingLed, MissingLed, MissingLed, 54, 60, 65, MissingLed, 73, MissingLed, 77, 82, 86, MissingLed, 91, MissingLed, 100, MissingLed }
    };

    private static readonly int[,] IsoLayoutMatrix =
    {
        { 0, MissingLed, 9, 15, 20, 25, MissingLed, 35, 40, 45, 50, 56, 62, 67, 72, MissingLed, 75, 79, 84, MissingLed, MissingLed, MissingLed, MissingLed, MissingLed },
        { 1, 6, 10, 16, 21, 26, 30, 36, 41, 46, 51, 57, 63, 68, MissingLed, MissingLed, 76, 80, 85, MissingLed, 88, 93, 97, 102 },
        { 2, MissingLed, 11, 17, 22, 27, 31, 37, 42, 47, 52, 58, 64, 69, MissingLed, MissingLed, 77, 81, 86, MissingLed, 89, 94, 98, 103 },
        { 3, MissingLed, 12, 18, 23, 28, 32, 38, 43, 48, 53, 59, 65, 70, 73, MissingLed, MissingLed, MissingLed, MissingLed, MissingLed, 90, 95, 99, MissingLed },
        { 4, 7, 13, 19, 24, 29, 33, 39, 44, 49, 54, 60, MissingLed, 71, MissingLed, MissingLed, MissingLed, 82, MissingLed, MissingLed, 91, 96, 100, 104 },
        { 5, 8, 14, MissingLed, MissingLed, MissingLed, 34, MissingLed, MissingLed, MissingLed, 55, 61, 66, MissingLed, 74, MissingLed, 78, 83, 87, MissingLed, 92, MissingLed, 101, MissingLed }
    };

    public bool Matches(HidDevice device)
    {
        return device.VendorID == RoccatVendorId
            && device.ProductID == RoccatVulcan120AimoProductId
            && device.DevicePath.Contains(ControlInterfaceMarker, StringComparison.OrdinalIgnoreCase)
            && device.GetMaxFeatureReportLength() > 0;
    }

    public string BuildDeviceIdentifier(HidDevice device)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"ROCCAT-VULCAN-AIMO|{device.ProductID:X4}|SN:{serialNumber}";
        }

        return $"ROCCAT-VULCAN-AIMO|{device.ProductID:X4}|PATH:{NormalizeInterfacePath(device.DevicePath)}";
    }

    public string BuildDeviceLabel(HidDevice device)
    {
        string? serialNumber = TryGetSerialNumber(device);
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"Roccat Vulcan 120/121 AIMO ({serialNumber})";
        }

        string[] parts = NormalizeInterfacePath(device.DevicePath)
            .Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string suffix = parts.LastOrDefault() ?? device.DevicePath;
        return $"Roccat Vulcan 120/121 AIMO ({suffix})";
    }

    public string BuildDeviceSummary(HidDevice device)
    {
        return $"Roccat Vulcan 120/121 AIMO | VID 0x{device.VendorID:X4} PID 0x{device.ProductID:X4} | {NormalizeInterfacePath(device.DevicePath)}";
    }

    public bool TryOpen(HidDevice device, out IHidRgbControllerSession? session)
    {
        session = null;

        if (!TryFindLedInterface(device, out HidDevice? ledDevice) || ledDevice is null)
        {
            return false;
        }

        if (!device.TryOpen(out HidStream controlStream)
            || !ledDevice.TryOpen(out HidStream ledStream))
        {
            return false;
        }

        controlStream.ReadTimeout = 1000;
        controlStream.WriteTimeout = 1000;
        ledStream.ReadTimeout = 20;
        ledStream.WriteTimeout = 1000;

        try
        {
            session = new RoccatVulcanAimoKeyboardSession(device, controlStream, ledStream);
            return true;
        }
        catch
        {
            ledStream.Dispose();
            controlStream.Dispose();
            return false;
        }
    }

    private static bool TryFindLedInterface(HidDevice controlDevice, out HidDevice? ledDevice)
    {
        ledDevice = DeviceList.Local.GetHidDevices(controlDevice.VendorID, controlDevice.ProductID)
            .FirstOrDefault(candidate =>
                candidate.DevicePath.Contains(LedInterfaceMarker, StringComparison.OrdinalIgnoreCase)
                && candidate.GetMaxOutputReportLength() >= ExpectedLedReportLength
                && candidate.GetMaxInputReportLength() >= ExpectedLedReportLength);

        return ledDevice is not null;
    }

    private static string NormalizeInterfacePath(string devicePath)
    {
        return devicePath
            .Replace($"&{ControlInterfaceMarker}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"&{LedInterfaceMarker}", string.Empty, StringComparison.OrdinalIgnoreCase);
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

    private sealed class RoccatVulcanAimoKeyboardSession : IHidRgbControllerSession
    {
        private readonly HidDevice _controlDevice;
        private readonly HidStream _controlStream;
        private readonly HidStream _ledStream;
        private readonly DeviceInfo _deviceInfo;
        private readonly int[] _logicalToHardwareLedIds;
        private readonly int _maxLedCount;

        public RoccatVulcanAimoKeyboardSession(HidDevice controlDevice, HidStream controlStream, HidStream ledStream)
        {
            _controlDevice = controlDevice;
            _controlStream = controlStream;
            _ledStream = ledStream;
            _deviceInfo = ReadDeviceInfo();
            _logicalToHardwareLedIds = BuildLedMap(_deviceInfo.LayoutType);
            _maxLedCount = _logicalToHardwareLedIds.Length;
            Channels = [new HidRgbChannelInfo(1, "Keyboard", _maxLedCount, _maxLedCount)];

            EnableDirectMode(true);
            WaitUntilReady();
        }

        public string DeviceName => string.IsNullOrWhiteSpace(_deviceInfo.Version)
            ? "Roccat Vulcan 120/121 AIMO"
            : $"Roccat Vulcan 120/121 AIMO ({_deviceInfo.Version})";

        public int VendorId => _controlDevice.VendorID;
        public int ProductId => _controlDevice.ProductID;
        public string DevicePath => NormalizeInterfacePath(_controlDevice.DevicePath);
        public int ChannelCount => 1;
        public int MaxLedsPerChannel => _maxLedCount;
        public IReadOnlyList<HidRgbChannelInfo> Channels { get; }

        public bool TrySetLedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel, out string message)
        {
            if (channelFrames.Count != 1)
            {
                message = $"Expected 1 channel frame, got {channelFrames.Count}.";
                return false;
            }

            if (ledCountsPerChannel.Count != 1)
            {
                message = $"Expected 1 channel LED setting, got {ledCountsPerChannel.Count}.";
                return false;
            }

            int ledCount = ledCountsPerChannel[0];
            if (ledCount < 1 || ledCount > _maxLedCount)
            {
                message = $"Keyboard LED count must be between 1 and {_maxLedCount}.";
                return false;
            }

            IReadOnlyList<LedColor> frame = channelFrames[0];
            if (frame.Count != ledCount)
            {
                message = $"Keyboard frame contains {frame.Count} LEDs, expected {ledCount}.";
                return false;
            }

            try
            {
                SendColors(frame);
                message = $"Sent colors to {ledCount} keyboard LEDs.";
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
            _ledStream.Dispose();
            _controlStream.Dispose();
        }

        private DeviceInfo ReadDeviceInfo()
        {
            byte[] buffer = new byte[8];
            buffer[0] = 0x0F;
            _controlStream.GetFeature(buffer);

            int versionMajor = buffer[2] / 100;
            int versionMinor = buffer[2] % 100;

            return new DeviceInfo(
                $"{versionMajor}.{versionMinor:00}",
                versionMajor,
                versionMinor,
                buffer[6]);
        }

        private bool IsBigEndianDirectMode()
        {
            return _deviceInfo.VersionMajor >= 1 && _deviceInfo.VersionMinor >= 16;
        }

        private void EnableDirectMode(bool enabled)
        {
            byte[] buffer = [0x15, 0x00, enabled ? (byte)1 : (byte)0];
            _controlStream.SetFeature(buffer);
        }

        private void WaitUntilReady()
        {
            byte[] buffer = new byte[3];
            buffer[0] = 0x04;

            for (int attempt = 0; attempt < 100; attempt++)
            {
                _controlStream.GetFeature(buffer);
                if (buffer[1] == 1)
                {
                    return;
                }

                if (attempt != 99)
                {
                    Thread.Sleep(25);
                }
            }
        }

        private void SendColors(IReadOnlyList<LedColor> colors)
        {
            const int packetLength = 436;
            const int columnLength = 12;

            int packetCount = (int)Math.Ceiling(packetLength / 64d);
            byte[][] packets = Enumerable.Range(0, packetCount)
                .Select(_ => new byte[65])
                .ToArray();

            packets[0][1] = 0xA1;
            packets[0][2] = 0x01;

            const int headerLength = 4;
            if (IsBigEndianDirectMode())
            {
                packets[0][3] = (byte)(packetLength >> 8);
                packets[0][4] = (byte)(packetLength & 0xFF);
            }
            else
            {
                packets[0][3] = (byte)(packetLength & 0xFF);
                packets[0][4] = (byte)(packetLength >> 8);
            }

            for (int ledIndex = 0; ledIndex < colors.Count; ledIndex++)
            {
                int hardwareLedId = _logicalToHardwareLedIds[ledIndex];
                int column = hardwareLedId / columnLength;
                int row = hardwareLedId % columnLength;
                int offset = (column * 3 * columnLength) + row + headerLength;

                WritePacketColor(packets, offset, colors[ledIndex].Red);
                offset += columnLength;
                WritePacketColor(packets, offset, colors[ledIndex].Green);
                offset += columnLength;
                WritePacketColor(packets, offset, colors[ledIndex].Blue);
            }

            foreach (byte[] packet in packets)
            {
                _ledStream.Write(packet);
            }

            ClearResponses();
            AwaitResponse(20);
        }

        private static void WritePacketColor(byte[][] packets, int offset, byte value)
        {
            int packetIndex = offset / 64;
            int packetOffset = (offset % 64) + 1;
            if (packetIndex >= 0 && packetIndex < packets.Length)
            {
                packets[packetIndex][packetOffset] = value;
            }
        }

        private static int[] BuildLedMap(int layoutType)
        {
            int[,] source = layoutType == RoccatLayoutUs
                ? UsLayoutMatrix
                : IsoLayoutMatrix;

            var ledIds = new List<int>(FullLedCount);
            var seen = new HashSet<int>();

            for (int row = 0; row < source.GetLength(0); row++)
            {
                for (int column = 0; column < source.GetLength(1); column++)
                {
                    int ledId = source[row, column];
                    if (ledId != MissingLed && seen.Add(ledId))
                    {
                        ledIds.Add(ledId);
                    }
                }
            }

            for (int ledId = 0; ledId < FullLedCount; ledId++)
            {
                if (seen.Add(ledId))
                {
                    ledIds.Add(ledId);
                }
            }

            return ledIds.ToArray();
        }

        private void AwaitResponse(int timeoutMilliseconds)
        {
            byte[] buffer = new byte[65];

            try
            {
                _ledStream.ReadTimeout = timeoutMilliseconds;
                _ledStream.ReadAtLeast(buffer, 1, throwOnEndOfStream: false);
            }
            catch (TimeoutException)
            {
            }
        }

        private void ClearResponses()
        {
            byte[] buffer = new byte[65];

            while (true)
            {
                try
                {
                    _ledStream.ReadTimeout = 1;
                    int bytesRead = _ledStream.ReadAtLeast(buffer, 1, throwOnEndOfStream: false);
                    if (bytesRead <= 0)
                    {
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    return;
                }
            }
        }

        private sealed record DeviceInfo(
            string Version,
            int VersionMajor,
            int VersionMinor,
            int LayoutType);
    }
}