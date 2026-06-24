namespace SmyruRGB.Effects;

internal sealed class FireEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("cooling", "Cooling", "How quickly the flames darken toward the top of the strip.", 20, 95, 58, "Heat"),
        new("sparking", "Sparking", "How often bright embers flare near the origin.", 2, 35, 12, "Heat"),
        new("drift", "Drift", "How quickly the fire climbs and changes shape.", 1, 20, 8, "Motion")
    ];

    public string Name => "Fire";

    public string Description => "Builds a warm fire palette with flickering embers and rising heat.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => false;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        double cooling = context.GetParameterValue("cooling", 58) / 100.0;
        int sparking = Math.Max(2, context.GetParameterValue("sparking", 12));
        double drift = 0.03 + (context.GetParameterValue("drift", 8) / 150.0);
        double speedBoost = 0.4 + (context.Speed / 90.0);

        return EffectFrameFactory.CreatePerChannelFrame(
            context.LedCountsPerChannel,
            (channelIndex, ledCount) =>
            {
                if (ledCount == 0)
                {
                    return [];
                }

                LedColor[] leds = new LedColor[ledCount];
                for (int ledIndex = 0; ledIndex < ledCount; ledIndex++)
                {
                    double normalizedHeight = ledCount == 1 ? 0.0 : ledIndex / (double)(ledCount - 1);
                    double body = Math.Max(0.0, 1.0 - (normalizedHeight * (1.2 + cooling)));
                    double flicker = (Math.Sin((context.Tick * drift * speedBoost) + (channelIndex * 0.8) + (ledIndex * 0.55)) + 1.0) / 2.0;
                    double turbulence = (Math.Sin((context.Tick * drift * speedBoost * 2.1) + (ledIndex * 1.35) + (channelIndex * 1.7)) + 1.0) / 2.0;
                    bool hasSpark = ledIndex < Math.Max(2, ledCount / 5)
                        && ((ledIndex + channelIndex + ((int)context.Tick * Math.Max(1, sparking / 3))) % sparking) == 0;

                    double heat = body * (0.45 + (0.55 * flicker)) + (turbulence * 0.18) + (hasSpark ? 0.35 : 0.0);
                    leds[ledIndex] = CreateFireColor(Math.Clamp(heat, 0.0, 1.0));
                }

                return leds;
            },
            Math.Max(18, 80 - context.Speed / 2));
    }

    private static LedColor CreateFireColor(double heat)
    {
        double hue = 8.0 + (42.0 * Math.Pow(heat, 0.82));
        double saturation = Math.Clamp(1.0 - (heat * 0.22), 0.55, 1.0);
        double value = Math.Clamp(0.08 + (heat * 0.92), 0.0, 1.0);
        return ColorMath.FromHsv(hue, saturation, value);
    }
}