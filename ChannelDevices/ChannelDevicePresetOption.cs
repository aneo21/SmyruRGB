namespace SmyruRGB;

public sealed class ChannelDevicePresetOption
{
    public ChannelDevicePresetOption(string id, string displayName, string defaultName, int defaultLedCount, bool isCustom = false)
    {
        Id = id;
        DisplayName = displayName;
        DefaultName = defaultName;
        DefaultLedCount = defaultLedCount;
        IsCustom = isCustom;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string DefaultName { get; }

    public int DefaultLedCount { get; }

    public bool IsCustom { get; }
}