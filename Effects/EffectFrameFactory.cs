namespace SmyruRGB.Effects;

internal static class EffectFrameFactory
{
    public static EffectFrame CreateSolidFrame(IReadOnlyList<int> ledCountsPerChannel, LedColor color, int delayMilliseconds)
    {
        return CreateMappedFrame(ledCountsPerChannel, (_, _, _) => color, delayMilliseconds);
    }

    public static EffectFrame CreateMappedFrame(IReadOnlyList<int> ledCountsPerChannel, Func<int, int, int, LedColor> colorSelector, int delayMilliseconds)
    {
        LedColor[][] channels = new LedColor[ledCountsPerChannel.Count][];
        int flatIndex = 0;

        for (int channelIndex = 0; channelIndex < ledCountsPerChannel.Count; channelIndex++)
        {
            int ledCount = ledCountsPerChannel[channelIndex];
            LedColor[] leds = new LedColor[ledCount];

            for (int ledIndex = 0; ledIndex < ledCount; ledIndex++)
            {
                leds[ledIndex] = colorSelector(channelIndex, ledIndex, flatIndex);
                flatIndex++;
            }

            channels[channelIndex] = leds;
        }

        return new EffectFrame(channels, delayMilliseconds);
    }

    public static EffectFrame CreatePerChannelFrame(IReadOnlyList<int> ledCountsPerChannel, Func<int, int, IReadOnlyList<LedColor>> channelBuilder, int delayMilliseconds)
    {
        IReadOnlyList<LedColor>[] channels = new IReadOnlyList<LedColor>[ledCountsPerChannel.Count];

        for (int channelIndex = 0; channelIndex < ledCountsPerChannel.Count; channelIndex++)
        {
            channels[channelIndex] = channelBuilder(channelIndex, ledCountsPerChannel[channelIndex]);
        }

        return new EffectFrame(channels, delayMilliseconds);
    }
}