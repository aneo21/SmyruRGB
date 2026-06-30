namespace SmyruRGB;

internal static class ChannelDevicePresetCatalog
{
    public const string CustomPresetId = "custom";
    public const string RoccatVulcan121AimoPresetId = "roccat-vulcan-121-aimo";

    private const string RoccatDeviceIdentifierPrefix = "ROCCAT-VULCAN-AIMO";

    private static readonly ChannelDevicePresetOption SmxOroshiPreset = SmxOroshi120MmPwmArgbPreset.Create();
    private static readonly ChannelDevicePresetOption RoccatVulcan121AimoPresetOption = RoccatVulcan121AimoPreset.Create();

    private static readonly ChannelDevicePresetOption CustomPreset = new(
        CustomPresetId,
        "Custom",
        string.Empty,
        1,
        ChannelDeviceShapeCatalog.BarShapeId,
        isCustom: true);

    private static readonly IReadOnlyList<ChannelDevicePresetOption> Presets =
    [
        SmxOroshiPreset,
        RoccatVulcan121AimoPresetOption,
        CustomPreset
    ];

    private static readonly IReadOnlyList<ChannelDevicePresetOption> RoccatPresets =
    [
        RoccatVulcan121AimoPresetOption
    ];

    public static IReadOnlyList<ChannelDevicePresetOption> All => Presets;

    public static bool IsRoccatDevice(string? deviceIdentifier)
    {
        return !string.IsNullOrWhiteSpace(deviceIdentifier)
            && deviceIdentifier.StartsWith(RoccatDeviceIdentifierPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ChannelDevicePresetOption> GetAvailableForDevice(string? deviceIdentifier)
    {
        return IsRoccatDevice(deviceIdentifier) ? RoccatPresets : Presets;
    }

    public static ChannelDevicePresetOption GetDefaultForDevice(string? deviceIdentifier)
    {
        return IsRoccatDevice(deviceIdentifier) ? RoccatVulcan121AimoPresetOption : CustomPreset;
    }

    public static ChannelDevicePresetOption NormalizeForDevice(string? presetId, string? deviceIdentifier)
    {
        IReadOnlyList<ChannelDevicePresetOption> availablePresets = GetAvailableForDevice(deviceIdentifier);
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            ChannelDevicePresetOption? preset = availablePresets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, presetId, StringComparison.OrdinalIgnoreCase));

            if (preset is not null)
            {
                return preset;
            }
        }

        return GetDefaultForDevice(deviceIdentifier);
    }

    public static ChannelDevicePresetOption FindById(string? presetId)
    {
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            ChannelDevicePresetOption? preset = Presets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, presetId, StringComparison.OrdinalIgnoreCase));

            if (preset is not null)
            {
                return preset;
            }
        }

        return CustomPreset;
    }
}