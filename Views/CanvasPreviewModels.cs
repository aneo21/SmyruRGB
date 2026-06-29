using Avalonia;
using Avalonia.Media;

namespace SmyruRGB.Views;

public sealed class CanvasPreviewSceneSnapshot
{
    public CanvasPreviewSceneSnapshot(
        IReadOnlyList<CanvasPreviewBackgroundCellSnapshot> backgroundCells,
        IReadOnlyList<CanvasPreviewDeviceSnapshot> devices)
    {
        BackgroundCells = backgroundCells;
        Devices = devices;
    }

    public IReadOnlyList<CanvasPreviewBackgroundCellSnapshot> BackgroundCells { get; }

    public IReadOnlyList<CanvasPreviewDeviceSnapshot> Devices { get; }
}

public sealed class CanvasPreviewBackgroundCellSnapshot
{
    public CanvasPreviewBackgroundCellSnapshot(Rect bounds, Color color)
    {
        Bounds = bounds;
        Color = color;
    }

    public Rect Bounds { get; }

    public Color Color { get; }
}

public sealed class CanvasPreviewDeviceSnapshot
{
    public CanvasPreviewDeviceSnapshot(string deviceIdentifier, string deviceName, Rect bounds, IReadOnlyList<CanvasPreviewChannelSnapshot> channels)
    {
        DeviceIdentifier = deviceIdentifier;
        DeviceName = deviceName;
        Bounds = bounds;
        Channels = channels;
    }

    public string DeviceIdentifier { get; }

    public string DeviceName { get; }

    public Rect Bounds { get; }

    public IReadOnlyList<CanvasPreviewChannelSnapshot> Channels { get; }
}

public sealed class CanvasPreviewChannelSnapshot
{
    public CanvasPreviewChannelSnapshot(
        string channelLabel,
        string channelName,
        string shapeName,
        ChannelDeviceLayout layout,
        Rect bounds,
        IReadOnlyList<Color> ledColors)
    {
        ChannelLabel = channelLabel;
        ChannelName = channelName;
        ShapeName = shapeName;
        Layout = layout;
        Bounds = bounds;
        LedColors = ledColors;
    }

    public string ChannelLabel { get; }

    public string ChannelName { get; }

    public string ShapeName { get; }

    public ChannelDeviceLayout Layout { get; }

    public Rect Bounds { get; }

    public IReadOnlyList<Color> LedColors { get; }

    public int LedCount => LedColors.Count;
}

public readonly record struct CanvasDeviceMoveRequest(string DeviceIdentifier, double Left, double Top);