namespace SmyruRGB.Effects;

internal sealed class TwinkleRainbowEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("density", "Density", "How many LEDs join the twinkle peaks at once.", 5, 45, 16, "Twinkle"),
        new("background", "Background glow", "Base rainbow brightness between twinkles.", 0, 40, 8, "Brightness"),
        new("spread", "Hue spread", "Hue distance between neighboring LEDs.", 4, 40, 14, "Color")
    ];

    public string Name => "Twinkle rainbow";

    public string Description => "Combines shifting rainbow hues with fast glitter-like twinkles.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => false;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int density = Math.Max(5, context.GetParameterValue("density", 16));
        double background = context.GetParameterValue("background", 8) / 100.0;
        double spread = context.GetParameterValue("spread", 14);
        double speedPhase = 0.08 + (context.Speed / 150.0);
        double hueShift = context.Tick * (1.0 + (context.Speed * 0.08));

        return EffectFrameFactory.CreateMappedFrame(
            context.LedCountsPerChannel,
            (channelIndex, ledIndex, flatIndex) =>
            {
                double hue = hueShift + (flatIndex * spread);
                double wave = (Math.Sin((context.Tick * speedPhase) + (flatIndex * 0.9) + (channelIndex * 0.6) + (ledIndex * 0.35)) + 1.0) / 2.0;
                bool twinkle = ((flatIndex + channelIndex + ((int)context.Tick * 3)) % density) == 0 || wave > 0.88;
                double brightness = twinkle ? Math.Max(0.75, wave) : background + (wave * 0.18);
                double saturation = twinkle ? 0.45 : 1.0;
                return ColorMath.FromHsv(hue, saturation, Math.Clamp(brightness, 0.0, 1.0));
            },
            Math.Max(16, 68 - context.Speed / 2));
    }
}