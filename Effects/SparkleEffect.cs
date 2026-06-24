namespace SmyruRGB.Effects;

internal sealed class SparkleEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("density", "Density", "How often bright sparkles appear along the LEDs.", 2, 30, 10, "Sparkles"),
        new("background", "Background", "Brightness of the base glow under the sparkles.", 0, 60, 18, "Brightness")
    ];

    public string Name => "Sparkle";

    public string Description => "Adds bright sparkles over a dimmer base color across all LEDs.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int density = Math.Max(2, context.GetParameterValue("density", 10));
        double background = context.GetParameterValue("background", 18) / 100.0;
        int sparkStep = Math.Max(1, 14 - (context.Speed / 8));

        return EffectFrameFactory.CreateMappedFrame(
            context.LedCountsPerChannel,
            (channelIndex, _, flatIndex) =>
            {
                bool sparkle = ((flatIndex + (int)context.Tick * sparkStep + (channelIndex * 3)) % density) == 0;
                return sparkle ? context.BaseColor : context.EffectiveBackgroundColor.Scale(background);
            },
            Math.Max(18, 85 - context.Speed));
    }
}