namespace SmyruRGB.Effects;

internal sealed class RainbowEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("spread", "Spread", "Hue distance between neighboring LEDs.", 2, 45, 12, "Color")
    ];

    public string Name => "Rainbow";

    public string Description => "Cycles a rainbow across individual LEDs and channels.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => false;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        double baseHue = (context.Tick * (0.8 + (context.Speed * 0.12))) % 360.0;
        double spread = context.GetParameterValue("spread", 12);

        return EffectFrameFactory.CreateMappedFrame(
            context.LedCountsPerChannel,
            (channelIndex, ledIndex, _) =>
            {
                EffectLedLocation location = context.GetLedLocation(channelIndex, ledIndex);
                double hueOffset = location.AxisPosition * spread;
                return ColorMath.FromHsv(baseHue + hueOffset, 1.0, 1.0);
            },
            Math.Max(12, 70 - context.Speed / 2));
    }
}