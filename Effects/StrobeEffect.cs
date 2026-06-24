namespace SmyruRGB.Effects;

internal sealed class StrobeEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("dutyCycle", "Duty cycle", "How long the flash stays on in each pulse.", 5, 90, 25, "Timing")
    ];

    public string Name => "Strobe";

    public string Description => "Flashes the selected color on and off for a sharp strobe effect.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int dutyCycle = context.GetParameterValue("dutyCycle", 25);
        int window = Math.Max(2, 10 - (context.Speed / 12));
        int onFrames = Math.Max(1, (int)Math.Round(window * (dutyCycle / 100.0)));
        bool isOn = context.Tick % window < onFrames;
        LedColor color = isOn ? context.BaseColor : context.EffectiveBackgroundColor;
        return EffectFrameFactory.CreateSolidFrame(context.LedCountsPerChannel, color, Math.Max(20, 95 - context.Speed));
    }
}