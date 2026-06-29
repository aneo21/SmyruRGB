namespace SmyruRGB;

public sealed class ChannelDevicePresetOption
{
    public ChannelDevicePresetOption(
        string id,
        string displayName,
        string defaultName,
        int defaultLedCount,
        string defaultShapeId,
        bool isCustom = false)
    {
        Id = id;
        DisplayName = displayName;
        DefaultName = defaultName;
        DefaultLedCount = defaultLedCount;
        DefaultShapeId = defaultShapeId;
        IsCustom = isCustom;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string DefaultName { get; }

    public int DefaultLedCount { get; }

    public string DefaultShapeId { get; }

    public ChannelDeviceShapeOption DefaultShape => ChannelDeviceShapeCatalog.FindById(DefaultShapeId);

    public ChannelDeviceLayout CreateLayout(int ledCount) => ChannelDeviceLayoutFactory.Create(DefaultShape.ShapeKind, ledCount);

    public bool IsCustom { get; }
}