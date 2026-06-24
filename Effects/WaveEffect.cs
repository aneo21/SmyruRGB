namespace SmyruRGB.Effects;

internal sealed class WaveEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("wavelength", "Wavelength", "Distance between wave peaks measured in LEDs.", 2, 40, 12, "Motion"),
        new("contrast", "Contrast", "How strong the wave intensity difference should be.", 10, 100, 70, "Brightness")
    ];

    public string Name => "Wave";

    public string Description => "Creates a brightness wave that travels across individual LEDs.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        double phaseStep = 0.08 + (context.Speed / 250.0);
        double wavelength = Math.Max(2, context.GetParameterValue("wavelength", 12));
        double contrast = context.GetParameterValue("contrast", 70) / 100.0;

        return EffectFrameFactory.CreateMappedFrame(
            context.LedCountsPerChannel,
            (_, _, flatIndex) =>
            {
                double phase = (context.Tick * phaseStep) + ((flatIndex / wavelength) * Math.PI);
                double wave = (Math.Sin(phase) + 1.0) / 2.0;
                double intensity = (1.0 - contrast) + (wave * contrast);
                return context.BaseColor.Scale(Math.Clamp(intensity, 0.1, 1.0));
            },
            Math.Max(14, 72 - context.Speed / 2));
    }
}