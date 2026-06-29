namespace SmyruRGB.Effects;

internal sealed class ScannerEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("tail", "Tail", "How long the scanner trail remains visible.", 2, 32, 8, "Motion")
    ];

    public string Name => "Scanner";

    public string Description => "Moves a scanning eye with a fading tail through each LED strip.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int tail = Math.Max(2, context.GetParameterValue("tail", 8));
        int motionStep = Math.Max(1, context.Speed / 12);

        return EffectFrameFactory.CreatePerChannelFrame(
            context.LedCountsPerChannel,
            (channelIndex, ledCount) =>
            {
                if (ledCount == 0)
                {
                    return [];
                }

                double cycleLength = Math.Max(1, (ledCount * 2) - 2);
                double rawPosition = (context.Tick * motionStep) % cycleLength;
                double axisHead = rawPosition < ledCount
                    ? rawPosition
                    : cycleLength - rawPosition;
                double normalizedTail = Math.Max(0.0001, tail);

                LedColor[] leds = new LedColor[ledCount];
                for (int ledIndex = 0; ledIndex < ledCount; ledIndex++)
                {
                    EffectLedLocation location = context.GetLedLocation(channelIndex, ledIndex);
                    double axisPosition = location.AxisPosition;
                    double distance = Math.Abs(axisHead - axisPosition);
                    if (distance >= normalizedTail)
                    {
                        leds[ledIndex] = context.EffectiveBackgroundColor;
                        continue;
                    }

                    double fade = 1.0 - (distance / normalizedTail);
                    leds[ledIndex] = context.BaseColor.Scale(Math.Max(0.08, fade));
                }

                return leds;
            },
            Math.Max(16, 78 - context.Speed / 2));
    }
}