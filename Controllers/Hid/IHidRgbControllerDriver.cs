using HidSharp;
using SmyruRGB.Effects;

namespace SmyruRGB.Controllers.Hid;

internal interface IHidRgbControllerDriver
{
    bool Matches(HidDevice device);
    string BuildDeviceIdentifier(HidDevice device);
    string BuildDeviceLabel(HidDevice device);
    string BuildDeviceSummary(HidDevice device);
    bool TryOpen(HidDevice device, out IHidRgbControllerSession? session);
}

internal interface IHidRgbControllerSession : IDisposable
{
    string DeviceName { get; }
    int VendorId { get; }
    int ProductId { get; }
    string DevicePath { get; }
    int ChannelCount { get; }
    int MaxLedsPerChannel { get; }
    IReadOnlyList<HidRgbChannelInfo> Channels { get; }
    bool TrySetLedFrames(IReadOnlyList<IReadOnlyList<LedColor>> channelFrames, IReadOnlyList<int> ledCountsPerChannel, out string message);
}

internal sealed record HidRgbChannelInfo(
    int ChannelNumber,
    string ChannelName,
    int DefaultLedCount,
    int MaxLedCount);