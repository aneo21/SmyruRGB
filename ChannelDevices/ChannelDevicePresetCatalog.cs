namespace SmyruRGB;

internal static class ChannelDevicePresetCatalog
{
    public const string CustomPresetId = "custom";

    private static readonly ChannelDevicePresetOption CustomPreset = new(
        CustomPresetId,
        "Custom",
        string.Empty,
        1,
        ChannelDeviceShapeCatalog.BarShapeId,
        isCustom: true);

    private static readonly IReadOnlyList<ChannelDevicePresetOption> Presets =
    [
        SmxOroshi120MmPwmArgbPreset.Create(),
        CustomPreset
    ];

    public static IReadOnlyList<ChannelDevicePresetOption> All => Presets;

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