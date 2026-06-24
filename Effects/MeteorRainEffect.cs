namespace SmyruRGB.Effects;

internal sealed class MeteorRainEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("tailLength", "Tail length", "How many LEDs remain lit behind the meteor head.", 2, 80, 18, "Trail"),
        new("decay", "Trail fade", "How quickly the meteor tail fades into darkness.", 20, 95, 65, "Trail"),
        new("spacing", "Loop spacing", "How much dark space remains before the next meteor pass.", 0, 80, 18, "Motion")
    ];

    public string Name => "Meteor rain";

    public string Description => "Sends a bright meteor with a fading tail through each channel.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int spacing = Math.Max(0, context.GetParameterValue("spacing", 18));
        int motionStep = Math.Max(1, 1 + (context.Speed / 14));
        double decay = Math.Clamp(context.GetParameterValue("decay", 65) / 100.0, 0.2, 0.95);

        return EffectFrameFactory.CreatePerChannelFrame(
            context.LedCountsPerChannel,
            (channelIndex, ledCount) =>
            {
                if (ledCount == 0)
                {
                    return [];
                }

                int tailLength = Math.Min(Math.Max(2, context.GetParameterValue("tailLength", 18)), ledCount);
                int cycleLength = ledCount + tailLength + spacing;
                int head = (int)((context.Tick * motionStep + (channelIndex * (tailLength / 2 + 1))) % Math.Max(1, cycleLength));

                LedColor[] leds = new LedColor[ledCount];
                for (int ledIndex = 0; ledIndex < ledCount; ledIndex++)
                {
                    int distance = head - ledIndex;
                    if (distance < 0 || distance >= tailLength)
                    {
                        leds[ledIndex] = context.EffectiveBackgroundColor;
                        continue;
                    }

                    double normalizedDistance = distance / (double)Math.Max(1, tailLength - 1);
                    double intensity = Math.Pow(1.0 - normalizedDistance, 1.0 + (decay * 2.0));
                    leds[ledIndex] = context.BaseColor.Scale(Math.Clamp(intensity, 0.0, 1.0));
                }

                return leds;
            },
            Math.Max(16, 72 - context.Speed / 2));
    }
}