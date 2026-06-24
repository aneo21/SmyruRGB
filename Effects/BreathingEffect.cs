namespace SmyruRGB.Effects;

internal sealed class BreathingEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("minimumBrightness", "Minimum brightness", "How dim the color gets at the bottom of the pulse.", 0, 80, 15, "Brightness")
    ];

    public string Name => "Breathing";

    public string Description => "Fades the chosen color in and out smoothly across all channels.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        double minimumBrightness = context.GetParameterValue("minimumBrightness", 15) / 100.0;
        double phaseStep = 0.05 + (context.Speed / 180.0);
        double intensity = minimumBrightness + (((Math.Sin(context.Tick * phaseStep) + 1.0) / 2.0) * (1.0 - minimumBrightness));
        LedColor color = context.BaseColor.Scale(intensity);
        int delay = Math.Max(18, 85 - context.Speed);
        return EffectFrameFactory.CreateSolidFrame(context.LedCountsPerChannel, color, delay);
    }
}