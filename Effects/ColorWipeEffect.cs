namespace SmyruRGB.Effects;

internal sealed class ColorWipeEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("tailLength", "Tail length", "How many LEDs stay lit in the moving wipe.", 1, 60, 12, "Motion")
    ];

    public string Name => "Color wipe";

    public string Description => "Moves a lit wipe across all LEDs channel by channel.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        List<(int ChannelIndex, int LedIndex)> orderedLeds = BuildOrderedLedList(context);
        int totalLeds = orderedLeds.Count;
        int tailLength = Math.Min(Math.Max(1, context.GetParameterValue("tailLength", 12)), Math.Max(1, totalLeds));
        int cursor = totalLeds == 0 ? 0 : (int)(context.Tick % (totalLeds + tailLength));

        IReadOnlyList<LedColor>[] channels = context.LedCountsPerChannel
            .Select(count => Enumerable.Repeat(context.EffectiveBackgroundColor, count).ToArray() as IReadOnlyList<LedColor>)
            .ToArray();

        int start = Math.Max(0, cursor - tailLength + 1);
        int end = Math.Min(cursor, totalLeds - 1);
        for (int index = start; index <= end; index++)
        {
            (int channelIndex, int ledIndex) = orderedLeds[index];
            LedColor[] channel = (LedColor[])channels[channelIndex];
            channel[ledIndex] = context.BaseColor;
        }

        return new EffectFrame(channels, Math.Max(25, 125 - context.Speed));
    }

    private static List<(int ChannelIndex, int LedIndex)> BuildOrderedLedList(EffectContext context)
    {
        var ordered = new List<(int ChannelIndex, int LedIndex)>();

        for (int channelIndex = 0; channelIndex < context.LedCountsPerChannel.Count; channelIndex++)
        {
            int ledCount = context.LedCountsPerChannel[channelIndex];
            IEnumerable<(int ChannelIndex, int LedIndex, EffectLedLocation Location)> channelOrder =
                Enumerable.Range(0, ledCount)
                    .Select(ledIndex => (channelIndex, ledIndex, context.GetLedLocation(channelIndex, ledIndex)))
                    .OrderBy(item => item.Item3.AxisPosition)
                    .ThenBy(item => item.Item3.Y)
                    .ThenBy(item => item.Item3.X)
                    .ThenBy(item => item.ledIndex);

            ordered.AddRange(channelOrder.Select(item => (item.ChannelIndex, item.LedIndex)));
        }

        return ordered;
    }
}