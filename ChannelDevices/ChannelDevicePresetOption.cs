namespace SmyruRGB;

public sealed class ChannelDevicePresetOption
{
    public ChannelDevicePresetOption(
        string id,
        string displayName,
        string defaultName,
        int defaultLedCount,
        string defaultShapeId,
        bool isCustom = false,
        KeyboardTemplate? keyboardLayout = null)
    {
        Id = id;
        DisplayName = displayName;
        DefaultName = defaultName;
        DefaultLedCount = defaultLedCount;
        DefaultShapeId = defaultShapeId;
        IsCustom = isCustom;
        KeyboardLayout = keyboardLayout;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string DefaultName { get; }

    public int DefaultLedCount { get; }

    public string DefaultShapeId { get; }

    public ChannelDeviceShapeOption DefaultShape => ChannelDeviceShapeCatalog.FindById(DefaultShapeId);

    public ChannelDeviceLayout CreateLayout(int ledCount) => ChannelDeviceLayoutFactory.Create(DefaultShape.ShapeKind, ledCount);

    public bool IsCustom { get; }

    /// <summary>
    /// Opcjonalny template klawiatury. Jeśli nie null, będzie użyty do renderowania zamiast auto-grid.
    /// </summary>
    public KeyboardTemplate? KeyboardLayout { get; }
}