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
            (_, ledCount) =>
            {
                if (ledCount == 0)
                {
                    return [];
                }

                int cycle = Math.Max(1, (ledCount * 2) - 2);
                int rawPosition = (int)((context.Tick * motionStep) % cycle);
                int head = rawPosition < ledCount ? rawPosition : cycle - rawPosition;

                LedColor[] leds = new LedColor[ledCount];
                for (int ledIndex = 0; ledIndex < ledCount; ledIndex++)
                {
                    int distance = Math.Abs(head - ledIndex);
                    if (distance >= tail)
                    {
                        leds[ledIndex] = context.EffectiveBackgroundColor;
                        continue;
                    }

                    double fade = 1.0 - (distance / (double)tail);
                    leds[ledIndex] = context.BaseColor.Scale(Math.Max(0.08, fade));
                }

                return leds;
            },
            Math.Max(16, 78 - context.Speed / 2));
    }
}